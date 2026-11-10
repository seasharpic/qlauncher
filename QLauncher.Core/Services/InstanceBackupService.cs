using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MinecraftLauncher.Helpers;

namespace MinecraftLauncher.Services
{
    /// <summary>Плоская сводка для backup.json внутри архива.</summary>
    public sealed class ModsBackupManifest
    {
        [JsonPropertyName("pack")]
        public string PackName { get; set; } = "";
        [JsonPropertyName("game_version")]
        public string? GameVersion { get; set; }
        [JsonPropertyName("loader")]
        public string? Loader { get; set; }
        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        [JsonPropertyName("files")]
        public List<string> Files { get; set; } = new();
    }

    /// <summary>
    /// Бэкап модов экземпляра перед «опасными» операциями — прежде всего
    /// перед заменой jar'ов при массовом обновлении. Архив кладётся в
    /// <c>instance/backups</c>, старые архивы подчищаются (остаются
    /// <see cref="MaxBackups"/> последних). Замена файлов — обратимо.
    /// </summary>
    public static class InstanceBackupService
    {
        /// <summary>Сколько последних бэкапов модов держать на экземпляр.</summary>
        public const int MaxBackups = 5;

        public static string BackupsFolder(string instanceFolder) =>
            Path.Combine(instanceFolder ?? "", "backups");

        /// <summary>
        /// Создаёт zip с содержимым папки mods экземпляра и manifest.json.
        /// Возвращает путь архива или null, если модов нет — бэкапить нечего.
        /// </summary>
        public static async Task<string?> CreateModsBackupAsync(
            string instanceFolder, string packName, string? gameVersion, string? loader,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(instanceFolder) || !Directory.Exists(instanceFolder))
            {
                throw new DirectoryNotFoundException($"Instance folder not found: {instanceFolder}");
            }

            string modsDir = Path.Combine(instanceFolder, "mods");
            if (!Directory.Exists(modsDir))
            {
                return null;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(modsDir, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("InstanceBackup", "Failed to list mods folder", ex);
                return null;
            }

            if (files.Length == 0) return null;

            string backupsDir = BackupsFolder(instanceFolder);
            Directory.CreateDirectory(backupsDir);

            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string zipPath = Path.Combine(backupsDir, $"mods-{SanitizeName(packName)}-{stamp}.zip");

            try
            {
                var manifest = new ModsBackupManifest
                {
                    PackName = packName ?? "",
                    GameVersion = gameVersion,
                    Loader = loader
                };

                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    foreach (string file in files)
                    {
                        ct.ThrowIfCancellationRequested();
                        string entryName = Path.GetFileName(file);
                        zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                        manifest.Files.Add(entryName);
                    }

                    var infoEntry = zip.CreateEntry("backup.json", CompressionLevel.Optimal);
                    using (var writer = new StreamWriter(infoEntry.Open()))
                    {
                        writer.Write(JsonSerializer.Serialize(manifest,
                            new JsonSerializerOptions { WriteIndented = true }));
                    }
                }

                CleanupOldBackups(backupsDir);
                return zipPath;
            }
            catch (Exception ex)
            {
                // Битый неполный архив бесполезен и вводит в заблуждение — удаляем.
                try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
                CrashLogWriter.Write("InstanceBackup", $"Backup failed for '{packName}'", ex);
                throw;
            }
        }

        /// <summary>Удаляет старые архивы сверх лимита, самый свежий всегда остаётся.</summary>
        private static void CleanupOldBackups(string backupsDir)
        {
            try
            {
                var olds = Directory.GetFiles(backupsDir, "mods-*.zip")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .Skip(MaxBackups);

                foreach (var file in olds)
                {
                    try { file.Delete(); }
                    catch { }
                }
            }
            catch { }
        }

        private static string SanitizeName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var clean = new string((name ?? "pack").Where(ch => !invalid.Contains(ch)).ToArray()).Trim();
            return clean.Length == 0 ? "pack" : clean;
        }
    }
}
