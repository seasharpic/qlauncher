using System;
using System.IO;
using MinecraftLauncher.Services;

namespace MinecraftLauncher.Helpers
{
    /// <summary>
    /// Проверка путей, приходящих извне (архивы модпаков, JSON-манифесты).
    ///
    /// Почему Path.GetFullPath недостаточно: Path.Combine(a, b) возвращает b как есть,
    /// если b — корневой путь. Поэтому modpack с полем "path": "C:/Windows/..." или
    /// "../../.ssh/..." в modrinth.index.json писал файлы куда угодно. Имя zip-записи
    /// .NET проверяет, но поле из JSON — нет, защита там не применяется.
    /// </summary>
    public static class SafePath
    {
        private static readonly ILocalizationService L = LocalizationService.Instance;
        /// <summary>
        /// Возвращает объединённый путь, гарантированно лежащий внутри baseDir.
        /// Бросает <see cref="IOException"/>, если путь выходит за пределы каталога.
        ///
        /// relativePath берётся из недоверенного источника, поэтому проверяется и
        /// абсолютность, и "../", и наличие вложенных ссылок на родительский каталог.
        /// </summary>
        public static string CombineWithin(string baseDir, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                throw new IOException(L.GetString("Str_Path_Empty"));
            }

            // Нормализуем разделители: в ZIP всегда "/", но встречаются и "\".
            string normalized = relativePath.Replace('\\', '/').Trim();

            if (normalized.Contains('\0'))
            {
                throw new IOException(L.GetString("Str_Path_InvalidChar"));
            }

            // Отсекаем абсолютные пути и UNC до работы с сегментами:
            // Path.Combine вернул бы "/etc/passwd" как есть.
            if (normalized.StartsWith("//", StringComparison.Ordinal)
                || Path.IsPathRooted(relativePath)
                || Path.IsPathRooted(normalized))
            {
                throw new IOException(L.Format("Str_Path_Absolute", relativePath));
            }

            // Разбираем по сегментам. ".." отклоняем явно, а не "очищаем":
            // молчаливое исправление пути маскировало бы попытку выхода из папки
            // и давало бы непредсказуемый результат импорта.
            string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var safeSegments = new List<string>(segments.Length);

            foreach (string segment in segments)
            {
                if (segment == ".")
                {
                    // Текущий каталог — просто пропускаем.
                    continue;
                }

                if (segment == "..")
                {
                    throw new IOException(L.Format("Str_Path_OutsideInstance", relativePath));
                }

                // Отсекаем хвост вида "name:stream" и пробелы в именах.
                safeSegments.Add(segment.TrimEnd(' ', '.'));
            }

            if (safeSegments.Count == 0 || safeSegments.Any(string.IsNullOrEmpty))
            {
                throw new IOException(L.Format("Str_Path_RootEscape", relativePath));
            }

            string root = Path.GetFullPath(baseDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            string combined = Path.GetFullPath(Path.Combine(
                root,
                string.Join(Path.DirectorySeparatorChar.ToString(), safeSegments)));

            // Контрольная проверка: итоговый путь обязан лежать внутри root.
            // StringComparison.OrdinalIgnoreCase — пути на Windows нечувствительны к регистру.
            if (!combined.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException(L.Format("Str_Path_OutsideInstance", relativePath));
            }

            return combined;
        }

        /// <summary>
        /// Мягкий вариант: возвращает null вместо исключения.
        /// Подходит для циклов, где один некорректный элемент не должен
        /// прерывать импорт остальных.
        /// </summary>
        public static string? TryCombineWithin(string baseDir, string relativePath)
        {
            try
            {
                return CombineWithin(baseDir, relativePath);
            }
            catch (IOException)
            {
                return null;
            }
        }
    }
}