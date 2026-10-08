using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using MinecraftLauncher.Helpers;

namespace MinecraftLauncher.Services
{
    /// <summary>
    /// Определяет минимальную версию Java, которую требуют моды в сборке.
    ///
    /// Зачем это нужно. Версия Java для версии Minecraft берётся из таблицы
    /// в JavaCompatibilityService, и для версий вида 26.x там стоит 21 —
    /// потому что больше некуда смотреть: правило по умолчанию не знает про
    /// будущие релизы. Но реальное требование задают моды, и на практике оно
    /// оказывается выше:
    ///
    ///   [main/ERROR]: Uncaught exception in thread "main"
    ///   java.lang.RuntimeException: Error parsing or using Mixin config
    ///       fabric-block-getter-api-v2.mixins.json for mod fabric-block-getter-api-v2
    ///   Caused by: java.lang.IllegalArgumentException: The requested compatibility
    ///       level JAVA_25 could not be set. Level is not supported by the active
    ///       JRE or ASM version (Java 21.0, ASM 9.10.1)
    ///
    /// То есть мод требует уровня совместимости миксинов JAVA_25, а игра
    /// запущена на Java 21. Таблица по версии игры такое требование не видит
    /// в принципе, а ломается всё уже внутри Fabric Loader.
    ///
    /// Поэтому требование читается из самих модов:
    ///  - fabric.mod.json → environment.java (например ">=25");
    ///  - *.mixins.json → compatibilityLevel (например "JAVA_25").
    ///
    /// Берётся максимум по всем модам: разные моды заявляют разное, и
    /// запускать можно только на максимальном.
    /// </summary>
    public static class ModJavaRequirementService
    {
        /// <summary>Шаблон уровня совместимости миксинов: JAVA_21, JAVA_25…</summary>
        private static readonly Regex CompatibilityLevel =
            new(@"JAVA_(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Версия в ограничении вида ">=25", ">21" или просто "25".
        /// Диапазоны вида [25,) тоже встречаются, но цифра в них одна.
        /// </summary>
        private static readonly Regex VersionConstraint =
            new(@"(?<op>[><=]+)?\s*(?<ver>\d+)", RegexOptions.Compiled);

        /// <summary>
        /// Кэш по папке модов. Сканирование открывает каждый jar как zip, а
        /// модов в сборке бывает двадцать с лишним, поэтому результат
        /// запоминается и пересчитывается только когда папка изменилась.
        /// </summary>
        private static readonly Dictionary<string, (DateTime Stamp, int Required)> Cache =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly object CacheGate = new();

        /// <summary>
        /// Максимальная требуемая версия Java среди модов. Ноль, если
        /// требований нет, папки нет или разобрать не удалось.
        /// </summary>
        public static int GetRequiredJavaVersion(string? modsFolder)
        {
            if (string.IsNullOrWhiteSpace(modsFolder) || !Directory.Exists(modsFolder))
            {
                return 0;
            }

            DateTime stamp;
            try
            {
                // Отпечаток берётся максимумом по обеим папкам: processedMods
                // меняется независимо от mods, и по одной только метке mods/
                // результат остался бы устаревшим.
                stamp = Directory.GetLastWriteTimeUtc(modsFolder);

                foreach (string folder in GetScanFolders(modsFolder))
                {
                    if (!Directory.Exists(folder)) continue;

                    DateTime folderStamp = Directory.GetLastWriteTimeUtc(folder);
                    if (folderStamp > stamp) stamp = folderStamp;
                }
            }
            catch
            {
                return 0;
            }

            lock (CacheGate)
            {
                if (Cache.TryGetValue(modsFolder, out var cached) && cached.Stamp == stamp)
                {
                    return cached.Required;
                }
            }

            int required = Scan(modsFolder);

            lock (CacheGate)
            {
                Cache[modsFolder] = (stamp, required);
            }

            return required;
        }

        /// <summary>
        /// Очищает кэш. Нужен после массовой установки или удаления модов,
        /// когда папка меняется быстрее, чем обновляется её метка времени.
        /// </summary>
        public static void InvalidateCache()
        {
            lock (CacheGate)
            {
                Cache.Clear();
            }
        }

        private static int Scan(string modsFolder)
        {
            int max = 0;

            foreach (string folder in GetScanFolders(modsFolder))
            {
                string[] jars;
                try
                {
                    jars = Directory.GetFiles(folder, "*.jar", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    continue;
                }

                foreach (string jar in jars)
                {
                    try
                    {
                        max = Math.Max(max, ReadJarRequirement(jar));
                    }
                    catch (Exception ex)
                    {
                        // Повреждённый или не-zip файл не должен ломать запуск:
                        // требование Java просто не будет учтено.
                        CrashLogWriter.Write("ModJavaRequirement", $"Failed to inspect '{Path.GetFileName(jar)}'", ex);
                    }
                }
            }

            return max;
        }

        /// <summary>
        /// Папки, в которых могут лежать загружаемые моды.
        ///
        /// Кроме mods/ проверяется .fabric\processedMods — это кэш Fabric
        /// Loader с перепакованными модами. В реальной сборке требование
        /// JAVA_25 нашлось именно там, пока в mods/ лежали лишь четыре jar:
        /// лоадер берёт моды и из своего кэша, и сканирование одной mods/
        /// требование пропускало.
        /// </summary>
        private static IEnumerable<string> GetScanFolders(string modsFolder)
        {
            yield return modsFolder;

            string? instance = null;

            try
            {
                instance = Path.GetDirectoryName(
                    modsFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            catch { }

            if (!string.IsNullOrEmpty(instance))
            {
                yield return Path.Combine(instance, ".fabric", "processedMods");
            }
        }

        /// <summary>
        /// Максимальное из требований, объявленных внутри одного мода.
        /// </summary>
        private static int ReadJarRequirement(string jarPath)
        {
            using var archive = ZipFile.OpenRead(jarPath);

            int max = 0;

            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.Equals("fabric.mod.json", StringComparison.OrdinalIgnoreCase))
                {
                    max = Math.Max(max, ReadFabricModJson(entry));
                }
                else if (entry.FullName.EndsWith(".mixins.json", StringComparison.OrdinalIgnoreCase))
                {
                    max = Math.Max(max, ReadMixinsJson(entry));
                }
            }

            return max;
        }

        /// <summary>
        /// fabric.mod.json → environment.java, например ">=25".
        /// </summary>
        private static int ReadFabricModJson(ZipArchiveEntry entry)
        {
            using var stream = entry.Open();
            using var doc = JsonDocument.Parse(stream);

            if (!doc.RootElement.TryGetProperty("environment", out var env) ||
                !env.TryGetProperty("java", out var javaElement))
            {
                return 0;
            }

            string? constraint = javaElement.GetString();
            return string.IsNullOrWhiteSpace(constraint) ? 0 : ParseConstraint(constraint);
        }

        /// <summary>
        /// *.mixins.json → compatibilityLevel, например "JAVA_25".
        /// </summary>
        private static int ReadMixinsJson(ZipArchiveEntry entry)
        {
            using var stream = entry.Open();
            using var doc = JsonDocument.Parse(stream);

            if (!doc.RootElement.TryGetProperty("compatibilityLevel", out var level))
            {
                return 0;
            }

            string? value = level.GetString();
            if (string.IsNullOrWhiteSpace(value)) return 0;

            var m = CompatibilityLevel.Match(value);
            return m.Success && int.TryParse(m.Groups[1].Value, out int major) ? major : 0;
        }

        /// <summary>
        /// Разбирает ограничение версии Java и берёт из него требуемую.
        /// Поддерживаются "25", ">=25", ">21", "[25,)".
        /// </summary>
        private static int ParseConstraint(string constraint)
        {
            // Диапазон вида [25,) или [21,25) — берём обе границы и максимум:
            // для запуска нужен верхний предел, иначе мод не загрузится.
            var all = VersionConstraint.Matches(constraint);
            int max = 0;

            foreach (Match m in all)
            {
                if (int.TryParse(m.Groups["ver"].Value, out int v))
                {
                    max = Math.Max(max, v);
                }
            }

            return max;
        }
    }
}