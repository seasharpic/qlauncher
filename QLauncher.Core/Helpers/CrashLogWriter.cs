using System;
using System.IO;
using System.Text;

namespace MinecraftLauncher.Helpers
{
    /// <summary>
    /// Потокобезопасная запись в crash-лог.
    ///
    /// Раньше App.xaml.cs писал в launcher-crash.log через File.AppendAllText
    /// из обработчика диспетчера и из AppDomain.UnhandledException (то есть из
    /// произвольного потока пула). Две одновременные записи перемежались или
    /// роняли IOException, который тут же глушился пустым catch — в лог попадало
    /// меньше, чем должно.
    /// </summary>
    public static class CrashLogWriter
    {
        private static readonly object _gate = new object();

        private static string LogFilePath =>
            Path.Combine(LauncherPathHelper.GetDefaultDataDirectory(), "launcher-crash.log");

        public static void Write(string message)
        {
            Write("info", message, null);
        }

        public static void Write(string category, string message, Exception? exception)
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {category}: {message}";

            if (exception != null)
            {
                line += Environment.NewLine + exception;
            }

            line += Environment.NewLine + Environment.NewLine;

            lock (_gate)
            {
                try
                {
                    string? dir = Path.GetDirectoryName(LogFilePath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    File.AppendAllText(LogFilePath, line, Encoding.UTF8);
                }
                catch
                {
                    // Записать в лог не получилось — молча уходим: потеря записи
                    // о логе не должна приводить к новому исключению.
                }
            }
        }

        public static string LogFile => LogFilePath;
    }
}