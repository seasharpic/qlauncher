using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace MinecraftLauncher.Helpers
{
    public static class LauncherPathHelper
    {
        public static string AppBaseDir => AppDomain.CurrentDomain.BaseDirectory;

        public static bool IsPortableMode =>
            File.Exists(Path.Combine(AppBaseDir, "portable")) ||
            File.Exists(Path.Combine(AppBaseDir, "portable.txt")) ||
            Directory.Exists(Path.Combine(AppBaseDir, "data"));

        public static string GetDefaultDataDirectory()
        {
            if (IsPortableMode)
            {
                string localData = Path.Combine(AppBaseDir, "data");
                Directory.CreateDirectory(localData);
                return localData;
            }

            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".qlauncher");
            Directory.CreateDirectory(appData);
            return appData;
        }

        public static string GetSettingsFilePath()
        {
            return Path.Combine(GetDefaultDataDirectory(), "settings.json");
        }

        /// <summary>
        /// Включает портативный режим: создаёт маркер и переносит существующие
        /// данные в ./data.
        ///
        /// Раньше метод просто создавал пустую папку data рядом с exe. Из-за этого
        /// включить режим было нечем: кнопки в UI не было, а если marker создать
        /// вручную, то настройки, аккаунты и сборки оставались в %APPDATA%\.qlauncher
        /// и просто терялись из виду — лаунчер выглядел пустым. Теперь данные
        /// переезжают вместе с режимом.
        ///
        /// Возвращает false, если перенос не удался: тогда маркер не создаётся, иначе
        /// пользователь потерял бы доступ к своим настройкам.
        /// </summary>
        public static bool EnablePortableMode()
        {
            try
            {
                string dataDir = Path.Combine(AppBaseDir, "data");
                Directory.CreateDirectory(dataDir);

                // Переносим данные ДО создания маркера. Пока маркера нет,
                // GetDefaultDataDirectory() указывает на %APPDATA%\.qlauncher,
                // поэтому исходный путь вычисляется напрямую.
                string previousDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".qlauncher");

                if (!PathsEqual(previousDataDir, dataDir) && Directory.Exists(previousDataDir))
                {
                    MigrateDirectory(previousDataDir, dataDir);
                }

                string marker = Path.Combine(AppBaseDir, "portable");
                if (!File.Exists(marker))
                {
                    File.WriteAllBytes(marker, Array.Empty<byte>());
                }

                return true;
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("PortableMode", "Failed to enable portable mode", ex);
                return false;
            }
        }

        /// <summary>
        /// Выключает портативный режим: удаляет маркер и возвращает данные в
        /// %APPDATA%\.qlauncher. Требует перезапуска — пути вычисляются при старте.
        /// </summary>
        public static bool DisablePortableMode()
        {
            try
            {
                string dataDir = Path.Combine(AppBaseDir, "data");

                foreach (string name in new[] { "portable", "portable.txt" })
                {
                    string marker = Path.Combine(AppBaseDir, name);
                    if (File.Exists(marker))
                    {
                        File.Delete(marker);
                    }
                }

                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".qlauncher");

                if (Directory.Exists(dataDir))
                {
                    Directory.CreateDirectory(appDataDir);
                    MigrateDirectory(dataDir, appDataDir);
                }

                return true;
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("PortableMode", "Failed to disable portable mode", ex);
                return false;
            }
        }

        /// <summary>
        /// Переносит содержимое каталога, не затирая уже существующие файлы в
        /// приёмнике. Файлы копируются, а не перемещаются: оригинал остаётся на
        /// месте, чтобы сбой посреди переноса не приводил к потере данных.
        /// </summary>
        private static void MigrateDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string targetFile = Path.Combine(targetDir, Path.GetFileName(file));

                if (File.Exists(targetFile))
                {
                    // Настройки в приёмнике уже есть — оставляем их, но отступ
                    // о конфликте пишем: иначе перенос молча выберет одну из версий.
                    CrashLogWriter.Write(
                        "PortableMode",
                        $"Kept existing '{targetFile}' while migrating '{file}'",
                        null);
                    continue;
                }

                File.Copy(file, targetFile);
            }

            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                MigrateDirectory(dir, Path.Combine(targetDir, Path.GetFileName(dir)));
            }
        }

        private static bool PathsEqual(string a, string b)
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        public static async Task<string> ExportPortablePackageAsync(string destinationZipPath, bool includeGameData = false)
        {
            return await Task.Run(() =>
            {
                string tempDir = Path.Combine(Path.GetTempPath(), $"QLauncher_Portable_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);

                try
                {
                    // Копируем исполняемый файл и необходимые DLL/ресурсы.
                    string currentExe = Environment.ProcessPath ?? "";
                    if (!string.IsNullOrEmpty(currentExe) && File.Exists(currentExe))
                    {
                        string exeDir = Path.GetDirectoryName(currentExe) ?? AppBaseDir;
                        foreach (var file in Directory.GetFiles(exeDir))
                        {
                            string ext = Path.GetExtension(file).ToLowerInvariant();
                            if (ext == ".exe" || ext == ".dll" || ext == ".json" || ext == ".png" || ext == ".ico")
                            {
                                if (!file.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                                {
                                    File.Copy(file, Path.Combine(tempDir, Path.GetFileName(file)), true);
                                }
                            }
                        }
                    }

                    // Скачанные лаунчером JRE. Без них портативный пакет на другом
                    // компьютере сначала скачает Java заново, а в офлайне не запустится
                    // вообще. Это основная причина, по которой «перенос на флешку»
                    // не работал.
                    CopyDirectoryIfExists(Path.Combine(AppBaseDir, "runtime"), Path.Combine(tempDir, "runtime"));

                    // Маркер портативного режима (без расширения).
                    File.WriteAllBytes(Path.Combine(tempDir, "portable"), Array.Empty<byte>());

                    if (includeGameData)
                    {
                        // Настройки, аккаунты, сборки и сама игра. Копирование, а не
                        // перенос: исходная папка продолжает работать на месте.
                        string currentData = GetDefaultDataDirectory();
                        CopyDirectoryIfExists(currentData, Path.Combine(tempDir, "data"));
                    }
                    else
                    {
                        // Чистый пакет: пустая data/, чтобы получатель начал со своих
                        // настроек, а не с чужими аккаунтами.
                        Directory.CreateDirectory(Path.Combine(tempDir, "data"));
                    }

                    if (File.Exists(destinationZipPath))
                    {
                        File.Delete(destinationZipPath);
                    }

                    ZipFile.CreateFromDirectory(tempDir, destinationZipPath, CompressionLevel.Optimal, false);
                    return destinationZipPath;
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(tempDir))
                        {
                            Directory.Delete(tempDir, true);
                        }
                    }
                    catch { }
                }
            });
        }

        /// <summary>
        /// Рекурсивно копирует каталог, пропуская существующие подкаталоги в целе.
        /// Ошибки отдельных файлов не пробрасываются: экспорт пакета не должен
        /// падать из-за одного занятого файла кэша.
        /// </summary>
        private static void CopyDirectoryIfExists(string sourceDir, string targetDir)
        {
            if (!Directory.Exists(sourceDir)) return;

            Directory.CreateDirectory(targetDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string targetFile = Path.Combine(targetDir, Path.GetFileName(file));
                try
                {
                    File.Copy(file, targetFile, overwrite: true);
                }
                catch (Exception ex)
                {
                    CrashLogWriter.Write("PortableMode", $"Failed to copy '{file}'", ex);
                }
            }

            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                CopyDirectoryIfExists(dir, Path.Combine(targetDir, Path.GetFileName(dir)));
            }
        }

        public static void CleanupOldBackupsAndTemp()
        {
            try
            {
                string currentExe = Environment.ProcessPath ?? "";
                if (!string.IsNullOrEmpty(currentExe))
                {
                    string bakFile = currentExe + ".bak";
                    if (File.Exists(bakFile))
                    {
                        File.Delete(bakFile);
                    }
                }

                string tempUpdateDir = Path.Combine(Path.GetTempPath(), "QLauncher_Update");
                if (Directory.Exists(tempUpdateDir))
                {
                    Directory.Delete(tempUpdateDir, true);
                }
            }
            catch
            {
                // Игнорируем ошибки фоновой очистки
            }
        }
    }
}
