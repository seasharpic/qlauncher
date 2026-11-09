using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MinecraftLauncher.Helpers;

namespace MinecraftLauncher.Services
{
    /// <summary>
    /// Публикация логов на mclo.gs: короткая ссылка вместо 10 000 строк
    /// в чат. Используется консолью игры, когда юзеру нужно показать
    /// лог в Discord или на форуме.
    /// </summary>
    public static class LogShareService
    {
        private static readonly HttpClient _client = new();

        static LogShareService()
        {
            _client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent", "QLauncher_LogShare/1.0 (+https://github.com)");
        }

        /// <summary>
        /// Загружает текст лога и возвращает короткий URL вида https://mclo.gs/xxxxx.
        /// mclo.gs принимает form-urlencoded content; иначе бросает исключение.
        /// </summary>
        public static async Task<string> UploadLogAsync(string content, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException("Лог пустой — нечего публиковать.");
            }

            using var form = new FormUrlEncodedContent(
                new Dictionary<string, string?> { ["content"] = content });

            using var response = await SecureHttp.Shared
                .PostAsync("https://api.mclo.gs/1/log", form, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("success", out var success) &&
                success.ValueKind == JsonValueKind.True &&
                doc.RootElement.TryGetProperty("url", out var url) &&
                url.GetString() is { Length: > 0 } link)
            {
                return link;
            }

            throw new HttpRequestException("mclo.gs не принял лог (success=false).");
        }
    }
}
