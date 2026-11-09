using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace MinecraftLauncher.Services
{
    public interface IJavaCompatibilityService
    {
        int GetRequiredJavaVersion(string minecraftVersion);
        int DetectInstalledJavaVersion(string? customJavaPath = null);
        (bool IsCompatible, int RequiredVersion, int InstalledVersion, string Message) CheckCompatibility(string minecraftVersion, string? customJavaPath = null);
    }

    public class JavaCompatibilityService : IJavaCompatibilityService
    {
        public static JavaCompatibilityService Instance { get; } = new JavaCompatibilityService();

        /// <summary>
        /// Единственный источник правды о требуемой версии Java.
        ///
        /// Раньше правило было продублировано в JavaCompatibilityService и JavaService
        /// с разными ответами по умолчанию (17 против 21) и разной обработкой тегов
        /// сборок, поэтому один и тот же вопрос получал два разных ответа.
        /// Теперь оба сервиса используют это правило.
        /// </summary>
        public static int GetRequiredJavaVersionCore(string minecraftVersion)
        {
            if (string.IsNullOrWhiteSpace(minecraftVersion)) return DefaultJavaVersion;

            // Убираем теги вида "⭐ MyPack (Fabric)" и префиксы вроде "1.20.1-fabric".
            string clean = minecraftVersion.Replace("⭐", "").Trim();
            if (clean.Contains(" ("))
            {
                clean = clean.Substring(0, clean.LastIndexOf(" (")).Trim();
            }

            if (clean.Contains('-'))
            {
                clean = clean[..clean.IndexOf('-')].Trim();
            }

            if (string.IsNullOrEmpty(clean)) return DefaultJavaVersion;

            // Снапшоты вида 25w31a и релизы вида 1.21.4
            var snapshotMatch = Regex.Match(clean, @"^(\d{2})w\d+");
            if (snapshotMatch.Success && int.TryParse(snapshotMatch.Groups[1].Value, out int snapshotYear))
            {
                // Годовой нумерации Minecraft: 22w -> Java 17, 24w+ -> Java 21,
                // 25w+ -> Java 25.
                if (snapshotYear >= 25) return 25;
                if (snapshotYear >= 24) return 21;
                if (snapshotYear >= 22) return 17;
                if (snapshotYear >= 20) return 8;
                return 17;
            }

            var match = Regex.Match(clean, @"^(\d+)\.(\d+)(?:\.(\d+))?");
            if (!match.Success) return DefaultJavaVersion;

            if (!int.TryParse(match.Groups[1].Value, out int major)) return DefaultJavaVersion;
            if (!int.TryParse(match.Groups[2].Value, out int minor)) return DefaultJavaVersion;

            int patch = 0;
            if (match.Groups[3].Success)
            {
                int.TryParse(match.Groups[3].Value, out patch);
            }

            if (major == 1)
            {
                if (minor <= 16) return 8;
                if (minor == 17 || minor == 18 || minor == 19) return 17;
                if (minor == 20) return patch >= 5 ? 21 : 17;
                if (minor >= 21) return 21;
            }
            else if (major >= 2)
            {
                return 21;
            }

            return DefaultJavaVersion;
        }

        /// <summary>Версия Java по умолчанию, когда версию определить не удалось.</summary>
        public const int DefaultJavaVersion = 21;

        public int GetRequiredJavaVersion(string minecraftVersion)
        {
            return GetRequiredJavaVersionCore(minecraftVersion);
        }

        public int DetectInstalledJavaVersion(string? customJavaPath = null)
        {
            // Обёртка поверх асинхронного варианта: синхронный вызов остаётся
            // для мест, где await недоступен, но сам он больше не вызывает
            // Process.Start и WaitForExit на текущем потоке.
            return DetectInstalledJavaVersionAsync(customJavaPath).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Определяет мажорную версию Java по выводу "java -version".
        ///
        /// Асинхронный вариант читает stdout и stderr одновременно: прежний код
        /// делал ReadToEnd() для stderr раньше stdout, и если процесс заполнял
        /// буфер stdout, чтение stderr никогда не завершалось — дедлок двух пайпов.
        /// </summary>
        public async Task<int> DetectInstalledJavaVersionAsync(string? customJavaPath = null)
        {
            string javaExe = !string.IsNullOrWhiteSpace(customJavaPath) && File.Exists(customJavaPath)
                ? customJavaPath
                : "java";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = javaExe,
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return 0;

                // Читаем оба потока параллельно, иначе возможен взаимный дедлок
                // на заполнении буферов пайпов. WaitAsync с таймаутом не блокирует поток.
                Task<string> stdoutTask = proc.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = proc.StandardError.ReadToEndAsync();
                Task allReads = Task.WhenAll(stdoutTask, stderrTask);

                if (await Task.WhenAny(allReads, Task.Delay(5000)) != allReads)
                {
                    return 0;
                }

                string output = await stdoutTask + await stderrTask;

                var match = Regex.Match(output, @"version\s+""([^""]+)""");
                if (match.Success)
                {
                    string verStr = match.Groups[1].Value;
                    if (verStr.StartsWith("1."))
                    {
                        var sub = Regex.Match(verStr, @"1\.(\d+)");
                        if (sub.Success && int.TryParse(sub.Groups[1].Value, out int v)) return v;
                    }
                    else
                    {
                        var sub = Regex.Match(verStr, @"^(\d+)");
                        if (sub.Success && int.TryParse(sub.Groups[1].Value, out int v)) return v;
                    }
                }
            }
            catch { }

            return 0;
        }

        /// <summary>
        /// Асинхронная проверка совместимости: не блокирует UI-поток.
        /// Основная точка входа из UI.
        /// </summary>
        public async Task<(bool IsCompatible, int RequiredVersion, int InstalledVersion, string Message)> CheckCompatibilityAsync(string minecraftVersion, string? customJavaPath = null)
        {
            int required = GetRequiredJavaVersion(minecraftVersion);
            int installed = await DetectInstalledJavaVersionAsync(customJavaPath);
            return EvaluateCompatibility(minecraftVersion, required, installed);
        }

        /// <summary>
        /// Синхронная обёртка. В UI-коде не используется: вызов блокирует поток
        /// до завершения процесса Java. Оставлена для консольных и тестовых
        /// сценариев.
        /// </summary>
        [Obsolete("Используйте CheckCompatibilityAsync: синхронный вариант блокирует поток.")]
        public (bool IsCompatible, int RequiredVersion, int InstalledVersion, string Message) CheckCompatibility(string minecraftVersion, string? customJavaPath = null)
        {
            int required = GetRequiredJavaVersion(minecraftVersion);
            int installed = DetectInstalledJavaVersion(customJavaPath);
            return EvaluateCompatibility(minecraftVersion, required, installed);
        }

        private static (bool IsCompatible, int RequiredVersion, int InstalledVersion, string Message) EvaluateCompatibility(string minecraftVersion, int required, int installed)
        {

            if (installed == 0)
            {
                return (false, required, 0, LocalizationService.Instance.Format("Str_JavaCompat_NotFound", minecraftVersion, required));
            }

            // Java 8 is strict for older versions
            if (required == 8 && installed > 8)
            {
                return (false, required, installed, LocalizationService.Instance.Format("Str_JavaCompat_NeedsJava8", minecraftVersion, installed));
            }

            // Java 17 or 21 required, but installed is older
            if (installed < required)
            {
                return (false, required, installed, LocalizationService.Instance.Format("Str_JavaCompat_TooOld", minecraftVersion, required, installed));
            }

            return (true, required, installed, LocalizationService.Instance.Format("Str_JavaCompat_Ok", required, installed));
        }
    }
}
