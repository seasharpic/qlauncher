using System;
using System.Collections.Generic;

namespace MinecraftLauncher.Models
{
    /// <summary>Онлайн-каталог, из которого берутся моды.</summary>
    public enum ModSource
    {
        Modrinth = 0,
        CurseForge = 1
    }

    /// <summary>Тип контента, который ищем (влияет на папку установки и фильтры API).</summary>
    public enum ModCategoryKind
    {
        Mod = 0,
        ResourcePack = 1,
        Shader = 2,
        DataPack = 3,
        Plugin = 4,
        Modpack = 5,
        /// <summary>Миры CurseForge: только просмотр, Modrinth их не держит.</summary>
        World = 6,
        /// <summary>Кастомизация CurseForge (лаки-блоки и т.п.): только просмотр.</summary>
        Customization = 7,
        /// <summary>Аддоны CurseForge (Bedrock-контент): только просмотр.</summary>
        Addons = 8
    }

    public static class ModCategoryKinds
    {
        /// <summary>Типы, которые есть в обоих каталогах (Modrinth держит их как project_type).</summary>
        public static readonly ModCategoryKind[] ModrinthSupported =
        {
            ModCategoryKind.Mod,
            ModCategoryKind.ResourcePack,
            ModCategoryKind.Shader,
            ModCategoryKind.DataPack,
            ModCategoryKind.Plugin,
            ModCategoryKind.Modpack
        };

        /// <summary>Типы, для которых установка в папку имеет смысл.</summary>
        public static readonly ModCategoryKind[] Installable =
        {
            ModCategoryKind.Mod,
            ModCategoryKind.ResourcePack,
            ModCategoryKind.Shader,
            ModCategoryKind.DataPack,
            ModCategoryKind.Plugin
        };

        public static bool IsModrinthSupported(ModCategoryKind kind) =>
            Array.IndexOf(ModrinthSupported, kind) >= 0;

        public static bool IsInstallable(ModCategoryKind kind) =>
            Array.IndexOf(Installable, kind) >= 0;

        /// <summary>Обратное преобразование ключа типа ("mod","shader"...) в kind.</summary>
        public static ModCategoryKind FromKey(string? key) => key switch
        {
            "resourcepack" => ModCategoryKind.ResourcePack,
            "shader" => ModCategoryKind.Shader,
            "datapack" => ModCategoryKind.DataPack,
            "plugin" => ModCategoryKind.Plugin,
            "modpack" => ModCategoryKind.Modpack,
            "world" => ModCategoryKind.World,
            "customization" => ModCategoryKind.Customization,
            "addons" => ModCategoryKind.Addons,
            _ => ModCategoryKind.Mod
        };

        /// <summary>
        /// Подпапка установки. null — для этого типа установка не предусмотрена.
        /// </summary>
        public static string? InstallFolder(ModCategoryKind kind) => kind switch
        {
            ModCategoryKind.Mod => "mods",
            ModCategoryKind.ResourcePack => "resourcepacks",
            ModCategoryKind.Shader => "shaderpacks",
            ModCategoryKind.DataPack => "datapacks",
            ModCategoryKind.Plugin => "plugins",
            _ => null
        };
    }

    /// <summary>Порядок сортировки результатов поиска.</summary>
    public enum ModSortMode
    {
        Relevance = 0,
        Downloads = 1,
        RecentlyUpdated = 2
    }

    /// <summary>
    /// Параметры поискового запроса. Все поля необязательные: пустая строка
    /// или null означают «без этого фильтра».
    /// </summary>
    public sealed class ModSearchRequest
    {
        public string Query = "";
        /// <summary>Версия Minecraft, например "1.21.5". null — без фильтра.</summary>
        public string? GameVersion;
        /// <summary>"fabric" | "forge" | "neoforge" | "quilt". null — без фильтра.</summary>
        public string? Loader;
        /// <summary>Идентификатор категории в формате конкретного каталога.</summary>
        public string? CategoryId;
        public ModSortMode Sort = ModSortMode.Relevance;
        /// <summary>Номер страницы начиная с 0.</summary>
        public int Page;
        public int PageSize = 20;
    }

    /// <summary>Категория каталога (например "Adventure and RPG" у CurseForge).</summary>
    public sealed class ModCategory
    {
        /// <summary>Строковый id, который передаётся обратно в <see cref="ModSearchRequest.CategoryId"/>.</summary>
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";

        /// <summary>
        /// Раньше WPF-шаблон ComboBox в некоторых состояниях рисовал объект «как есть»
        /// (получалось имя типа вместо названия категории) — поэтому отдаём имя строкой.
        /// </summary>
        public override string ToString() => Name;
    }

    /// <summary>Карточка мода в результатах поиска.</summary>
    public sealed class OnlineModEntry
    {
        public ModSource Source { get; set; }
        /// <summary>Внутренний id проекта (у Modrinth — project_id, у CurseForge — числовой id).</summary>
        public string Id { get; set; } = "";
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string Author { get; set; } = "";
        public string Description { get; set; } = "";
        public string IconUrl { get; set; } = "";
        public long Downloads { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    /// <summary>Зависимость мода из метаданных файла.</summary>
    public sealed class OnlineModDependency
    {
        public ModSource Source { get; set; }
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public bool Required { get; set; }
    }

    /// <summary>Конкретный файл мода, выбранный под версию и загрузчик.</summary>
    public sealed class OnlineModFile
    {
        public string Url { get; set; } = "";
        /// <summary>Резервная ссылка (CDN CurseForge), если основная отдаёт 403/404.</summary>
        public string FallbackUrl { get; set; } = "";
        public string FileName { get; set; } = "";

        /// <summary>Красивое имя версии, если каталог его отдал (иначе FileName).</summary>
        public string DisplayName { get; set; } = "";
        /// <summary>Версии Minecraft, под которые католог пометил этот файл.</summary>
        public List<string> GameVersions { get; } = new List<string>();

        public string Sha1 { get; set; } = "";
        public long Size { get; set; }
        /// <summary>Резервный вариант файла под ту же сборку, если основной не скачался.</summary>
        public OnlineModFile? Alternative { get; set; }
        public List<OnlineModDependency> RequiredDependencies { get; } = new List<OnlineModDependency>();
    }

    /// <summary>
    /// Подробные сведения о проекте для страницы мода: блока с описанием,
    /// ссылкой на каталог и списком последних файлов.
    /// </summary>
    public sealed class OnlineModDetail
    {
        public ModSource Source { get; set; }
        public string Id { get; set; } = "";
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string Author { get; set; } = "";
        public string IconUrl { get; set; } = "";
        public long Downloads { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        /// <summary>Прямая ссылка на страницу проекта в каталоге.</summary>
        public string PageUrl { get; set; } = "";

        /// <summary>Описание без HTML-разметки (готово к показу текстом).</summary>
        public string Description { get; set; } = "";

        /// <summary>Последние файлы/версии для прямого скачивания.</summary>
        public List<OnlineModFile> Files { get; } = new List<OnlineModFile>();
    }
}
