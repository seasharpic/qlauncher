namespace MinecraftLauncher.Models
{
    /// <summary>
    /// Карточка мода в результатах поиска каталога (Modrinth / CurseForge).
    /// Заполняется из <see cref="Services.ModBrowserService"/> через ModsViewModel.
    /// </summary>
    public class ModItem
    {
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Author { get; set; } = "";
        public string IconUrl { get; set; } = "";
        /// <summary>Уже отформатированное число, например "162 733 997".</summary>
        public string Downloads { get; set; } = "";
        public string ProjectType { get; set; } = "mod";

        /// <summary>Внутренний id проекта в каталоге (Modrinth project_id или CurseForge modId).</summary>
        public string Id { get; set; } = "";

        /// <summary>Каталог, откуда получен результат. Используется при установке.</summary>
        public string Source { get; set; } = "modrinth";

        /// <summary>Отформатированная дата последнего обновления, например "15.10.2026".</summary>
        public string UpdatedText { get; set; } = "";

        /// <summary>
        /// Подготовленный ключ типа контента ("mod","modpack","world"...):
        /// определяет папку установки и наличие кнопки «Установить».
        /// </summary>
        public bool CanInstall { get; set; }
    }
}
