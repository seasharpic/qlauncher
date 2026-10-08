using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MinecraftLauncher.Models;
using MinecraftLauncher.Services;

namespace MinecraftLauncher.Helpers
{
    /// <summary>
    /// Анализ crash-репортов: текст разбора показывается пользователю в тосте.
    /// Раньше он был зашит по-русски прямо здесь, из-за чего в английском
    /// интерфейсе подсказка после падения игры оставалась русской.
    /// </summary>
    public static class CrashAnalyzer
    {
        private static readonly ILocalizationService L = LocalizationService.Instance;
        public static CrashAnalysisResult AnalyzeLatestCrash(string gamePath)
        {
            try
            {
                string crashReportsDir = Path.Combine(gamePath, "crash-reports");
                string latestLogPath = Path.Combine(gamePath, "logs", "latest.log");
                string targetFile = "";

                if (Directory.Exists(crashReportsDir))
                {
                    var latestReport = new DirectoryInfo(crashReportsDir)
                        .GetFiles("crash-*.txt")
                        .OrderByDescending(f => f.LastWriteTime)
                        .FirstOrDefault();

                    if (latestReport != null && (DateTime.Now - latestReport.LastWriteTime).TotalMinutes < 5)
                    {
                        targetFile = latestReport.FullName;
                    }
                }

                if (string.IsNullOrEmpty(targetFile) && File.Exists(latestLogPath))
                {
                    targetFile = latestLogPath;
                }

                if (string.IsNullOrEmpty(targetFile) || !File.Exists(targetFile))
                {
                    return new CrashAnalysisResult
                    {
                        HasCrash = true,
                        Title = L.GetString("Str_Crash_Title_Generic"),
                        Summary = L.GetString("Str_Crash_Summary_Generic"),
                        Recommendation = L.GetString("Str_Crash_Fix_Generic")
                    };
                }

                string content = File.ReadAllText(targetFile);

                if (content.Contains("java.lang.OutOfMemoryError") ||
                    content.Contains("There is insufficient memory for the Java Runtime Environment"))
                {
                    return new CrashAnalysisResult
                    {
                        HasCrash = true,
                        Title = L.GetString("Str_Crash_Title_Oom"),
                        Summary = L.GetString("Str_Crash_Summary_Oom"),
                        Recommendation = L.GetString("Str_Crash_Fix_Oom")
                    };
                }

                if ((content.Contains("optifine", StringComparison.OrdinalIgnoreCase)) &&
                    (content.Contains("sodium") || content.Contains("iris") || content.Contains("rubidium")))
                {
                    return new CrashAnalysisResult
                    {
                        HasCrash = true,
                        Title = L.GetString("Str_Crash_Title_ModConflict"),
                        Summary = L.GetString("Str_Crash_Summary_ModConflict"),
                        Recommendation = L.GetString("Str_Crash_Fix_ModConflict")
                    };
                }

                if (content.Contains("Fabric error") ||
                    content.Contains("Requires:") ||
                    content.Contains("net.fabricmc.loader.impl.formatted.FormattedException"))
                {
                    var match = Regex.Match(content, @"Requires:\s*(.+)");
                    string req = match.Success ? match.Groups[1].Value.Trim() : L.GetString("Str_Crash_MissingLib_Fabric");

                    return new CrashAnalysisResult
                    {
                        HasCrash = true,
                        Title = L.GetString("Str_Crash_Title_MissingDep"),
                        Summary = L.Format("Str_Crash_Summary_MissingDep", req),
                        Recommendation = L.GetString("Str_Crash_Fix_MissingDep")
                    };
                }

                if (content.Contains("UnsupportedClassVersionError") ||
                    content.Contains("has been compiled by a more recent version of the Java Runtime"))
                {
                    return new CrashAnalysisResult
                    {
                        HasCrash = true,
                        Title = L.GetString("Str_Crash_Title_Java"),
                        Summary = L.GetString("Str_Crash_Summary_Java"),
                        Recommendation = L.GetString("Str_Crash_Fix_Java")
                    };
                }

                if (content.Contains("org.lwjgl.LWJGLException: Pixel format not accelerated") ||
                    content.Contains("GLFW error 65542: WGL: The driver does not appear to support OpenGL") ||
                    content.Contains("ig4icd64.dll") || content.Contains("nvoglv64.dll") || content.Contains("atio6axx.dll"))
                {
                    return new CrashAnalysisResult
                    {
                        HasCrash = true,
                        Title = L.GetString("Str_Crash_Title_OpenGl"),
                        Summary = L.GetString("Str_Crash_Summary_OpenGl"),
                        Recommendation = L.GetString("Str_Crash_Fix_OpenGl")
                    };
                }

                return new CrashAnalysisResult
                {
                    HasCrash = true,
                    Title = L.GetString("Str_Crash_Title_Runtime"),
                    Summary = L.GetString("Str_Crash_Summary_Runtime"),
                    Recommendation = L.GetString("Str_Crash_Fix_Runtime")
                };
            }
            catch
            {
                return new CrashAnalysisResult
                {
                    HasCrash = true,
                    Title = L.GetString("Str_Crash_Title_Generic"),
                    Summary = L.GetString("Str_Crash_Summary_Unknown"),
                    Recommendation = L.GetString("Str_Crash_Fix_Unknown")
                };
            }
        }
    }
}
