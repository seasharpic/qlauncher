using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MinecraftLauncher.Helpers;

namespace MinecraftLauncher.Services
{
    /// <summary>
    /// Как выбирать между официальными источниками и зеркалами.
    ///
    /// Тип назван MirrorMode, а не MirrorPreference: в SettingsViewModel есть
    /// одноимённое строковое свойство MirrorPreference, и одноимённый тип
    /// разрешался бы там как поле строки, а не как перечисление.
    /// </summary>
    public enum MirrorMode
    {
        /// <summary>
        /// Проверить доступность при старте и поставить вперёд тот источник,
        /// который реально отвечает. Это то, что делает PineconeMC
        /// (PineconeNetworkCheck), и оно заметно ускоряет запуск в странах,
        /// где Mojang заблокирован: без проверки каждый запрос сначала
        /// упёрся бы в таймаут.
        /// </summary>
        Auto = 0,

        /// <summary>Всегда сначала официальный источник, зеркало только как запасной.</summary>
        OfficialFirst = 1,

        /// <summary>Всегда сначала зеркало.</summary>
        MirrorFirst = 2,

        /// <summary>
        /// Только официальные источники. Нужен пользователям, которые не хотят
        /// отдавать запросы третьим лицам; зеркала в этом режиме не используются.
        /// </summary>
        OfficialOnly = 3
    }

    /// <summary>
    /// Определение доступности источников загрузки.
    ///
    /// Идея перенесена из PineconeMC: лаунчер один раз проверяет, какие хосты
    /// отвечают, и строит список кандидатов в порядке, который сразу работает.
    ///
    /// Зачем это в QLauncher: раньше порядок был жёстко зашит — «сначала Mojang,
    /// потом bmclapi». В сети, где piston-meta недоступен, каждый запуск игры
    /// платил полный таймаут (8 секунд на манифест, 15 на client.jar) перед
    /// тем, как добраться до зеркала. Теперь после одной проверки официальный
    /// источник, который не отвечает, уходит в конец списка.
    ///
    /// Проверка идёт параллельно, с коротким таймаутом и методом HEAD. TLS
    /// валидируется всегда: подмена сертификата здесь позволила бы объявить
    /// недоступным официальный сервер и подсунуть свой ответ с зеркала.
    /// </summary>
    public sealed class MirrorService
    {
        /// <summary>Хост зеркала bmclapi2. Используется как маркер «это не официальный источник».</summary>
        public const string BmclapiHost = "bmclapi2.bangbang93.com";

        /// <summary>Хосты, доступность которых мы проверяем.</summary>
        private static readonly string[] KnownHosts =
        {
            "piston-meta.mojang.com",
            "piston-data.mojang.com",
            "resources.download.minecraft.net",
            "libraries.minecraft.net",
            "repo1.maven.org",
            "maven.fabricmc.net",
            "meta.fabricmc.net",
            "meta.quiltmc.net",
            "api.adoptium.net",
            "bmclapi2.bangbang93.com"
        };

        /// <summary>URL для HEAD-запроса: путь не важен, важен только факт ответа.</summary>
        private static readonly string[] ProbeUrls =
        {
            "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json",
            "https://piston-data.mojang.com/",
            "https://resources.download.minecraft.net/",
            "https://libraries.minecraft.net/",
            "https://repo1.maven.org/maven2/",
            "https://maven.fabricmc.net/",
            "https://meta.fabricmc.net/v2/versions/loader",
            "https://meta.quiltmc.net/v3/versions/loader",
            "https://api.adoptium.net/v3/info/available_releases",
            "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json"
        };

        private static readonly ConcurrentDictionary<string, bool> HostReachable = new(StringComparer.OrdinalIgnoreCase);
        private static readonly SemaphoreSlim ProbeLock = new(1, 1);

        private DateTime _lastProbeAt = DateTime.MinValue;
        private readonly TimeSpan _probeInterval = TimeSpan.FromMinutes(10);

        public static MirrorService Instance { get; } = new();

        public MirrorMode Preference { get; set; } = MirrorMode.Auto;

        /// <summary>Заполнен ли кэш хотя бы для одного хоста.</summary>
        public bool HasProbeResult => HostReachable.Count > 0;

        /// <summary>
        /// Проверяет доступность хостов. Результат кэшируется на 10 минут:
        /// сеть пользователя меняется редко, а проверка на каждом запуске игры
        /// означала бы десяток лишних запросов.
        /// </summary>
        public async Task ProbeAsync(CancellationToken cancellationToken = default)
        {
            await ProbeLock.WaitAsync(cancellationToken);
            try
            {
                if (DateTime.UtcNow - _lastProbeAt < _probeInterval && HasProbeResult)
                {
                    return;
                }

                var tasks = new List<Task<bool>>(ProbeUrls.Length);

                foreach (string url in ProbeUrls)
                {
                    tasks.Add(ProbeAsync(url, cancellationToken));
                }

                bool[] results = await Task.WhenAll(tasks);

                for (int i = 0; i < ProbeUrls.Length; i++)
                {
                    string host = GetHost(ProbeUrls[i]);
                    HostReachable[host] = results[i];
                }

                _lastProbeAt = DateTime.UtcNow;
                LogProbeResults();
            }
            catch (OperationCanceledException)
            {
                // Отмена при закрытии приложения — это не ошибка проверки.
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("MirrorService", "Mirror probe failed", ex);
            }
            finally
            {
                ProbeLock.Release();
            }
        }

        private static async Task<bool> ProbeAsync(string url, CancellationToken cancellationToken)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                // Короткий таймаут: смысл проверки — не ждать сеть, а быстро
                // понять, кто отвечает.
                cts.CancelAfter(TimeSpan.FromSeconds(4));

                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                using var response = await SecureHttp.Shared.SendAsync(request, cts.Token);

                // 4xx — хост жив, просто по этому пути нет содержимого.
                // Это не повод считать хост недоступным.
                return (int)response.StatusCode < 500;
            }
            catch
            {
                return false;
            }
        }

        private void LogProbeResults()
        {
            var reachable = new List<string>();
            var blocked = new List<string>();

            foreach (string host in KnownHosts)
            {
                if (HostReachable.TryGetValue(host, out bool ok))
                {
                    (ok ? reachable : blocked).Add(host);
                }
            }

            CrashLogWriter.Write(
                "MirrorService",
                $"Sources reachable: [{string.Join(", ", reachable)}]; unreachable: [{string.Join(", ", blocked)}]",
                null);
        }

        /// <summary>
        /// Строит список URL для загрузки в порядке, который с наибольшей
        /// вероятностью сработает сразу.
        ///
        /// officialUrl и mirrorUrl могут быть пустыми: пустые кандидаты
        /// отбрасываются, поэтому вызывающий код может передать только то,
        /// что есть.
        /// </summary>
        public IReadOnlyList<string> BuildCandidates(string officialUrl, string? mirrorUrl)
        {
            var candidates = new List<string>(2);

            string? officialHost = GetHostOrNull(officialUrl);
            string? mirrorHost = GetHostOrNull(mirrorUrl);

            // Пользовательский выбор важнее любых проверок сети.
            if (Preference == MirrorMode.OfficialOnly || mirrorHost == null)
            {
                if (!string.IsNullOrEmpty(officialUrl)) candidates.Add(officialUrl);
                return candidates;
            }

            if (Preference == MirrorMode.OfficialFirst || officialHost == null)
            {
                if (!string.IsNullOrEmpty(officialUrl)) candidates.Add(officialUrl);
                if (!string.IsNullOrEmpty(mirrorUrl)) candidates.Add(mirrorUrl);
                return candidates;
            }

            if (Preference == MirrorMode.MirrorFirst)
            {
                if (!string.IsNullOrEmpty(mirrorUrl)) candidates.Add(mirrorUrl);
                if (!string.IsNullOrEmpty(officialUrl)) candidates.Add(officialUrl);
                return candidates;
            }

            // MirrorMode.Auto: решение по результату проверки.
            bool officialOk = IsReachable(officialHost);
            bool mirrorOk = IsReachable(mirrorHost);

            // Зеркало вперёд только когда официальный источник заведомо не отвечает,
            // а зеркало отвечает. Случай «оба недоступны» тоже оставляем официальный
            // первым: тогда поведение совпадает с прежним, а сверка по хешу ниже по
            // цепочке в любом случае защищает от подмены содержимого.
            if (mirrorOk && !officialOk)
            {
                if (!string.IsNullOrEmpty(mirrorUrl)) candidates.Add(mirrorUrl);
                if (!string.IsNullOrEmpty(officialUrl)) candidates.Add(officialUrl);
            }
            else
            {
                if (!string.IsNullOrEmpty(officialUrl)) candidates.Add(officialUrl);
                if (!string.IsNullOrEmpty(mirrorUrl)) candidates.Add(mirrorUrl);
            }

            return candidates;
        }

        /// <summary>
        /// Известна ли доступность хоста. Пока проверка не выполнялась,
        /// считаем хост доступным, чтобы поведение совпадало с прежним
        /// (официальный источник первым) и не зависело от порядка старта.
        /// </summary>
        private static bool IsReachable(string host)
        {
            return !HostReachable.TryGetValue(host, out bool ok) || ok;
        }

        /// <summary>
        /// Человекочитаемое описание результата проверки для настроек.
        /// Показываем именно недоступные хосты: пользователю важно понимать,
        /// почему игра качает с зеркала, а не перечислять все рабочие адреса.
        /// </summary>
        /// <summary>
        /// Хосты, которые проверка признала недоступными, через запятую.
        /// Пустая строка означает «всё доступно» либо «проверка ещё не шла» —
        /// вызывающий код различает эти случаи по <see cref="HasProbeResult"/>.
        /// </summary>
        public static string DescribeBlockedHosts()
        {
            var blocked = new List<string>();

            foreach (string host in KnownHosts)
            {
                if (HostReachable.TryGetValue(host, out bool ok) && !ok)
                {
                    blocked.Add(host);
                }
            }

            return string.Join(", ", blocked);
        }

        /// <summary>
        /// Известна ли доступность хоста по результату последней проверки.
        /// Пока проверка не выполнялась, хост считается доступным.
        /// Вызывающий код может переставлять свои списки кандидатов по этому
        /// признаку, не зная деталей кэша.
        /// </summary>
        public static bool IsHostReachable(string host)
        {
            return IsReachable(host);
        }

        public static string GetHost(string url)
        {
            return GetHostOrNull(url) ?? url;
        }

        private static string? GetHostOrNull(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;
        }

        /// <summary>
        /// Заменяет официальный хост Mojang на зеркальный, сохраняя путь.
        /// Используется там, где зеркало повторяет структуру оригинала
        /// (манифест версий, client.jar, индекс ассетов).
        /// </summary>
        public static string BuildMojangMirrorUrl(string officialUrl)
        {
            return officialUrl
                .Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com")
                .Replace("https://launchermeta.mojang.com", "https://bmclapi2.bangbang93.com")
                .Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com")
                .Replace("https://launcher.mojang.com", "https://bmclapi2.bangbang93.com");
        }

        /// <summary>
        /// Читает предпочтение из настроек. Неизвестное значение трактуется как
        /// Auto, чтобы испорченный settings.json не отключил зеркала совсем.
        /// </summary>
        public static MirrorMode ParsePreference(string? value)
        {
            return (value ?? "").Trim().ToLowerInvariant() switch
            {
                "officialfirst" => MirrorMode.OfficialFirst,
                "official" => MirrorMode.OfficialFirst,
                "mirrorfirst" => MirrorMode.MirrorFirst,
                "mirror" => MirrorMode.MirrorFirst,
                "officialonly" => MirrorMode.OfficialOnly,
                "direct" => MirrorMode.OfficialOnly,
                _ => MirrorMode.Auto
            };
        }

        public static string PreferenceToString(MirrorMode preference)
        {
            return preference switch
            {
                MirrorMode.OfficialFirst => "OfficialFirst",
                MirrorMode.MirrorFirst => "MirrorFirst",
                MirrorMode.OfficialOnly => "OfficialOnly",
                _ => "Auto"
            };
        }
    }
}