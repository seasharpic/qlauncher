using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MinecraftLauncher.Models;

namespace MinecraftLauncher.Helpers
{
    public static class CrashAnalyzer
    {
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
                        Title = "Сбой игры",
                        Summary = "Minecraft завершился с ненулевым кодом выхода.",
                        Recommendation = "Откройте консоль в лаунчере для просмотра журнала работы."
                    };
                }

                string content = File.ReadAllText(targetFile);

                if (content.Contains("java.lang.OutOfMemoryError") ||
                    content.Contains("There is insufficient memory for the Java Runtime Environment"))
                {
                    return new CrashAnalysisResult
                    {
                        HasCrash = true,
                        Title = "Нехватка оперативной памяти",
                        Summary = "Игре недостаточно выделенной оперативной памяти (RAM).",
                        Recommendation = "Увеличьте объем выделяемой памяти в настройках лаунчера."
                    };
                }

                if ((content.Contains("optifine", StringComparison.OrdinalIgnoreCase)) &&
                    (content.Contains("sodium") || content.Contains("iris") || content.Contains("rubidium")))
                {
                    return new CrashAnalysisResult
                    {
                        HasCrash = true,
                        Title = "Конфликт модов оптимизации",
                        Summary = "Обнаружен совместный запуск несовместимых графических модификаций.",
                        Recommendation = "Удалите OptiFine из папки mods при использовании Sodium и Iris."
                    };
                }

                if (content.Contains("Fabric error") ||
                    content.Contains("Requires:") ||
                    content.Contains("net.fabricmc.loader.impl.formatted.FormattedException"))
                {
                    var match = Regex.Match(content, @"Requires:\s*(.+)");
                    string req = match.Success ? match.Groups[1].Value.Trim() : "библиотеки Fabric API";

                    return new CrashAnalysisResult
                    {
                        HasCrash = true,
                        Title = "Отсутствует зависимость",
                        Summary = $"Для работы установленных модов требуется: {req}",
                        Recommendation = "Установите необходимые моды через раздел 'Моды' в лаунчере."
                    };
                }

                if (content.Contains("UnsupportedClassVersionError") ||
                    content.Contains("has been compiled by a more recent version of the Java Runtime"))
                {
                    return new CrashAnalysisResult
                    {
                        HasCrash = true,
                        Title = "Несовместимая версия Java",
                        Summary = "Версия игры или один из модов скомпилирован под более новую версию Java.",
                        Recommendation = "Проверьте выбор Java в настройках (рекомендуется автоматический выбор версий 17 или 21)."
                    };
                }

                if (content.Contains("org.lwjgl.LWJGLException: Pixel format not accelerated") ||
                    content.Contains("GLFW error 65542: WGL: The driver does not appear to support OpenGL") ||
                    content.Contains("ig4icd64.dll") || content.Contains("nvoglv64.dll") || content.Contains("atio6axx.dll"))
                {
                    return new CrashAnalysisResult
                    {
                        HasCrash = true,
                        Title = "Сбой видеодрайвера (OpenGL)",
                        Summary = "Видеокарта или драйвер не поддерживают требуемую версию OpenGL.",
                        Recommendation = "Обновите драйверы видеокарты или переключите используемый GPU в Настройках лаунчера."
                    };
                }

                return new CrashAnalysisResult
                {
                    HasCrash = true,
                    Title = "Ошибка выполнения игры",
                    Summary = "Процесс завершился аварийно.",
                    Recommendation = "Перейдите во вкладку 'Консоль' для просмотра полного журнала событий."
                };
            }
            catch
            {
                return new CrashAnalysisResult
                {
                    HasCrash = true,
                    Title = "Сбой игры",
                    Summary = "Игра завершилась с ошибкой.",
                    Recommendation = "Откройте консоль лаунчера для анализа логов."
                };
            }
        }
    }
}
