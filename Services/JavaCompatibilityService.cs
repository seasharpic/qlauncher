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

        public int GetRequiredJavaVersion(string minecraftVersion)
        {
            if (string.IsNullOrWhiteSpace(minecraftVersion)) return 17;

            // Remove any modpack tags like "⭐ MyPack (Fabric)"
            string clean = minecraftVersion.Replace("⭐", "").Trim();
            if (clean.Contains(" ("))
            {
                clean = clean.Substring(0, clean.LastIndexOf(" (")).Trim();
            }

            var match = Regex.Match(clean, @"(\d+)\.(\d+)(?:\.(\d+))?");
            if (!match.Success) return 17;

            int major = int.Parse(match.Groups[1].Value);
            int minor = int.Parse(match.Groups[2].Value);
            int patch = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;

            if (major == 1)
            {
                if (minor <= 16) return 8;
                if (minor >= 17 && minor <= 20)
                {
                    if (minor == 20 && patch >= 5) return 21;
                    return 17;
                }
                if (minor >= 21) return 21;
            }
            else if (major >= 2)
            {
                return 21;
            }

            return 17;
        }

        public int DetectInstalledJavaVersion(string? customJavaPath = null)
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

                string output = proc.StandardError.ReadToEnd() + proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(3000);

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

        public (bool IsCompatible, int RequiredVersion, int InstalledVersion, string Message) CheckCompatibility(string minecraftVersion, string? customJavaPath = null)
        {
            int required = GetRequiredJavaVersion(minecraftVersion);
            int installed = DetectInstalledJavaVersion(customJavaPath);

            if (installed == 0)
            {
                return (false, required, 0, $"Java не обнаружена в системе или указан неверный путь. Для Minecraft {minecraftVersion} требуется Java {required}.");
            }

            // Java 8 is strict for older versions
            if (required == 8 && installed > 8)
            {
                return (false, required, installed, $"Для Minecraft {minecraftVersion} требуется Java 8, однако обнаружена Java {installed}. Старые версии игры могут аварийно завершаться на Java {installed}.");
            }

            // Java 17 or 21 required, but installed is older
            if (installed < required)
            {
                return (false, required, installed, $"Для запуска Minecraft {minecraftVersion} требуется Java {required} или новее. В системе обнаружена устаревшая Java {installed}.");
            }

            return (true, required, installed, $"Совместимость подтверждена (требуется: Java {required}, установлена: Java {installed}).");
        }
    }
}
