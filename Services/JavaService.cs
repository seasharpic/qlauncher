using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MinecraftLauncher.Helpers;

namespace MinecraftLauncher.Services
{
    public interface IJavaService
    {
        int GetRequiredJavaVersion(string mcVersion);

        /// <summary>
        /// Требуемая версия Java с учётом требований модов: планку по версии
        /// игры поднимает максимум из того, что объявляют сами моды.
        /// </summary>
        int GetRequiredJavaVersion(string mcVersion, string? modsFolder);

        Task<string> ResolveJavaExecutableAsync(string configuredJavaPath, string gameVersion, Action<string>? statusCallback = null);

        /// <param name="modsFolder">
        /// Папка mods инстанса. Если задана, требование Java дополнительно
        /// повышается по данным из самих модов.
        /// </param>
        Task<string> ResolveJavaExecutableAsync(
            string configuredJavaPath,
            string gameVersion,
            Action<string>? statusCallback,
            string? modsFolder);
    }

    public class JavaService : IJavaService
    {
        public static JavaService Instance { get; } = new JavaService();

        /// <summary>
        /// Требуемая версия Java.
        ///
        /// Раньше правило существовало здесь и в JavaCompatibilityService, причём
        /// они давали разные ответы (по умолчанию 21 против 17) и по-разному
        /// обрабатывали теги сборок. Теперь используется общее правило.
        /// </summary>
        public int GetRequiredJavaVersion(string mcVersion)
        {
            return JavaCompatibilityService.GetRequiredJavaVersionCore(mcVersion);
        }

        /// <summary>
        /// Требуемая версия Java с учётом требований модов.
        ///
        /// Правило по версии игры для будущих релизов (26.x и далее) отдаёт 21 —
        /// не из ошибки, а потому что таблица физически не может знать про них.
        /// Настоящее требование часто задаёт мод: mixin-конфиг с
        /// compatibilityLevel "JAVA_25" обрывает Fabric Loader уже во время игры.
        /// Поэтому в папке модов ищется максимум из объявленного, и он
        /// поднимает планку, если выше версии по игре.
        /// </summary>
        /// <param name="mcVersion">Версия игры.</param>
        /// <param name="modsFolder">Папка mods инстанса.</param>
        public int GetRequiredJavaVersion(string mcVersion, string? modsFolder)
        {
            int byGameVersion = GetRequiredJavaVersion(mcVersion);
            int byMods = ModJavaRequirementService.GetRequiredJavaVersion(modsFolder);

            return byMods > byGameVersion ? byMods : byGameVersion;
        }

        public async Task<string> ResolveJavaExecutableAsync(string configuredJavaPath, string gameVersion, Action<string>? statusCallback = null)
        {
            return await ResolveJavaExecutableAsync(configuredJavaPath, gameVersion, statusCallback, modsFolder: null);
        }

        /// <param name="modsFolder">
        /// Папка mods инстанса. Если задана, требование Java дополнительно
        /// повышается по данным из самих модов.
        /// </param>
        public async Task<string> ResolveJavaExecutableAsync(
            string configuredJavaPath,
            string gameVersion,
            Action<string>? statusCallback,
            string? modsFolder)
        {
            int versionRequired = GetRequiredJavaVersion(gameVersion);
            int requiredVer = modsFolder == null
                ? versionRequired
                : GetRequiredJavaVersion(gameVersion, modsFolder);

            // Раньше путь из настроек возвращался без проверки версии: пользователь мог
            // указать Java 8, и сборка 1.21 молча стартовала на ней.
            if (!string.IsNullOrWhiteSpace(configuredJavaPath) && File.Exists(configuredJavaPath))
            {
                int configuredVer = DetectJavaMajorVersion(configuredJavaPath);

                if (configuredVer == requiredVer)
                {
                    return configuredJavaPath;
                }

                statusCallback?.Invoke(
                    LocalizationService.Instance.Format("Str_Java_NotConfigured", configuredVer, requiredVer));
            }

            // Раньше путь из настроек возвращался без проверки версии: пользователь мог
            // указать Java 8, и сборка 1.21 молча стартовала на ней.
            if (!string.IsNullOrWhiteSpace(configuredJavaPath) && File.Exists(configuredJavaPath))
            {
                int configuredVer = DetectJavaMajorVersion(configuredJavaPath);

                if (configuredVer == requiredVer)
                {
                    return configuredJavaPath;
                }

                statusCallback?.Invoke(
                    LocalizationService.Instance.Format("Str_Java_NotConfigured", configuredVer, requiredVer));
            }

            string runtimeFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime", $"java-{requiredVer}");
            string localJavaw = Path.Combine(runtimeFolder, "bin", "javaw.exe");

            if (File.Exists(localJavaw))
            {
                return localJavaw;
            }

            // Раньше здесь возвращался первый найденный javaw.exe без проверки версии.
            // Теперь проверяем мажор и пропускаем несовпадающие вложенные папки.
            if (Directory.Exists(runtimeFolder))
            {
                var files = Directory.GetFiles(runtimeFolder, "javaw.exe", SearchOption.AllDirectories);

                foreach (var file in files)
                {
                    if (DetectJavaMajorVersion(file) == requiredVer)
                    {
                        return file;
                    }
                }
            }

            string? javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                string sysJavaw = Path.Combine(javaHome, "bin", "javaw.exe");
                if (File.Exists(sysJavaw) && DetectJavaMajorVersion(sysJavaw) == requiredVer)
                {
                    return sysJavaw;
                }
            }

            statusCallback?.Invoke(LocalizationService.Instance.Format("Str_Java_Downloading", requiredVer));

            try
            {
                return await DownloadOpenJDKAsync(requiredVer, runtimeFolder, statusCallback);
            }
            catch (Exception ex) when (requiredVer > versionRequired)
            {
                // Версию подняли моды, а такой Java в зеркалах нет. Это не повод
                // отказывать в запуске: возвращаемся к требованию по версии игры.
                // Игра упадёт с понятной ошибкой от Fabric, но запустится.
                CrashLogWriter.Write(
                    "JavaService",
                    $"Java {requiredVer} required by mods is unavailable, falling back to {versionRequired}",
                    ex);

                statusCallback?.Invoke(LocalizationService.Instance.Format("Str_Java_ModsFallback", versionRequired));

                string fallbackFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "runtime", $"java-{versionRequired}");
                string fallbackJavaw = Path.Combine(fallbackFolder, "bin", "javaw.exe");

                if (File.Exists(fallbackJavaw))
                {
                    return fallbackJavaw;
                }

                return await DownloadOpenJDKAsync(versionRequired, fallbackFolder, statusCallback);
            }
        }

        /// <summary>
        /// Кэш результатов пробы: путь JVM → множество поддерживаемых флагов.
        /// Проба стоит запуска процесса, поэтому переигрывать её на каждый запуск
        /// игры незачем: состав опций у конкретного javaw.exe не меняется.
        /// </summary>
        /// Максимум проходов проверки. JVM сообщает об отвергнутых опциях по
        /// одной и прерывает разбор на первой, поэтому за проход удаляется
        /// только часть флагов — остальные вылезут в следующем.
        private const int MaxValidationPasses = 4;

        /// <summary>
        /// Проверяет, принимает ли JVM набор флагов, не запуская игру.
        ///
        /// Последний рубеж перед падением: JVM сообщает об отвергнутых опциях
        /// именами, поэтому лишние запуски процесса позволяют вырезать ровно те
        /// флаги, которые не работают, вместо того чтобы ронять игру сообщением
        /// "Unrecognized VM option". Вызывается только когда проба состава
        /// опций не удалась.
        ///
        /// Проверка повторяется, а не делается один раз: JVM разбирает -XX по
        /// порядку и обрывается на первой неизвестной опции, поэтому за один
        /// проход вычищается лишь часть флагов. Раньше проход был один — с
        /// ZGC удалялся только он, а следом тут же следовал отказ по
        /// ZUncommit, и игра падала в диалог JVM.
        ///
        /// Если отвергнутые опции распознать не удалось, возвращается признак
        /// «срезать всё»: это надёжнее, чем оставить заведомо нерабочий флаг.
        /// </summary>
        public static async Task<(IReadOnlyList<string> Args, bool StripAll)> ValidateJvmArgsAsync(
            string javaPath,
            IReadOnlyList<string> args)
        {
            if (string.IsNullOrWhiteSpace(javaPath) || !File.Exists(javaPath) || args.Count == 0)
            {
                return (args, false);
            }

            // Проверяем через java.exe, а не javaw.exe. javaw — консольное
            // приложение без окна: при ошибке старта он показывает модальное
            // окно JVM поверх лаунчера, и пользователь видит его вместо игры.
            string probePath = ResolveConsoleExecutable(javaPath);

            IReadOnlyList<string> current = args;

            for (int pass = 1; pass <= MaxValidationPasses; pass++)
            {
                var outcome = await RunValidationPassAsync(probePath, current);

                if (outcome.Result == ValidationResult.Accepted)
                {
                    return (current, false);
                }

                if (outcome.Result == ValidationResult.Unknown)
                {
                    return (Array.Empty<string>(), true);
                }

                var remaining = current
                    .Where(arg => !outcome.RejectedFlags.Contains(OptionName(arg)))
                    .ToList();

                if (remaining.Count == current.Count)
                {
                    // Ничего не вырезалось: дальше крутить бессмысленно.
                    return (Array.Empty<string>(), true);
                }

                CrashLogWriter.Write(
                    "JavaService",
                    $"JVM rejected flags ({pass}): {string.Join(", ", outcome.RejectedFlags)}",
                    null);

                current = remaining;
            }

            // Не сходится за отведённое число проходов — безопаснее срезать всё.
            return (Array.Empty<string>(), true);
        }

        private enum ValidationResult
        {
            /// <summary>JVM приняла набор флагов.</summary>
            Accepted,

            /// <summary>JVM отвергла часть опций, имена известны.</summary>
            Rejected,

            /// <summary>Проверка не дала разумного результата.</summary>
            Unknown,
        }

        private sealed record ValidationOutcome(ValidationResult Result, IReadOnlySet<string> RejectedFlags)
        {
            public static ValidationOutcome Ok { get; } =
                new(ValidationResult.Accepted, new HashSet<string>(StringComparer.Ordinal));
        }

        private static async Task<ValidationOutcome> RunValidationPassAsync(
            string javaPath,
            IReadOnlyList<string> args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = javaPath,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                foreach (var arg in args)
                {
                    psi.ArgumentList.Add(arg);
                }

                psi.ArgumentList.Add("-version");

                using var p = System.Diagnostics.Process.Start(psi);
                if (p == null)
                {
                    return new ValidationOutcome(ValidationResult.Unknown, new HashSet<string>());
                }

                Task<string> stdoutTask = p.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = p.StandardError.ReadToEndAsync();

                await Task.WhenAny(Task.WhenAll(stdoutTask, stderrTask), Task.Delay(8000));

                if (!p.WaitForExit(1000))
                {
                    try { p.Kill(); } catch { }
                    return new ValidationOutcome(ValidationResult.Unknown, new HashSet<string>());
                }

                if (p.ExitCode == 0)
                {
                    return ValidationOutcome.Ok;
                }

                string error = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : string.Empty;
                string output = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : string.Empty;
                string diagnostic = error.Length > 0 ? error : output;

                var rejected = ParseRejectedFlags(diagnostic);
                if (rejected.Count == 0)
                {
                    CrashLogWriter.Write(
                        "JavaService",
                        $"JVM rejected JVM args, exit {p.ExitCode}: {FirstLine(diagnostic)}",
                        null);

                    return new ValidationOutcome(ValidationResult.Unknown, new HashSet<string>());
                }

                return new ValidationOutcome(ValidationResult.Rejected, rejected);
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("JavaService", "JVM args validation failed", ex);
                return new ValidationOutcome(ValidationResult.Unknown, new HashSet<string>());
            }
        }

        /// <summary>
        /// Консольный двойник javaw.exe — java.exe в той же папке.
        ///
        /// javaw.exe при ошибке старта показывает модальное окно JVM. Проба идёт
        /// в фоне и не должна выскакивать окном, поэтому для служебных запусков
        /// берётся java.exe; если его нет, остаётся исходный путь.
        /// </summary>
        private static string ResolveConsoleExecutable(string javaPath)
        {
            try
            {
                string? dir = Path.GetDirectoryName(javaPath);
                if (string.IsNullOrEmpty(dir)) return javaPath;

                if (!Path.GetFileName(javaPath).Equals("javaw.exe", StringComparison.OrdinalIgnoreCase))
                {
                    return javaPath;
                }

                string console = Path.Combine(dir, "java.exe");
                return File.Exists(console) ? console : javaPath;
            }
            catch
            {
                return javaPath;
            }
        }

        /// <summary>
        /// Достаёт имена опций, которые JVM отверг, из её диагностики.
        /// Формат: Unrecognized VM option 'FooBar'
        /// </summary>
        private static HashSet<string> ParseRejectedFlags(string diagnostic)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);

            foreach (System.Text.RegularExpressions.Match m in
                Regex.Matches(diagnostic, @"(?:Unrecognized|Unusable|experimental)\s+VM\s+option\s+'?([A-Za-z][A-Za-z0-9_]*)"))
            {
                result.Add(m.Groups[1].Value);
            }

            return result;
        }

        /// <summary>
        /// Имя опции во флаге -XX, без знака и значения.
        /// </summary>
        private static string OptionName(string arg)
        {
            if (!arg.StartsWith("-XX:", StringComparison.Ordinal)) return arg;

            string name = arg[4..];
            if (name.StartsWith('+') || name.StartsWith('-')) name = name[1..];

            int eq = name.IndexOf('=');
            return eq >= 0 ? name[..eq] : name;
        }
        private static string FirstLine(string text)
        {
            int i = text.IndexOfAny(new[] { '\r', '\n' });
            return i > 0 ? text[..i] : text;
        }

        private static readonly ConcurrentDictionary<string, Task<IReadOnlySet<string>?>> FlagProbeCache =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Какие -XX-опции реально понимает эта JVM.
        ///
        /// Зачем: наличие опции определяется не только версией, но и сборкой.
        /// Shenandoah есть в OpenJDK начиная с 12, но в конкретных дистрибутивах
        /// (Temurin, Zulu, Corretto) он собран по-разному, и в Temurin его может
        /// не быть вовсе. Флаг, которого JVM не знает, обрывает запуск игры
        /// сообщением "Unrecognized VM option" — то есть пользователь получает
        /// краш вместо игры, выбрав пресет в настройках.
        ///
        /// Поэтому мы спрашиваем саму JVM: -XX:+PrintFlagsFinal печатает полный
        /// список опций и выходит с кодом 0. Если проба не удалась, возвращается
        /// null, и вызывающий код решает по версии JVM (см. JvmOptimizationHelper).
        /// </summary>
        public static Task<IReadOnlySet<string>?> GetSupportedFlagsAsync(string javaPath)
        {
            if (string.IsNullOrWhiteSpace(javaPath) || !File.Exists(javaPath))
            {
                return Task.FromResult<IReadOnlySet<string>?>(null);
            }

            if (FlagProbeCache.TryGetValue(javaPath, out var cached))
            {
                return cached;
            }

            // GetOrAdd здесь использовать нельзя: проба может вернуть null,
            // и ConcurrentDictionary закэширует этот null навсегда. Одна
            // неудача — например, файл JDK ещё распаковывался или его держал
            // антивирус — навсегда отключала бы отбрасывание неизвестных
            // флагов, и игра падала бы с "Unrecognized VM option" до конца
            // работы программы. Неудачу не кэшируем — следующий вызов попробует снова.
            var probe = ProbeFlagsAsync(javaPath);

            if (probe.IsCompletedSuccessfully && probe.Result == null)
            {
                return probe;
            }

            return AwaitAndCacheAsync(javaPath, probe);
        }

        /// <summary>
        /// Кэширует результат пробы, только если она удалась.
        /// </summary>
        private static async Task<IReadOnlySet<string>?> AwaitAndCacheAsync(
            string javaPath,
            Task<IReadOnlySet<string>?> probe)
        {
            var result = await probe.ConfigureAwait(false);

            if (result != null)
            {
                FlagProbeCache.TryAdd(javaPath, Task.FromResult<IReadOnlySet<string>?>(result));
            }
            else
            {
                // Неудачную пробу могла уже запустить параллельная попытка —
                // забираем её из кэша, чтобы следующий вызов не ждал заново.
                FlagProbeCache.TryRemove(javaPath, out _);
            }

            return result;
        }

        private static async Task<IReadOnlySet<string>?> ProbeFlagsAsync(string javaPath)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = javaPath,
                    Arguments = "-XX:+PrintFlagsFinal -version",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var p = System.Diagnostics.Process.Start(psi);
                if (p == null)
                {
                    return null;
                }

                // Оба потока читаются параллельно, иначе возможен дедлок пайпов —
                // та же причина, что и в DetectJavaMajorVersion.
                Task<string> stdoutTask = p.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = p.StandardError.ReadToEndAsync();

                await Task.WhenAny(Task.WhenAll(stdoutTask, stderrTask), Task.Delay(5000));

                if (!p.WaitForExit(500))
                {
                    // JVM завис — скорее всего это вообще не java. Не оставляем процесс.
                    try { p.Kill(); } catch { }
                    return null;
                }

                string output = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : string.Empty;
                if (output.Length == 0 && stderrTask.IsCompletedSuccessfully)
                {
                    output = stderrTask.Result;
                }

                // Строки вида: "     bool UseG1GC    = true   {product} {default}"
                // У глобальных опций в начале строки стоит префикс "[Global flags]".
                var flags = new HashSet<string>(StringComparer.Ordinal);
                foreach (var line in output.Split('\n'))
                {
                    var m = Regex.Match(line, @"^\s*(?:\[[^\]]*\]\s*)?([A-Za-z][A-Za-z0-9_]*)\s*=");
                    if (m.Success)
                    {
                        flags.Add(m.Groups[1].Value);
                    }
                }

                return flags.Count > 0 ? flags : null;
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("JavaService", $"Failed to probe JVM flags of '{javaPath}'", ex);
                return null;
            }
        }

        public static int DetectJavaMajorVersion(string javaPath)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = javaPath,
                    Arguments = "-version",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = System.Diagnostics.Process.Start(psi);
                if (p != null)
                {
                    // Оба потока читаются параллельно, иначе возможен дедлок пайпов.
                    // Сам метод синхронный, но вызывается уже не с UI-потока.
                    Task<string> stdoutTask = p.StandardOutput.ReadToEndAsync();
                    Task<string> stderrTask = p.StandardError.ReadToEndAsync();
                    Task.WhenAll(stdoutTask, stderrTask).Wait(2000);

                    string output = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result : string.Empty;

                    // "java -version" печатает версию в stderr, а не в stdout.
                    // Условие стояло наоборот — stderr добавлялся, когда он НЕ
                    // завершился, — поэтому версия не читалась никогда и метод
                    // уходил в запасное значение. Дальше это выдавало Java 8 за
                    // Java 21, лаунчер выбирал ZGC, JVM отвергал неизвестные
                    // опции, и игра не запускалась.
                    if (stderrTask.IsCompletedSuccessfully)
                    {
                        output += stderrTask.Result;
                    }

                    var m = Regex.Match(output, @"version ""(?:1\.)?(\d+)");
                    if (m.Success && int.TryParse(m.Groups[1].Value, out int v))
                    {
                        return v;
                    }
                }
            }
            catch { }

            // Версию определить не удалось. Раньше здесь возвращалось 21, и это
            // была вторая половина той же поломки: неизвестная версия молча
            // становилась "современной" и включала самый агрессивный пресет на
            // JVM, которая его не поддерживает. 0 означает "не знаю" и даёт
            // консервативный G1GC с базовыми флагами.
            return 0;
        }

        private async Task<string> DownloadOpenJDKAsync(int version, string destinationFolder, Action<string>? statusCallback)
        {
            Directory.CreateDirectory(destinationFolder);

            // Adoptium отдаёт редирект на актуальную сборку, Zulu прибит к конкретной версии.
            // Порядок задаёт MirrorService: если api.adoptium.net из этой сети
            // недоступен, лаунчер не тратит пятиминутный таймаут, а идёт на CDN Azul.
            string adoptiumUrl =
                $"https://api.adoptium.net/v3/binary/latest/{version}/ga/windows/x64/jre/hotspot/normal/eclipse";

            string? azulUrl = version switch
            {
                8 => "https://cdn.azul.com/zulu/bin/zulu8.74.0.17-ca-jre8.0.392-win_x64.zip",
                17 => "https://cdn.azul.com/zulu/bin/zulu17.46.19-ca-jre17.0.9-win_x64.zip",
                21 => "https://cdn.azul.com/zulu/bin/zulu21.38.21-ca-jre21.0.5-win_x64.zip",
                // Раньше здесь стоял zulu21...zip, то есть для версии 25 устанавливалась
                // Java 21 в папку runtime\java-25. Пустой fallback: лучше явная ошибка,
                // чем запуск игры на несовместимой версии JVM.
                _ => null
            };

            string zipPath = Path.Combine(Path.GetTempPath(), $"java_{version}.zip");

            try
            {
                statusCallback?.Invoke(LocalizationService.Instance.Format("Str_Java_DownloadingOpenJdk", version));
                byte[]? fileBytes = null;

                // TLS-валидация включена. Этот архив распаковывается, и найденный
                // в нём javaw.exe потом запускается как процесс, поэтому подмена
                // сертификата здесь означала бы выполнение чужого кода от имени пользователя.
                byte[]? downloaded = await SecureHttp.TryDownloadBytesAsync(
                    MirrorService.Instance.BuildCandidates(adoptiumUrl, azulUrl),
                    TimeSpan.FromMinutes(5));

                if (downloaded != null)
                {
                    fileBytes = downloaded;
                }

                if (fileBytes == null || fileBytes.Length == 0)
                {
                    // Типизированное исключение: раньше здесь был безликий new Exception,
                    // поэтому сетевая ошибка, битый архив и пустой ответ были
                    // неразличимы для пользователя.
                    throw new IOException(
                        LocalizationService.Instance.Format("Str_Java_ArchiveFailed", version));
                }

                await File.WriteAllBytesAsync(zipPath, fileBytes);

                statusCallback?.Invoke(LocalizationService.Instance.Format("Str_Java_Unpacking", version));
                ZipFile.ExtractToDirectory(zipPath, destinationFolder, true);

                var javaws = Directory.GetFiles(destinationFolder, "javaw.exe", SearchOption.AllDirectories);
                if (javaws.Length > 0)
                {
                    return javaws[0];
                }

                throw new FileNotFoundException(LocalizationService.Instance.GetString("Str_Java_JavawMissing"));
            }
            finally
            {
                if (File.Exists(zipPath))
                {
                    try { File.Delete(zipPath); } catch { }
                }
            }
        }
    }
}
