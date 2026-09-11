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

        public static bool EnablePortableMode()
        {
            try
            {
                string marker = Path.Combine(AppBaseDir, "portable");
                if (!File.Exists(marker))
                {
                    File.WriteAllBytes(marker, Array.Empty<byte>());
                }

                string dataDir = Path.Combine(AppBaseDir, "data");
                if (!Directory.Exists(dataDir))
                {
                    Directory.CreateDirectory(dataDir);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<string> ExportPortablePackageAsync(string destinationZipPath)
        {
            return await Task.Run(() =>
            {
                string tempDir = Path.Combine(Path.GetTempPath(), $"QLauncher_Portable_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);

                try
                {
                    // Copy executable and essential DLLs or single binary
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

                    // Create empty 'portable' marker file (without extension)
                    File.WriteAllBytes(Path.Combine(tempDir, "portable"), Array.Empty<byte>());

                    // Create clean 'data' folder
                    Directory.CreateDirectory(Path.Combine(tempDir, "data"));

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
