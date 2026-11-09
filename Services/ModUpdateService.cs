using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Models;

namespace MinecraftLauncher.Services
{
    /// <summary>
    /// Массовое обновление модов в сборке: для запакованных jar'ов находим
    /// проект в каталоге (Modrinth по SHA-1, CurseForge по murmur2-отпечатку),
    /// подбираем последнюю совместимую версию и при отличии предлагаем замену.
    /// </summary>
    public sealed class ModUpdateCandidate
    {
        public string OldPath { get; init; } = "";
        public string OldFileName => Path.GetFileName(OldPath);
        public string OldSha1 { get; init; } = "";
        public ModSource Source { get; init; }
        public OnlineModFile FreshFile { get; init; } = new();

        /// <summary>Версия для показа в строке отчёта.</summary>
        public string FreshName =>
            FreshFile.DisplayName.Length > 0 ? FreshFile.DisplayName : FreshFile.FileName;
    }

    /// <summary>Результат сканирования папки модов.</summary>
    public sealed class ModUpdateScanResult
    {
        public List<ModUpdateCandidate> Updates { get; } = new();
        /// <summary>Jar'ы, не опознанные ни в одном каталоге.</summary>
        public List<string> UnknownPaths { get; } = new();
        public int ScannedCount { get; set; }
    }

    public static class ModUpdateService
    {
        // Modrinth: пакетное опознание по sha1 — один запрос на всю папку.
        private const string CheckFileUrl = "https://api.modrinth.com/v2/check_file?algorithm=sha1";
        // CurseForge: пакетный murmur2-отпечаток — тоже один запрос на папку.
        private const string FingerprintsUrl = "https://api.curseforge.com/v1/fingerprints";

        /// <summary>
        /// Сканирует папку модов и возвращает список обновлений.
        /// Проект ищется в обоих каталогах: мод может быть выложен где угодно.
        /// </summary>
        public static async Task<ModUpdateScanResult> ScanAsync(
            string modsFolder, string? gameVersion, string? loader,
            Action<string>? progress = null, CancellationToken ct = default)
        {
            var result = new ModUpdateScanResult();

            if (string.IsNullOrWhiteSpace(modsFolder) || !Directory.Exists(modsFolder))
            {
                return result;
            }

            string[] jars;
            try
            {
                // "*.jar" не подхватывает .disabled — обновляем только включённые.
                jars = Directory.GetFiles(modsFolder, "*.jar", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("ModUpdateService", "Failed to list mods folder", ex);
                return result;
            }

            result.ScannedCount = jars.Length;
            if (jars.Length == 0) return result;

            progress?.Invoke("");

            // --- Хеши: sha1 всем, murmur2 по факту наличия ключа CurseForge ---
            var hashes = new Dictionary<string, uint>(jars.Length, StringComparer.OrdinalIgnoreCase);
            bool useCurseForge = ModBrowserService.Instance.HasCurseForgeKey();

            foreach (string jar in jars)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    byte[] bytes = await File.ReadAllBytesAsync(jar, ct).ConfigureAwait(false);
                    hashes[jar] = ComputeCurseForgeFingerprint(bytes);
                }
                catch (Exception ex)
                {
                    CrashLogWriter.Write("ModUpdateService", $"Hash failed for '{Path.GetFileName(jar)}'", ex);
                }
            }

            // sha1 нужны в любом случае — они и для опознания Modrinth, и для сравнения версий.
            var sha1ByPath = new Dictionary<string, string>(jars.Length, StringComparer.OrdinalIgnoreCase);
            foreach (string jar in jars)
            {
                try
                {
                    using var stream = File.OpenRead(jar);
                    using var sha1 = SHA1.Create();
                    byte[] hashBytes = await sha1.ComputeHashAsync(stream, ct).ConfigureAwait(false);
                    sha1ByPath[jar] = Convert.ToHexString(hashBytes).ToLowerInvariant();
                }
                catch (Exception ex)
                {
                    CrashLogWriter.Write("ModUpdateService", $"sha1 failed for '{Path.GetFileName(jar)}'", ex);
                }
            }

            // --- Modrinth: пакетный check_file ---
            var mrProject = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // path → project_id
            try
            {
                var requestPaths = sha1ByPath.Where(kvp => kvp.Key != null).Select(kvp => kvp.Key).ToList();
                var hashesPayload = requestPaths.Select(p => sha1ByPath[p]).ToList();

                using var content = new System.Net.Http.StringContent(
                    JsonSerializer.Serialize(new { hashes = hashesPayload }),
                    System.Text.Encoding.UTF8, "application/json");
                using var response = await SecureHttp.Shared.PostAsync(CheckFileUrl, content, ct).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        var arr = doc.RootElement.EnumerateArray().ToList();
                        for (int i = 0; i < arr.Count && i < requestPaths.Count; i++)
                        {
                            if (arr[i].ValueKind != JsonValueKind.Array || arr[i].GetArrayLength() < 1) continue;
                            var first = arr[i][0];
                            if (first.ValueKind == JsonValueKind.String && first.GetString() is { Length: > 0 } projectId)
                            {
                                mrProject[requestPaths[i]] = projectId;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                CrashLogWriter.Write("ModUpdateService", "Modrinth check_file failed", ex);
            }

            // --- CurseForge: пакетный murmur2-отпечаток ---
            var cfMod = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // path → modId
            if (useCurseForge && hashes.Count > 0)
            {
                try
                {
                    var paths = hashes.Keys.ToList();
                    var body = JsonSerializer.Serialize(new
                    {
                        fileFingerprints = paths.Select(p => hashes[p]).ToArray()
                    });
                    using var content = new System.Net.Http.StringContent(
                        body, System.Text.Encoding.UTF8, "application/json");
                    using var request = new System.Net.Http.HttpRequestMessage(
                        System.Net.Http.HttpMethod.Post, FingerprintsUrl) { Content = content };
                    request.Headers.Add("x-api-key", SettingsService.Instance.Settings.CurseForgeApiKey ?? "");
                    request.Headers.Accept.ParseAdd("application/json");

                    using var response = await SecureHttp.Shared.SendAsync(request, ct).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

                        if (doc.RootElement.TryGetProperty("data", out var data) &&
                            data.TryGetProperty("exactMatches", out var matches) &&
                            matches.ValueKind == JsonValueKind.Array)
                        {
                            // В ответе совпадений лежит сам файл с murmur2 и modId:
                            // сопоставляем отпечаток нашего jar с отпечатком файла.
                            foreach (var match in matches.EnumerateArray())
                            {
                                if (!match.TryGetProperty("file", out var file) ||
                                    !file.TryGetProperty("modId", out var modId) ||
                                    modId.ValueKind != JsonValueKind.Number ||
                                    !file.TryGetProperty("murmur2", out var murmur) ||
                                    murmur.ValueKind != JsonValueKind.Number)
                                {
                                    continue;
                                }

                                uint fingerprint = murmur.GetUInt32();
                                foreach (var path in hashes.Keys)
                                {
                                    if (hashes[path] == fingerprint)
                                    {
                                        cfMod[path] = modId.GetInt32().ToString();
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
                {
                    CrashLogWriter.Write("ModUpdateService", "CurseForge fingerprints failed", ex);
                }
            }

            // --- Последняя совместимая версия по каждому опознанному пути ---
            var pathsWithProject = mrProject.Keys.Union(cfMod.Keys)
                .Where(p => sha1ByPath.ContainsKey(p))
                .ToList();

            using var semaphore = new SemaphoreSlim(4);

            await Task.WhenAll(pathsWithProject.Select(async path =>
            {
                if (ct.IsCancellationRequested) return;
                await semaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    bool isCf = cfMod.TryGetValue(path, out var cfModId) && !mrProject.ContainsKey(path);
                    var source = isCf ? ModSource.CurseForge : ModSource.Modrinth;
                    var projectId = isCf ? cfModId! : mrProject[path];

                    var fresh = await ModBrowserService.Instance.ResolveFileAsync(
                        source, new OnlineModEntry { Source = source, Id = projectId },
                        gameVersion, loader, ct).ConfigureAwait(false);

                    if (fresh == null) return;

                    // Свежий файл тот же, что стоит: обновления нет.
                    string freshSha = fresh.Sha1;
                    bool same = !string.IsNullOrEmpty(freshSha)
                        ? string.Equals(freshSha, sha1ByPath[path], StringComparison.OrdinalIgnoreCase)
                        : string.Equals(fresh.FileName, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase);
                    if (same) return;

                    lock (result.Updates)
                    {
                        result.Updates.Add(new ModUpdateCandidate
                        {
                            OldPath = path,
                            OldSha1 = sha1ByPath[path],
                            Source = source,
                            FreshFile = fresh
                        });
                    }
                }
                catch (Exception ex)
                {
                    CrashLogWriter.Write("ModUpdateService", $"Scan failed for '{Path.GetFileName(path)}'", ex);
                }
                finally
                {
                    semaphore.Release();
                }
            })).ConfigureAwait(false);

            // Неопознанные: ни Modrinth, ни CurseForge не знают файл.
            foreach (string path in jars)
            {
                if (!mrProject.ContainsKey(path) && !cfMod.ContainsKey(path))
                {
                    result.UnknownPaths.Add(path);
                }
            }

            return result;
        }

        /// <summary>
        /// Скачивает свежий файл, проверяет SHA-1 и заменяет jar:
        /// новый пишем, старый удаляем. Возвращает путь нового файла.
        /// </summary>
        public static async Task<string?> ApplyAsync(ModUpdateCandidate candidate, CancellationToken ct = default)
        {
            try
            {
                byte[]? bytes = await SecureHttp.TryDownloadBytesAsync(
                    new[] { candidate.FreshFile.Url, candidate.FreshFile.FallbackUrl },
                    TimeSpan.FromMinutes(5), ct).ConfigureAwait(false);

                if (bytes == null || bytes.Length == 0) return null;

                if (!string.IsNullOrEmpty(candidate.FreshFile.Sha1) &&
                    !HashHelper.VerifySha1(bytes, candidate.FreshFile.Sha1))
                {
                    CrashLogWriter.Write("ModUpdateService", $"sha1 mismatch for '{candidate.FreshName}'", null);
                    return null;
                }

                string dir = Path.GetDirectoryName(candidate.OldPath) ?? ".";
                string newPath = Path.Combine(dir, candidate.FreshFile.FileName);

                // Если цель совпала со старым именем — просто перезапись.
                await File.WriteAllBytesAsync(newPath, bytes, ct).ConfigureAwait(false);

                if (!string.Equals(
                        Path.GetFullPath(candidate.OldPath),
                        Path.GetFullPath(newPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(candidate.OldPath);
                }

                return newPath;
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("ModUpdateService", $"Apply failed for '{candidate.OldFileName}'", ex);
                return null;
            }
        }

        /// <summary>
        /// murmur2-отпечаток для CurseForge: пробельные байты (TAB, LF, CR,
        /// пробел) выбрасываются, остальные раскладываются парами [байт, 0],
        /// затем обычный murmur2-32 с seed = 1. Формат
        /// воспроизводит официальное описание fileFingerprint.
        /// </summary>
        private static uint ComputeCurseForgeFingerprint(byte[] data)
        {
            unchecked
            {
                // Таблица пробелов: TAB=9, LF=10, CR=13, SPACE=32.
                var filtered = new List<byte>(data.Length);
                foreach (byte b in data)
                {
                    if (b == 9 || b == 10 || b == 13 || b == 32) continue;
                    filtered.Add(b);
                }

                byte[] mixed = new byte[filtered.Count * 2];
                for (int i = 0; i < filtered.Count; i++)
                {
                    mixed[i * 2] = filtered[i];
                }

                return Murmur2(mixed, 1u);
            }
        }

        /// <summary>Классический murmur2-32 (не x64) — он используется CurseForge.</summary>
        private static uint Murmur2(byte[] data, uint seed)
        {
            unchecked
            {
                const uint c1 = 0xCC9E2D51u, c2 = 0x1B873593u;
                int length = data.Length;
                uint h = seed;
                int roundedEnd = length & ~3;

                for (int i = 0; i < roundedEnd; i += 4)
                {
                    uint k = (uint)(data[i]
                        | (data[i + 1] << 8)
                        | (data[i + 2] << 16)
                        | (data[i + 3] << 24));

                    k *= c1;
                    k = (k << 15) | (k >> 17);
                    k *= c2;

                    h ^= k;
                    h = (h << 13) | (h >> 19);
                    h = h * 5u + 0xE6546B64u;
                }

                uint tail = 0;
                switch (length & 3)
                {
                    case 3: tail ^= (uint)(data[roundedEnd + 2] << 16); goto case 2;
                    case 2: tail ^= (uint)(data[roundedEnd + 1] << 8); goto case 1;
                    case 1:
                        tail ^= data[roundedEnd];
                        tail *= c1;
                        tail = (tail << 15) | (tail >> 17);
                        tail *= c2;
                        h ^= tail;
                        break;
                }

                h ^= (uint)length;
                h ^= h >> 16;
                h *= 0x85EBCA6Bu;
                h ^= h >> 13;
                h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return h;
            }
        }
    }
}
