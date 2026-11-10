using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MinecraftLauncher.Common;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Models;
using MinecraftLauncher.Services;

namespace MinecraftLauncher.ViewModels
{
    /// <summary>
    /// Страница мода: описание из каталога, последние файлы и установка.
    /// Установка считает контекст сборки так же, как браузер модов
    /// (выбранная сборка или общий GamePath), плюс явные фильтры страницы.
    /// </summary>
    public class ModDetailViewModel : ViewModelBase
    {
        private readonly ILocalizationService _loc = LocalizationService.Instance;

        private readonly ModItem _item;
        private readonly ModCategoryKind _kind;
        private readonly IModBrowserService _browser;
        private string _title = "";
        private string _author = "";
        private string _iconUrl = "";
        private string _sourceName = "";
        private string _downloadsText = "";
        private string _updatedText = "";
        private string _description = "";
        private string _loadingText = "";
        private bool _isLoading = true;

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string Author
        {
            get => _author;
            private set => SetProperty(ref _author, value);
        }

        public string IconUrl
        {
            get => _iconUrl;
            private set => SetProperty(ref _iconUrl, value);
        }

        public string SourceName
        {
            get => _sourceName;
            private set => SetProperty(ref _sourceName, value);
        }

        public string DownloadsText
        {
            get => _downloadsText;
            private set => SetProperty(ref _downloadsText, value);
        }

        public string UpdatedText
        {
            get => _updatedText;
            private set => SetProperty(ref _updatedText, value);
        }

        public string Description
        {
            get => _description;
            private set => SetProperty(ref _description, value);
        }

        public string LoadingText
        {
            get => _loadingText;
            set => SetProperty(ref _loadingText, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set => SetProperty(ref _isLoading, value);
        }

        public ObservableCollection<ModFileRow> Files { get; } = new();

        public AsyncRelayCommand OpenCatalogPageCommand { get; }
        public AsyncRelayCommand<ModFileRow> DownloadFileCommand { get; }

        /// <summary>
        /// Тип «живёт в папке» (мод, ресурспак...): тогда есть и установка,
        /// и скачивание файлов. Модпаки/миры/кастомизация — только просмотр.
        /// </summary>
        public bool CanInstall { get; }

        public AsyncRelayCommand InstallCommand { get; }

        private bool _isFavorite;

        /// <summary>Проект в «Избранном» лаунчера; кнопка ★ в шапке страницы.</summary>
        public bool IsFavorite
        {
            get => _isFavorite;
            private set
            {
                if (SetProperty(ref _isFavorite, value))
                {
                    OnPropertyChanged(nameof(FavoriteGlyph));
                }
            }
        }

        public string FavoriteGlyph => _isFavorite ? "\uE735" : "\uE734";

        public RelayCommand ToggleFavoriteCommand { get; }

        private string? _pageUrl;

        public ModDetailViewModel(ModItem item, ModCategoryKind kind)
            : this(item, kind, ModBrowserService.Instance)
        {
        }

        public ModDetailViewModel(ModItem item, ModCategoryKind kind, IModBrowserService browser)
        {
            _item = item;
            _kind = kind;
            _browser = browser;
            Title = item.Title;
            Author = item.Author;
            IconUrl = item.IconUrl;
            SourceName = item.Source == "curseforge"
                ? LocalizationService.Instance.GetString("Str_Mods_Source_CurseForge")
                : LocalizationService.Instance.GetString("Str_Mods_Source_Modrinth");
            LoadingText = _loc.GetString("Str_Detail_Loading");

            OpenCatalogPageCommand = new AsyncRelayCommand(ExecuteOpenPageAsync);
            DownloadFileCommand = new AsyncRelayCommand<ModFileRow>(ExecuteDownloadFileAsync);
            CanInstall = ModCategoryKinds.IsInstallable(kind);
            InstallCommand = new AsyncRelayCommand(ExecuteInstallAsync);
            ToggleFavoriteCommand = new RelayCommand(ExecuteToggleFavorite);
            IsFavorite = SettingsService.Instance.Settings.FavoriteMods.Any(f =>
                string.Equals(f.Source, item.Source, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(f.Id, item.Id, StringComparison.OrdinalIgnoreCase));

            _ = LoadAsync(browser);
        }

        private void ExecuteToggleFavorite()
        {
            var settings = SettingsService.Instance.Settings;
            var existing = settings.FavoriteMods.FirstOrDefault(f =>
                string.Equals(f.Source, _item.Source, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(f.Id, _item.Id, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                settings.FavoriteMods.Remove(existing);
                IsFavorite = false;
                // Карточка в списке браузера осталась с прежним флагом — обновим.
                _item.IsFavorite = false;
                ToastService.Instance.ShowInfo(
                    _loc.Format("Str_Fav_Removed", _item.Title),
                    _loc.GetString("Str_Mods_Favorites"));
            }
            else
            {
                settings.FavoriteMods.Add(new FavoriteMod
                {
                    Id = _item.Id,
                    Source = _item.Source,
                    Slug = _item.Slug,
                    Title = _item.Title,
                    Author = _item.Author,
                    IconUrl = _item.IconUrl,
                    ProjectType = _item.ProjectType
                });
                IsFavorite = true;
                _item.IsFavorite = true;
                ToastService.Instance.ShowSuccess(
                    _loc.Format("Str_Fav_Added", _item.Title),
                    _loc.GetString("Str_Mods_Favorites"));
            }

            SettingsService.Instance.Save();
        }

        private async Task LoadAsync(IModBrowserService browser)
        {
            try
            {
                var entry = new OnlineModEntry
                {
                    Source = _item.Source == "curseforge" ? ModSource.CurseForge : ModSource.Modrinth,
                    Id = _item.Id,
                    Slug = _item.Slug,
                    Title = _item.Title,
                    Author = _item.Author
                };

                var detail = await browser.GetProjectDetailsAsync(
                    entry.Source, _kind, entry, CancellationToken.None).ConfigureAwait(true);

                if (detail == null)
                {
                    Description = _loc.GetString("Str_Detail_DescriptionUnavailable");
                    return;
                }

                if (detail.Title.Length > 0) Title = detail.Title;
                if (detail.IconUrl.Length > 0) IconUrl = detail.IconUrl;
                if (detail.Author.Length > 0) Author = detail.Author;
                _pageUrl = detail.PageUrl;

                DownloadsText = _loc.Format("Str_Mods_Card_Downloads",
                    detail.Downloads.ToString("N0", CultureInfo.CurrentCulture));
                UpdatedText = detail.UpdatedAt == DateTimeOffset.MinValue
                    ? ""
                    : _loc.Format("Str_Mods_Card_Updated",
                        detail.UpdatedAt.LocalDateTime.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture));

                Description = detail.Description.Length > 0
                    ? detail.Description
                    : _item.Description;

                Files.Clear();
                foreach (var file in detail.Files.Take(10))
                {
                    Files.Add(new ModFileRow(file, _loc));
                }
            }
            catch (CurseForgeApiKeyException)
            {
                Description = _loc.GetString("Str_Mods_CfKeyNeed");
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("ModDetailViewModel", "Details load failed", ex);
                Description = _loc.Format("Str_Mods_SearchError", ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private Task ExecuteOpenPageAsync()
        {
            OpenUrl(!string.IsNullOrEmpty(_pageUrl)
                ? _pageUrl
                : _item.Source == "curseforge"
                    ? $"https://www.curseforge.com/minecraft/search?search={Uri.EscapeDataString(_item.Title)}"
                    : $"https://modrinth.com/mods?q={Uri.EscapeDataString(_item.Title)}");
            return Task.CompletedTask;
        }

        private async Task ExecuteDownloadFileAsync(ModFileRow? row)
        {
            if (row == null) return;

            string? installFolder = ModCategoryKinds.InstallFolder(_kind);
            if (installFolder == null)
            {
                ShowToast(_loc.GetString("Str_Mods_NoInstall"), error: true);
                return;
            }

            var (targetFolder, _, _) = ResolveInstallContext();

            IsLoading = true;
            LoadingText = _loc.Format("Str_Mods_SearchingTitle", row.DisplayName);

            try
            {
                string? savedName = await SaveFileAsync(row.File, targetFolder, installFolder).ConfigureAwait(true);
                if (savedName == null)
                {
                    ShowToast(_loc.GetString("Str_Mods_NothingSaved"), error: true);
                    return;
                }

                ShowToast(_loc.Format("Str_Mods_Installed", savedName, installFolder));
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("ModDetailViewModel", "File download failed", ex);
                ShowToast(_loc.Format("Str_Mods_DownloadError", ex.Message), error: true);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Установка как в браузере модов: подбираем файл под версию сборки и
        /// загрузчик ResolveFileAsync-ом, затем скачиваем и кладём в папку.
        /// </summary>
        private async Task ExecuteInstallAsync()
        {
            string? installFolder = ModCategoryKinds.InstallFolder(_kind);
            if (installFolder == null)
            {
                ShowToast(_loc.GetString("Str_Mods_NoInstall"), error: true);
                return;
            }

            var (targetFolder, gameVersion, loader) = ResolveInstallContext();
            var entry = new OnlineModEntry
            {
                Source = _item.Source == "curseforge" ? ModSource.CurseForge : ModSource.Modrinth,
                Id = _item.Id,
                Slug = _item.Slug,
                Title = _item.Title,
                Author = _item.Author
            };

            IsLoading = true;
            LoadingText = _loc.Format("Str_Mods_SearchingTitle", _item.Title);

            try
            {
                var file = await _browser.ResolveFileAsync(entry.Source, entry, gameVersion, loader).ConfigureAwait(true);
                if (file == null)
                {
                    ShowToast(gameVersion.Length > 0
                        ? _loc.Format("Str_Mods_NoCompatibleFile", gameVersion)
                        : _loc.GetString("Str_Mods_NoVersions"), error: true);
                    return;
                }

                LoadingText = _loc.Format("Str_Mods_DownloadTitle", file.FileName);

                string? savedName = await SaveFileAsync(file, targetFolder, installFolder).ConfigureAwait(true);
                if (savedName == null)
                {
                    ShowToast(_loc.GetString("Str_Mods_NothingSaved"), error: true);
                    return;
                }

                ShowToast(_loc.Format("Str_Mods_Installed", savedName, installFolder));
            }
            catch (CurseForgeApiKeyException)
            {
                ShowToast(_loc.GetString("Str_Mods_CfKeyNeed"), error: true);
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("ModDetailViewModel", "Install failed", ex);
                ShowToast(_loc.Format("Str_Mods_DownloadError", ex.Message), error: true);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Скачивает файл (с запасным URL и сверкой SHA-1) и пишет в папку установки.
        /// Возвращает итоговое имя файла, null — не получилось сохранить.
        /// </summary>
        private async Task<string?> SaveFileAsync(OnlineModFile file, string targetFolder, string installFolder)
        {
            string destDir = Path.Combine(targetFolder, installFolder);
            Directory.CreateDirectory(destDir);

            byte[]? bytes = await SecureHttp.TryDownloadBytesAsync(
                new[] { file.Url, file.FallbackUrl }, TimeSpan.FromMinutes(5)).ConfigureAwait(true);

            if (bytes == null || bytes.Length == 0) return null;

            if (!string.IsNullOrEmpty(file.Sha1) && !HashHelper.VerifySha1(bytes, file.Sha1))
            {
                ShowToast(_loc.Format("Str_Mods_HashMismatch", file.FileName), error: true);
                return null;
            }

            string targetPath = Path.Combine(destDir, file.FileName);
            await File.WriteAllBytesAsync(targetPath, bytes).ConfigureAwait(true);
            return file.FileName;
        }

        /// <summary>Единый тост страницы: заголовок — «Установка» или «Ошибка».</summary>
        private void ShowToast(string message, bool error = false)
        {
            ToastService.Instance.ShowInfo(message, error ? "Str_T_Error" : "Str_T_Install");
        }

        private static void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        /// <summary>
        /// Папка установки: корень выбранной сборки, иначе общий GamePath.
        /// </summary>
        private (string TargetFolder, string GameVersion, string? Loader) ResolveInstallContext()
        {
            var settings = SettingsService.Instance.Settings;
            string targetFolder = settings.GamePath;
            string gameVersion = "";
            string? loader = null;

            string selected = settings.LastSelectedVersion ?? "";
            if (selected.StartsWith("⭐"))
            {
                string packName = selected.Replace("⭐", "").Trim();
                if (packName.Contains(" ("))
                {
                    string loaderInTag = packName.Substring(packName.LastIndexOf(" (") + 2).TrimEnd(')');
                    packName = packName.Substring(0, packName.LastIndexOf(" (")).Trim();

                    string first = loaderInTag.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                    if (first.Length > 0) loader = first.ToLowerInvariant();
                }

                var pack = settings.Modpacks.Find(p => p.Name == packName);
                if (pack != null)
                {
                    if (Directory.Exists(pack.FolderPath)) targetFolder = pack.FolderPath;
                    if (!string.IsNullOrWhiteSpace(pack.GameVersion)) gameVersion = pack.GameVersion;
                    if (loader == null && !string.IsNullOrWhiteSpace(pack.Loader)) loader = pack.Loader.ToLowerInvariant();
                }
            }
            else if (selected.Length > 0)
            {
                gameVersion = selected;
            }

            return (targetFolder, gameVersion, loader);
        }
    }

    /// <summary>
    /// Готовая к показу строка файла: красивое имя версии и однострочная мета.
    /// </summary>
    public class ModFileRow : ViewModelBase
    {
        private string _meta;

        public OnlineModFile File { get; }
        public string DisplayName { get; }

        public string Meta
        {
            get => _meta;
            set => SetProperty(ref _meta, value);
        }

        public ModFileRow(OnlineModFile file, ILocalizationService loc)
        {
            File = file;
            DisplayName = file.DisplayName.Length > 0 ? file.DisplayName : file.FileName;

            string versions = file.GameVersions.Count > 0
                ? string.Join(", ", file.GameVersions.Take(4)) + (file.GameVersions.Count > 4 ? " …" : "")
                : "";

            var culture = CultureInfo.CurrentCulture;
            string sizeText = file.Size > 0
                ? (file.Size >= 1024 * 1024
                    ? string.Format(culture, "{0:0.#} MB", file.Size / (1024.0 * 1024))
                    : string.Format(culture, "{0:0} KB", file.Size / 1024.0))
                : "";

            string joined = string.Join(" · ", new[] { versions, sizeText }.Where(s => s.Length > 0));
            _meta = joined.Length > 0 ? joined : loc.GetString("Str_Mods_Card_MetaEmpty");
        }
    }
}
