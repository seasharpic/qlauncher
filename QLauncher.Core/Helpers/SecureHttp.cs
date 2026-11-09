using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace MinecraftLauncher.Helpers
{
    /// <summary>
    /// Единая фабрика HttpClient для лаунчера.
    ///
    /// Раньше TLS-валидация отключалась через ServerCertificateCustomValidationCallback
    /// в JavaService и AntiBlockService. Для канала, который скачивает и затем ЗАПУСКАЕТ
    /// JRE, это означало удалённое выполнение кода: любой, кто отвечает на DNS/маршрут
    /// (чужой Wi-Fi, роутер, подмена), мог подсунуть свой ZIP с javaw.exe.
    ///
    /// Здесь валидация всегда включена. Для случаев, где провайдер режет прямой доступ,
    /// предусмотрен FallbackAsync: он пробует зеркала последовательно, но каждое
    /// соединение остаётся проверенным.
    /// </summary>
    public static class SecureHttp
    {
        private static readonly Lazy<HttpClient> _shared = new(() =>
        {
            var client = new HttpClient(CreateHandler());
            client.Timeout = TimeSpan.FromMinutes(5);
            return client;
        }, isThreadSafe: true);

        /// <summary>
        /// Общий клиент с включённой проверкой сертификатов. Переиспользуйте его,
        /// а не создавайте HttpClient на каждый запрос.
        /// </summary>
        public static HttpClient Shared => _shared.Value;

        /// <summary>
        /// Создаёт обработчик с включённой TLS-валидацией.
        /// Автоматическое сжатие оставлено — на нём нет последствий для безопасности.
        /// </summary>
        public static HttpClientHandler CreateHandler()
        {
            return new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                // ServerCertificateCustomValidationCallback намеренно НЕ задаётся:
                // поведение по умолчанию — строгая проверка сертификатов.
            };
        }

        /// <summary>
        /// Создаёт клиент с браузерными заголовками. Нужен для сайтов, которые режут
        /// запросы без нормального User-Agent (например, предпросмотр Telegram-канала).
        /// Замена User-Agent здесь безопасна: это заголовок, а не отключение проверки сертификата.
        /// </summary>
        public static HttpClient CreateBrowserLikeClient(TimeSpan? timeout = null)
        {
            var client = new HttpClient(CreateHandler());
            if (timeout.HasValue) client.Timeout = timeout.Value;

            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");

            return client;
        }

        /// <summary>
        /// Пробует каждый URL по очереди, возвращает первые успешные байты.
        /// Каждое соединение проверяет сертификат. Таймаут покрывает и подключение,
        /// и чтение тела — это важно для крупных файлов на медленной сети.
        /// </summary>
        public static async Task<byte[]?> TryDownloadBytesAsync(
            IEnumerable<string?> urls,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            IProgress<double>? progress = null)
        {
            foreach (var url in urls)
            {
                if (string.IsNullOrWhiteSpace(url)) continue;

                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(timeout);

                    using var response = await Shared.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    if (!response.IsSuccessStatusCode) continue;

                    // Тело читается чанками с учётом лимита скорости.
                    // Раньше здесь был ReadAsByteArrayAsync: он забирал файл
                    // целиком на полной скорости, и настройка ограничения
                    // на загрузку ресурсов и библиотек не действовала.
                    await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);

                    using var buffer = new MemoryStream();
                    byte[] chunk = System.Buffers.ArrayPool<byte>.Shared.Rent(81920);
                    try
                    {
                        int read;
                        while ((read = await stream.ReadAsync(chunk, cts.Token)) > 0)
                        {
                            await buffer.WriteAsync(chunk.AsMemory(0, read), cts.Token);

                            if (progress != null && response.Content.Headers.ContentLength is > 0)
                            {
                                progress.Report((double)buffer.Length / response.Content.Headers.ContentLength!.Value);
                            }

                            await BandwidthLimiter.ThrottleAsync(read, cts.Token);
                        }
                    }
                    finally
                    {
                        System.Buffers.ArrayPool<byte>.Shared.Return(chunk);
                    }

                    return buffer.ToArray();
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Таймаут этого URL — пробуем следующий.
                }
                catch (HttpRequestException)
                {
                    // Сеть недоступна для этого URL — пробуем следующий.
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Проверка целостности скачанных файлов.
    ///
    /// Раньше модели манифеста несли Sha1, но никто их не читал: JRE, client.jar,
    /// библиотеки, ассеты и моды устанавливались без сверки. Для исполняемых
    /// файлов это означало, что подмена проходит незамеченной.
    /// </summary>
    public static class HashHelper
    {
        /// <summary>
        /// Проверяет SHA-1 в hex-форме. Пустой или некорректный ожидаемый хеш
        /// трактуется как «проверять нечем» и возвращает false — чтобы вызывающий
        /// код мог решить сам, а не молча считать файл годным.
        /// </summary>
        public static bool VerifySha1(byte[] data, string? expectedHex)
        {
            if (!TryNormalizeHash(expectedHex, out byte[]? expected)) return false;
            return CryptographicOperations.FixedTimeEquals(ComputeSha1(data), expected);
        }

        public static byte[] ComputeSha1(byte[] data) => SHA1.HashData(data);

        /// <summary>
        /// Приводит хеш из base64 (формат Mojang/Modrinth) или hex к байтам.
        /// </summary>
        private static bool TryNormalizeHash(string? hash, out byte[]? expected)
        {
            expected = null;
            if (string.IsNullOrWhiteSpace(hash)) return false;

            string trimmed = hash.Trim();

            // Hex-форманта: 40 символов SHA-1 или 64 символа SHA-256.
            if (trimmed.Length is 40 or 64 && IsHex(trimmed))
            {
                byte[] bytes = new byte[trimmed.Length / 2];
                for (int i = 0; i < bytes.Length; i++)
                    bytes[i] = Convert.ToByte(trimmed.Substring(i * 2, 2), 16);

                expected = bytes;
                return true;
            }

            // Base64-форманта (Mojang отдаёт хеши в base64).
            try
            {
                expected = Convert.FromBase64String(trimmed);
                return expected.Length is 20 or 32;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static bool IsHex(string s)
        {
            foreach (char c in s)
            {
                bool isHexDigit = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHexDigit) return false;
            }
            return true;
        }
    }
}