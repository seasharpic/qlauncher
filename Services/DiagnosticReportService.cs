using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using MinecraftLauncher.Helpers;

namespace MinecraftLauncher.Services
{
    public interface IDiagnosticReportService
    {
        Task<string> GenerateReportZipAsync(string destinationFolder);
    }

    public class DiagnosticReportService : IDiagnosticReportService
    {
        public static DiagnosticReportService Instance { get; } = new DiagnosticReportService();

        public async Task<string> GenerateReportZipAsync(string destinationPath)
        {
            return await Task.Run(() =>
            {
                string zipPath;
                if (destinationPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    zipPath = destinationPath;
                    string? dir = Path.GetDirectoryName(zipPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                }
                else
                {
                    Directory.CreateDirectory(destinationPath);
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    zipPath = Path.Combine(destinationPath, $"qlauncher-report-{timestamp}.zip");
                }

                if (File.Exists(zipPath))
                {
                    File.Delete(zipPath);
                }

                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    // 1. Системная информация
                    var sysInfo = new StringBuilder();
                    sysInfo.AppendLine("=== QLauncher System & Environment Report ===");
                    sysInfo.AppendLine($"Timestamp: {DateTime.Now:O}");
                    sysInfo.AppendLine($"Launcher Version: {UpdateService.CurrentVersion}");
                    sysInfo.AppendLine($"Portable Mode: {LauncherPathHelper.IsPortableMode}");
                    sysInfo.AppendLine($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
                    sysInfo.AppendLine($"Framework: {RuntimeInformation.FrameworkDescription}");
                    sysInfo.AppendLine($"Process Architecture: {RuntimeInformation.ProcessArchitecture}");
                    sysInfo.AppendLine($"Data Directory: {LauncherPathHelper.GetDefaultDataDirectory()}");

                    var gpus = GpuService.Instance.GetAvailableGpus();
                    sysInfo.AppendLine("\n=== Detected Graphics Adapters ===");
                    foreach (var gpu in gpus)
                    {
                        sysInfo.AppendLine($"- {gpu.DisplayName} | Driver: {gpu.DriverVersion} | VRAM: {gpu.VramFormatted} | Discrete: {gpu.IsDiscrete}");
                    }

                    var settings = SettingsService.Instance.Settings;
                    sysInfo.AppendLine("\n=== Selected Settings Summary ===");
                    sysInfo.AppendLine($"Allocated RAM: {settings.RamMb} MB");
                    sysInfo.AppendLine($"Java Path: {(string.IsNullOrWhiteSpace(settings.JavaPath) ? "Auto/System" : settings.JavaPath)}");
                    sysInfo.AppendLine($"JVM Preset: {settings.JvmPreset}");
                    sysInfo.AppendLine($"Update Channel: {settings.UpdateChannel}");
                    sysInfo.AppendLine($"Update Mirror: {settings.UpdateMirror}");
                    sysInfo.AppendLine($"Modpacks Count: {settings.Modpacks.Count}");

                    var infoEntry = zip.CreateEntry("system_info.txt", CompressionLevel.Optimal);
                    using (var writer = new StreamWriter(infoEntry.Open(), Encoding.UTF8))
                    {
                        writer.Write(sysInfo.ToString());
                    }

                    // 2. Последние логи игры и лаунчера (с санитизацией токенов)
                    try
                    {
                        // Логи модпака лежат в instances/<pack>, а не в корне. Раньше читался
                        // неверный каталог, и для сборок отчёт оказывался пустым.
                        // Собираем логи из всех известных расположений.
                        var logsDirs = new List<string> { Path.Combine(settings.GamePath, "logs") };

                        foreach (var pack in settings.Modpacks)
                        {
                            if (!string.IsNullOrWhiteSpace(pack.FolderPath))
                            {
                                logsDirs.Add(Path.Combine(pack.FolderPath, "logs"));
                            }
                        }

                        foreach (string logsDir in logsDirs.Distinct())
                        {
                            if (!Directory.Exists(logsDir)) continue;

                            try
                            {
                                var logFiles = Directory.GetFiles(logsDir, "*.log")
                                    .OrderByDescending(File.GetLastWriteTime)
                                    .Take(3);

                                foreach (var logFile in logFiles)
                                {
                                    string content = File.ReadAllText(logFile);
                                    string sanitized = SanitizeLogContent(content);

                                    // Имя каталога-источника попадает в отчёт, иначе
                                    // логи из разных сборок перетирали бы друг друга.
                                    string sourceTag = SanitizeLogContent(Path.GetFileName(logsDir.TrimEnd(Path.DirectorySeparatorChar)));

                                    var logEntry = zip.CreateEntry(
                                        $"logs/{sourceTag}/{Path.GetFileName(logFile)}",
                                        CompressionLevel.Optimal);

                                    using var writer = new StreamWriter(logEntry.Open(), Encoding.UTF8);
                                    writer.Write(sanitized);
                                }
                            }
                            catch
                            {
                                // Ошибки чтения логов одного расположения не прерывают отчёт.
                            }
                        }
                    }
                    catch
                    {
                        // Ошибки чтения логов не прерывают создание репорта
                    }
                }

                return zipPath;
            });
        }

        private static string SanitizeLogContent(string log)
        {
            if (string.IsNullOrEmpty(log)) return "";
            // Вырезаем возможные токены сессии и приватные ключи
            string cleaned = log;

            // Порог {20,} был слишком строгим: короткий токен проходил насквозь,
            // при этом логи Minecraft токен в таком виде не пишут, так что regex
            // в основном вычищал безобидные UUID. Убираем оба класса отдельно.
            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                "(?i)(accessToken|access_token|auth_access_token|session|clientToken|refreshToken)\\s*[\"=:]\\s*[\"']?[^\"'\\s,}]+[\"']?",
                "$1=REDACTED");

            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                "(?i)(?<=[\"=:]\\s?)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}",
                "REDACTED-UUID");

            // Абсолютные пути выдают имя пользователя Windows — это ровно тот PII,
            // который пользователь публикует, когда просит помощь.
            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"[A-Za-z]:\\Users\\[^\\\s""]+",
                @"C:\Users\<user>");

            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned,
                @"/home/[^/\s""]+",
                "/home/<user>");

            return cleaned;
        }
    }
}
