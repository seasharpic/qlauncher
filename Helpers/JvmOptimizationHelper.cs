using System;
using System.Collections.Generic;
using System.Linq;
using MinecraftLauncher.Services;

namespace MinecraftLauncher.Helpers
{
    /// <summary>
    /// Пресеты сборщика мусора. Порядок важен: он используется для сравнения
    /// «пресет не слабее максимально поддерживаемого» (см. <see cref="JvmOptimizationHelper.GetMaximumSupportedPreset"/>).
    /// Модель перенесена из PineconeMC (Prism Launcher, launcher/java/JavaPerformance.h).
    /// </summary>
    public enum GarbageCollectorPreset
    {
        /// <summary>Не задавать сборщик мусора — JVM выберет свой.</summary>
        None = 0,
        /// <summary>G1GC. Флаги Mojang + часть флагов Aikar.</summary>
        G1GC = 1,
        /// <summary>Shenandoah: минимальные паузы, требует Java 12+.</summary>
        Shenandoah = 2,
        /// <summary>ZGC: суб-миллисекундные паузы, требует Java 17+.</summary>
        ZGC = 3
    }

    /// <summary>
    /// Подбор аргументов JVM: сборщик мусора плюс базовые оптимизации.
    ///
    /// Раньше здесь было три строковых пресета ("default", "aikar", "zgc_shenandoah"),
    /// у которых было две проблемы:
    ///
    ///  1. Пресет с названием "zgc_shenandoah" не добавлял НИКАКИХ флагов Shenandoah —
    ///     только -XX:+UseZGC. При этом он выдавался безусловно, то есть на Java 8,
    ///     которая нужна для Minecraft 1.16 и старше, игра падала сразу на старте:
    ///     "Unrecognized VM option 'UseZGC'".
    ///  2. Не было учёта версии JVM вообще. Пользователь с Java 17 и ZGC-пресетом
    ///     работал, пользователь с Java 8 и тем же пресетом — нет, без единого
    ///     предупреждения в интерфейсе.
    ///
    /// Теперь версия JVM участвует в выборе: неподдерживаемый пресет понижается
    /// до максимально допустимого, а пользователь видит предупреждение в настройках.
    /// </summary>
    public static class JvmOptimizationHelper
    {
        /// <summary>
        /// Псевдо-пресет: выбирать сборщик по объёму выделенной памяти.
        /// Ровно то же правило, что в PineconeMC: от 4 ГБ — ZGC, иначе Shenandoah.
        /// </summary>
        public const string AutoPresetName = "Auto";

        public static string PresetToString(GarbageCollectorPreset preset)
        {
            return preset switch
            {
                GarbageCollectorPreset.None => "None",
                GarbageCollectorPreset.G1GC => "G1GC",
                GarbageCollectorPreset.Shenandoah => "Shenandoah",
                GarbageCollectorPreset.ZGC => "ZGC",
                _ => "None"
            };
        }

        /// <summary>
        /// Разбирает имя пресета из settings.json.
        ///
        /// Старые имена сопоставлены явно, а не «на всякий случай»: у пользователя с
        /// JvmPreset="zgc_shenandoah" после обновления должен остаться ZGC, а не
        /// молчаливый откат на G1GC из-за того, что строка не распозналась.
        /// </summary>
        public static GarbageCollectorPreset PresetFromString(string? value)
        {
            return (value ?? "").Trim().ToLowerInvariant() switch
            {
                "none" => GarbageCollectorPreset.None,
                "g1gc" => GarbageCollectorPreset.G1GC,
                "shenandoah" => GarbageCollectorPreset.Shenandoah,
                "zgc" => GarbageCollectorPreset.ZGC,

                // Предупреждение об устаревшем профиле пишется в crash-лог вызывающим
                // кодом через NormalizePresetName; здесь просто разбираем значение.
                "default" => GarbageCollectorPreset.G1GC,
                "aikar" => GarbageCollectorPreset.G1GC,
                "zgc_shenandoah" => GarbageCollectorPreset.ZGC,

                _ => GarbageCollectorPreset.G1GC
            };
        }

        /// <summary>
        /// Приводит значение настройки к каноническому виду: "Auto" сохраняется,
        /// старые имена превращаются в новые, неизвестные значения не трогаются
        /// (иначе autosave потерял бы то, что пользователь написал руками).
        /// </summary>
        public static string NormalizePresetName(string? value)
        {
            string trimmed = (value ?? "").Trim();

            if (string.Equals(trimmed, AutoPresetName, StringComparison.OrdinalIgnoreCase))
                return AutoPresetName;

            return trimmed.ToLowerInvariant() switch
            {
                "none" => "None",
                "g1gc" => "G1GC",
                "shenandoah" => "Shenandoah",
                "zgc" => "ZGC",
                "default" => "G1GC",
                "aikar" => "G1GC",
                "zgc_shenandoah" => "ZGC",
                _ => trimmed
            };
        }

        /// <summary>
        /// Самый «старший» пресет, который умеет текущая версия JVM.
        ///
        /// Shenandoah появился в OpenJDK 12, ZGC стал production-ready в 17
        /// (с macOS/aarch64 — тоже в 17). Для Java 8 остаётся только G1GC.
        /// </summary>
        public static GarbageCollectorPreset GetMaximumSupportedPreset(int javaMajorVersion)
        {
            if (javaMajorVersion >= 17) return GarbageCollectorPreset.ZGC;
            if (javaMajorVersion >= 12) return GarbageCollectorPreset.Shenandoah;
            return GarbageCollectorPreset.G1GC;
        }

        /// <summary>
        /// Итоговый пресет для запуска: разбирает настройку, раскрывает "Auto"
        /// по объёму памяти и понижает пресет, если JVM старше.
        /// Через warning возвращается текст для показа в настройках, либо null.
        /// </summary>
        public static GarbageCollectorPreset ResolvePreset(string? presetName, int ramMb, int javaMajorVersion, out string? warning)
        {
            warning = null;

            GarbageCollectorPreset preset;

            if (string.Equals((presetName ?? "").Trim(), AutoPresetName, StringComparison.OrdinalIgnoreCase))
            {
                // Тот же порог, что в PineconeMC: 4 ГБ и выше — ZGC, ниже — Shenandoah.
                preset = ramMb >= 4096 ? GarbageCollectorPreset.ZGC : GarbageCollectorPreset.Shenandoah;
            }
            else
            {
                preset = PresetFromString(presetName);
            }

            GarbageCollectorPreset maxSupported = GetMaximumSupportedPreset(javaMajorVersion);

            if (preset > maxSupported)
            {
                preset = maxSupported;
                warning = LocalizationService.Instance.Format("Str_Gc_Downgrade", javaMajorVersion, PresetToString(maxSupported));
            }

            return preset;
        }

        /// <summary>
        /// Флаги сборщика мусора. Наборы перенесены из PineconeMC один в один.
        /// </summary>
        public static IReadOnlyList<string> GetGarbageCollectorArgs(int javaMajorVersion, GarbageCollectorPreset preset)
        {
            switch (preset)
            {
                case GarbageCollectorPreset.None:
                    return Array.Empty<string>();

                case GarbageCollectorPreset.G1GC:
                    return new[]
                    {
                        "-XX:+UnlockExperimentalVMOptions",
                        "-XX:+UseG1GC",
                        "-XX:G1NewSizePercent=20",
                        "-XX:G1ReservePercent=20",
                        "-XX:MaxGCPauseMillis=50",
                        "-XX:G1HeapRegionSize=32M",
                        // Из флагов Aikar
                        "-XX:SurvivorRatio=32",
                        "-XX:MaxTenuringThreshold=1"
                    };

                case GarbageCollectorPreset.Shenandoah:
                    {
                        var args = new List<string> { "-XX:+UseShenandoahGC" };

                        // Генерационный Shenandoah: JEP 521, в разработке с JDK 24.
                        if (javaMajorVersion >= 24)
                        {
                            if (javaMajorVersion == 24)
                            {
                                args.Add("-XX:+UnlockExperimentalVMOptions");
                            }
                            args.Add("-XX:ShenandoahGCMode=generational");
                        }

                        return args;
                    }

                case GarbageCollectorPreset.ZGC:
                    {
                        // Имя опции именно "ZGC". "UseZGC" — название из JDK 9–10,
                        // его удалили в JDK 12, и JVM отвечает
                        // "Unrecognized VM option 'UseZGC'" и не запускает игру.
                        var args = new List<string>();

                        // До Java 21 ZGC был экспериментальным, и опцию нужно
                        // разблокировать. Разблокировка обязана идти ПЕРЕД самой
                        // опцией: HotSpot разбирает -XX в порядке аргументов,
                        // и поставленная после -XX:+ZGC, она не помогает.
                        if (javaMajorVersion < 21)
                        {
                            args.Add("-XX:+UnlockExperimentalVMOptions");
                        }

                        args.Add("-XX:+ZGC");

                        // Поколения появились в Java 21 и стали дефолтными в 23.
                        if (javaMajorVersion >= 21 && javaMajorVersion < 23)
                        {
                            args.Add("-XX:+ZGenerational");
                        }

                        // ZUncommit появился вместе с переименованием опции в JDK 12,
                        // на 11 его нет. Отключать возврат памяти ОС полезно, но
                        // ценой падения игры на старте — отдаём предпочтение запуску.
                        //
                        // ZGC не должен возвращать память ОС: это добавляет задержки
                        // потокам Java. См. гайд Oracle по тюнингу ZGC.
                        if (javaMajorVersion >= 12)
                        {
                            args.Add("-XX:-ZUncommit");
                        }

                        return args;
                    }

                default:
                    return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Базовые оптимизации, не зависящие от сборщика мусора.
        /// Каждый флаг взят из дефолтов Mojang и проверяется по версии JVM:
        /// неизвестный для этой версии флаг приводит к падению игры на старте.
        /// </summary>
        public static IReadOnlyList<string> GetBaseOptimizationArgs(int javaMajorVersion, GarbageCollectorPreset preset)
        {
            var args = new List<string>();

            // JEP 450 / JEP 519. На Java 24 опция ещё экспериментальная.
            if (javaMajorVersion >= 24)
            {
                if (javaMajorVersion == 24)
                {
                    args.Add("-XX:+UnlockExperimentalVMOptions");
                }
                args.Add("-XX:+UseCompactObjectHeaders");
            }

            // Дефолт Mojang. С Java 8 для G1GC, с Java 18 — уже и для ZGC.
            if ((javaMajorVersion >= 8 && preset != GarbageCollectorPreset.ZGC) || javaMajorVersion >= 18)
            {
                args.Add("-XX:+UseStringDeduplication");
            }

            // Параллельная обработка ссылок сборщиком, начиная с Java 6.
            if (javaMajorVersion <= 25)
            {
                args.Add("-XX:+ParallelRefProcEnabled");
            }

            args.Add("-XX:+AlwaysPreTouch");
            args.Add("-XX:+PerfDisableSharedMem");

            return args;
        }

        /// <summary>
        /// Полный набор флагов производительности: базовые оптимизации + сборщик мусора.
        /// Флаги, которых нет в конкретной сборке JVM, отбрасываются (см. <see cref="FilterSupportedFlags"/>).
        /// </summary>
        public static IReadOnlyList<string> GetPerformanceArguments(
            int javaMajorVersion,
            GarbageCollectorPreset preset,
            bool useOptimizedArgs,
            IReadOnlySet<string>? supportedFlags = null)
        {
            var args = new List<string>();

            if (useOptimizedArgs)
            {
                args.AddRange(GetBaseOptimizationArgs(javaMajorVersion, preset));
            }

            args.AddRange(GetGarbageCollectorArgs(javaMajorVersion, preset));

            // В PineconeMC здесь removeDuplicates: -XX:+UnlockExperimentalVMOptions
            // встречается и в базовых, и в флагах G1GC.
            var unique = new List<string>(args.Count);
            foreach (string arg in args)
            {
                if (!unique.Contains(arg))
                {
                    unique.Add(arg);
                }
            }

            return FilterSupportedFlags(unique, supportedFlags, javaMajorVersion);
        }

        /// <summary>
        /// Собирает финальные аргументы для строки запуска JVM.
        /// Флаги памяти (-Xms/-Xmx) сюда намеренно НЕ входят: ими владеет
        /// MinecraftArgumentBuilder, который и так их добавляет. Раньше они
        /// добавлялись в обоих местах, и -Xms попадал в строку дважды с разными
        /// значениями (512M и 1024M) — побеждал последний, то есть результат зависел
        /// от порядка, а не от настройки.
        ///
        /// <paramref name="supportedFlags"/> — множество флагов, о которых JVM сообщил
        /// через -XX:+PrintFlagsFinal. Если оно null (проба не удалась), флаги не
        /// фильтруются и всё решение принимается по версии JVM.
        /// </summary>
        public static string[] GetOptimizedJvmArguments(
            string? presetName,
            int ramMb,
            int javaMajorVersion,
            string? customArgs,
            bool useOptimizedArgs,
            IReadOnlySet<string>? supportedFlags,
            out string? warning)
        {
            GarbageCollectorPreset preset = ResolvePreset(presetName, ramMb, javaMajorVersion, out warning);

            var args = new List<string>(GetPerformanceArguments(javaMajorVersion, preset, useOptimizedArgs, supportedFlags));

            if (!string.IsNullOrWhiteSpace(customArgs))
            {
                // Пользовательские аргументы в конце: если он повторил наш флаг с
                // другим значением, побеждает его — это ожидаемое поведение.
                args.AddRange(customArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }

            return args.ToArray();
        }

        /// <summary>
        /// Убирает флаги, которых нет в конкретной сборке JVM.
        ///
        /// Нужно потому, что наличие опции определяется не только версией, но и
        /// сборщиком: Adoptium, Azul Zulu и Corretto комплектуются по-разному, и
        /// Shenandoah в Temurin может отсутствовать. Флаг из выбранного пресета,
        /// который JVM не знает, обрывает запуск игры с "Unrecognized VM option".
        /// Поэтому то, что реально поддерживает JVM, мы спрашиваем у него самого
        /// (JavaService.GetSupportedFlagsAsync), а версию используем как запасной
        /// вариант, когда проба не удалась.
        /// </summary>
        public static IReadOnlyList<string> FilterSupportedFlags(
            IReadOnlyList<string> args,
            IReadOnlySet<string>? supportedFlags,
            int javaMajorVersion)
        {
            if (supportedFlags != null && supportedFlags.Count > 0)
            {
                return args
                    .Where(arg => IsFlagSupported(arg, supportedFlags))
                    .ToList();
            }

            // Пробы нет — значит спросить JVM не вышло. Без фильтра пользователь
            // получает вместо игры "Unrecognized VM option", поэтому отбрасываем
            // хотя бы заведомо несуществующие для этой версии опции.
            return FilterSupportedFlagsByVersion(args, javaMajorVersion);
        }

        /// <summary>
        /// Оставляет только те -XX-опции, которые существуют в указанной версии JVM.
        ///
        /// Нужна как страховка на случай, когда проба опций не удалась: набор
        /// флагов зависит не только от версии, но и от сборки, и точным
        /// источником остаётся сама JVM. Но когда спросить её нельзя, лучше
        /// срезать по заведомо известным границам, чем запускать игру с флагом,
        /// которого нет.
        ///
        /// Опции, которых нет в таблице, остаются: их появление уже
        /// отсекается по версии в GetBaseOptimizationArgs.
        /// </summary>
        public static IReadOnlyList<string> FilterSupportedFlagsByVersion(
            IReadOnlyList<string> args,
            int supportedFlagsJavaMajorVersion)
        {
            if (supportedFlagsJavaMajorVersion <= 0)
            {
                return args;
            }

            int java = supportedFlagsJavaMajorVersion;

            // Минимальная версия JVM, в которой опция существует.
            var introducedIn = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["UseG1GC"] = 6,
                ["UnlockExperimentalVMOptions"] = 7,
                ["MaxGCPauseMillis"] = 8,
                ["G1NewSizePercent"] = 8,
                ["G1ReservePercent"] = 8,
                ["G1HeapRegionSize"] = 8,
                ["SurvivorRatio"] = 8,
                ["MaxTenuringThreshold"] = 8,
                ["UseParallelGC"] = 6,
                ["UseConcMarkSweepGC"] = 8,
                ["ZGC"] = 11,
                ["UseShenandoahGC"] = 12,
                ["ZUncommit"] = 12,
                ["ZGenerational"] = 21,
                ["ShenandoahGCMode"] = 24,
            };

            return args
                .Where(arg =>
                {
                    string? name = TryGetFlagName(arg);
                    if (name == null) return true;

                    return !introducedIn.TryGetValue(name, out int minJava) || java >= minJava;
                })
                .ToList();
        }

        /// <summary>
        /// Имя опции во флаге -XX вида, либо null, если это не -XX-флаг.
        /// </summary>
        private static string? TryGetFlagName(string arg)
        {
            if (!arg.StartsWith("-XX:", StringComparison.Ordinal))
            {
                return null;
            }

            string name = arg[4..];
            if (name.StartsWith('+') || name.StartsWith('-'))
            {
                name = name[1..];
            }

            int eq = name.IndexOf('=');
            if (eq >= 0)
            {
                name = name[..eq];
            }

            return name.Length == 0 ? null : name;
        }

        /// <summary>
        /// Имя опции JVM во флаге вида -XX:+UseG1GC, -XX:-ZUncommit или -XX:MaxGCPauseMillis=50.
        /// Флаги, которые не относятся к -XX (например, -Xmx), всегда считаются
        /// поддерживаемыми: PrintFlagsFinal про них ничего не сообщает.
        /// </summary>
        public static bool IsFlagSupported(string arg, IReadOnlySet<string> supportedFlags)
        {
            if (!arg.StartsWith("-XX:", StringComparison.Ordinal))
            {
                return true;
            }

            string name = arg[4..];
            if (name.StartsWith('+') || name.StartsWith('-'))
            {
                name = name[1..];
            }

            int eq = name.IndexOf('=');
            if (eq >= 0)
            {
                name = name[..eq];
            }

            return name.Length == 0 || supportedFlags.Contains(name);
        }
    }
}