using System;
using System.IO;

namespace MinecraftLauncher.Helpers
{
    public static class CacheCleanerHelper
    {
        public static long CalculateCacheSize(string gamePath)
        {
            long totalBytes = 0;

            try
            {
                string logsDir = Path.Combine(gamePath, "logs");
                if (Directory.Exists(logsDir))
                {
                    foreach (var file in Directory.GetFiles(logsDir, "*.*", SearchOption.AllDirectories))
                    {
                        totalBytes += new FileInfo(file).Length;
                    }
                }

                string crashDir = Path.Combine(gamePath, "crash-reports");
                if (Directory.Exists(crashDir))
                {
                    foreach (var file in Directory.GetFiles(crashDir, "*.*", SearchOption.AllDirectories))
                    {
                        totalBytes += new FileInfo(file).Length;
                    }
                }

                string cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache");
                if (Directory.Exists(cacheDir))
                {
                    foreach (var file in Directory.GetFiles(cacheDir, "*.*", SearchOption.AllDirectories))
                    {
                        totalBytes += new FileInfo(file).Length;
                    }
                }

                string avatarDir = Path.Combine(LauncherPathHelper.GetDefaultDataDirectory(), "avatars");
                if (Directory.Exists(avatarDir))
                {
                    foreach (var file in Directory.GetFiles(avatarDir, "*.*", SearchOption.AllDirectories))
                    {
                        totalBytes += new FileInfo(file).Length;
                    }
                }

                string tempUpdate = Path.Combine(Path.GetTempPath(), "QLauncher_Update");
                if (Directory.Exists(tempUpdate))
                {
                    foreach (var file in Directory.GetFiles(tempUpdate, "*.*", SearchOption.AllDirectories))
                    {
                        totalBytes += new FileInfo(file).Length;
                    }
                }
            }
            catch { }

            return totalBytes;
        }

        /// <summary>
        /// Очищает кэш и возвращает фактически освобождённый объём в МБ.
        ///
        /// Две правки:
        ///  - Раньше размер считался до удаления, а ошибки глушились пустым catch,
        ///    поэтому тост всегда сообщал об освобождённом месте, даже если ничего
        ///    не удалилось. Теперь размер каждой папки измеряется и суммируется
        ///    только после успешного удаления.
        ///  - Папка avatars проверялась в неверном месте (реальный кэш —
        ///    BaseDirectory\Cache\Avatars), и ветка была мёртвой.
        /// </summary>
        public static double CleanCache(string gamePath)
        {
            long bytesFreed = 0;

            void TryDelete(string dir, string description)
            {
                try
                {
                    if (!Directory.Exists(dir)) return;

                    long size = CalculateDirectorySize(dir);
                    Directory.Delete(dir, recursive: true);

                    bytesFreed += size;
                }
                catch (Exception ex)
                {
                    // Раньше единственный catch покрывал всё сразу: неудача в
                    // первом Delete отменяла все остальные. Теперь каждая папка
                    // независима.
                    CrashLogWriter.Write("CacheCleaner", $"Failed to clean {description} at '{dir}'", ex);
                }
            }

            TryDelete(Path.Combine(gamePath, "logs"), "logs");
            TryDelete(Path.Combine(gamePath, "crash-reports"), "crash-reports");

            // Кэш аватаров живёт внутри Cache, поэтому удаление Cache
            // покрывает и его. Отдельно удалять нечего.
            TryDelete(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache"), "Cache");

            TryDelete(Path.Combine(LauncherPathHelper.GetDefaultDataDirectory(), "Cache", "Avatars"), "avatars");

            // Каталог загрузки обновления: удаляем только если он существует,
            // и это безопасно, потому что загрузка сейчас не идёт.
            TryDelete(Path.Combine(Path.GetTempPath(), "QLauncher_Update"), "update temp");

            return bytesFreed / (1024.0 * 1024.0);
        }

        /// <summary>
        /// Размер содержимого каталога рекурсивно. Раньше CalculateCacheSize
        /// считала только logs и crash-reports, хотя удалялось ещё три каталога.
        /// </summary>
        private static long CalculateDirectorySize(string dir)
        {
            long total = 0;

            try
            {
                foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        total += new FileInfo(file).Length;
                    }
                    catch
                    {
                        // Файл мог быть удалён во время обхода.
                    }
                }
            }
            catch
            {
                // Каталог недоступен — считаем как пустой.
            }

            return total;
        }
    }
}
