using System;

namespace MinecraftLauncher.Models
{
    /// <summary>
    /// Избранный проект каталога (Modrinth/CurseForge) в настройках лаунчера.
    /// Хранит только превращаемое в карточку: без сети список избранного
    /// открывается мгновенно, счётчики загрузок и «обновлено» не показываются.
    /// </summary>
    public class FavoriteMod
    {
        /// <summary>Внутренний id проекта в каталоге (Modrinth project_id / CurseForge modId).</summary>
        public string Id { get; set; } = "";

        /// <summary>"modrinth" | "curseforge".</summary>
        public string Source { get; set; } = "modrinth";

        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string Author { get; set; } = "";
        public string IconUrl { get; set; } = "";

        /// <summary>Ключ типа контента ("mod","resourcepack"...), как на карточке.</summary>
        public string ProjectType { get; set; } = "mod";

        /// <summary>Когда добавили — для будущей сортировки по дате.</summary>
        public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
