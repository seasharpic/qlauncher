using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MinecraftLauncher.Common;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Models;
using MinecraftLauncher.Services;

namespace MinecraftLauncher.ViewModels
{
    /// <summary>
    /// Мод-браузер страницы «Моды».
    ///
    /// Что появилось по сравнению с первой версией (поиск только в Modrinth):
    ///  - два каталога: Modrinth (без ключа) и CurseForge (нужен личный ключ);
    ///  - фильтры: категория, версия Minecraft, загрузчик, сортировка;
    ///  - подбор файла под версию сборки: раньше качался первый попавшийся файл
    ///    проекта, и на другой версии Minecraft мод просто падал при загрузке игры;
    ///  - установка зависимостей и сверка SHA-1 скачанного файла.
    /// </summary>
    public class ModsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly IToastService _toastService;
        private readonly IMinecraftLaunchService _launchService;
        private readonly IModBrowserService _browser;

        // Локализация для строк, которые собираются в коде.
        private readonly ILocalizationService _loc = LocalizationService.Instance;

        // Отмена предыдущего поиска: при быстром вводе отсталые ответы
        // не сбрасывали свежие результаты.
        private CancellationTokenSource? _searchCts;

        private int _currentPage;
        private bool _hasMore;

        private string _searchQuery = "";
        private string _selectedCategory = "mod";
        private string _selectedSource = "modrinth";
        private string _selectedLoader = "";
        private string _selectedGameVersion = "";
        private string _selectedSort = "relevance";
        private string _selectedCategoryId = "";
        private string _categoryHint = LocalizationService.Instance.GetString("Str_Mods_HintMod");
        private bool _isLoading;
        private string _loadingText = LocalizationService.Instance.GetString("Str_Mods_EmptyState");
        private bool _areVersionsLoaded;
        private bool _isApiKeyPanelVisible;
        private string _curseForgeKeyInput = "";

        public ObservableCollection<ModItem> Mods { get; } = new();
        public ObservableCollection<ModCategory> Categories { get; } = new();
        public ObservableCollection<string> GameVersions { get; } = new();

        private bool _isFavoritesMode;

        /// <summary>Вкладка «Избранное»: список собирается из настроек, без сети.</summary>
        public bool IsFavoritesMode
        {
            get => _isFavoritesMode;
            private set
            {
                if (SetProperty(ref _isFavoritesMode, value))
                {
                    // Заполненная/пустая звезда на кнопке-переключателе.
                    OnPropertyChanged(nameof(ToggleFavoritesGlyph));
                }
            }
        }

        public string ToggleFavoritesGlyph => _isFavoritesMode ? "\uE735" : "\uE734";

        public static string[] LoaderOptions { get; } = { "", "fabric", "forge", "neoforge", "quilt" };
        public static string[] SortOptions { get; } = { "relevance", "downloads", "updated" };

        public string SearchQuery
        {
            get => _searchQuery;
            set => SetProperty(ref _searchQuery, value);
        }

        public string SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                if (SetProperty(ref _selectedCategory, value))
                {
                    EnforceSourceForKind(value);
                    UpdateCategoryHint();
                    _ = LoadCategoriesSafeAsync();
                }
            }
        }

        /// <summary>"modrinth" | "curseforge". Переключается комбобоксом на странице.</summary>
        public string SelectedSource
        {
            get => _selectedSource;
            set
            {
                if (SetProperty(ref _selectedSource, value) && IsLoading != true)
                {
                    // Категории у каталогов разные — перегружаем список при смене.
                    _ = LoadCategoriesSafeAsync();
                }
            }
        }

        /// <summary>"" — авто (по загрузчику активной сборки).</summary>
        public string SelectedLoader
        {
            get => _selectedLoader;
            set => SetProperty(ref _selectedLoader, value);
        }

        /// <summary>"" — авто (версия активной сборки).</summary>
        public string SelectedGameVersion
        {
            get => _selectedGameVersion;
            set => SetProperty(ref _selectedGameVersion, value);
        }

        public string SelectedSort
        {
            get => _selectedSort;
            set => SetProperty(ref _selectedSort, value);
        }

        /// <summary>Id категории выбранного каталога. "" — без фильтра.</summary>
        public string SelectedCategoryId
        {
            get => _selectedCategoryId;
            set => SetProperty(ref _selectedCategoryId, value);
        }

        public string CategoryHint
        {
            get => _categoryHint;
            set => SetProperty(ref _categoryHint, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string LoadingText
        {
            get => _loadingText;
            set => SetProperty(ref _loadingText, value);
        }

        /// <summary>Список версий Minecraft грузится в фоне; до него фильтр скрывается.</summary>
        public bool AreVersionsLoaded
        {
            get => _areVersionsLoaded;
            private set => SetProperty(ref _areVersionsLoaded, value);
        }

        /// <summary>Скрыто, когда CurseForge не выбран.</summary>
        public bool IsApiKeyPanelVisible
        {
            get => _isApiKeyPanelVisible;
            set => SetProperty(ref _isApiKeyPanelVisible, value);
        }

        /// <summary>Текстовое поле для вставки токена CurseForge.</summary>
        public string CurseForgeKeyInput
        {
            get => _curseForgeKeyInput;
            set => SetProperty(ref _curseForgeKeyInput, value);
        }

        /// <summary>Команда «Показать ещё»: подтягивает следующую страницу каталога.</summary>
        public bool HasMore
        {
            get => _hasMore;
            private set => SetProperty(ref _hasMore, value);
        }

        public AsyncRelayCommand SearchCommand { get; }
        public AsyncRelayCommand LoadMoreCommand { get; }
        public AsyncRelayCommand<ModItem> InstallModCommand { get; }
        public RelayCommand ToggleApiKeyPanelCommand { get; }
        public RelayCommand SaveApiKeyCommand { get; }
        public RelayCommand ToggleFavoritesModeCommand { get; }
        public RelayCommand<ModItem> ToggleFavoriteCommand { get; }

        public ModsViewModel()
            : this(SettingsService.Instance, ToastService.Instance, MinecraftLaunchService.Instance, ModBrowserService.Instance)
        {
        }

        public ModsViewModel(
            ISettingsService settingsService,
            IToastService toastService,
            IMinecraftLaunchService launchService,
            IModBrowserService browser)
        {
            _settingsService = settingsService;
            _toastService = toastService;
            _launchService = launchService;
            _browser = browser;

            SearchCommand = new AsyncRelayCommand(ExecuteSearchAsync);
            LoadMoreCommand = new AsyncRelayCommand(ExecuteLoadMoreAsync);
            InstallModCommand = new AsyncRelayCommand<ModItem>(ExecuteInstallModAsync);
            ToggleApiKeyPanelCommand = new RelayCommand(_ => IsApiKeyPanelVisible = !IsApiKeyPanelVisible);
            SaveApiKeyCommand = new RelayCommand(_ => ExecuteSaveApiKey());
            ToggleFavoritesModeCommand = new RelayCommand(_ => ToggleFavoritesMode());
            ToggleFavoriteCommand = new RelayCommand<ModItem>(ExecuteToggleFavorite);

            // Версии Minecraft подтягиваются один раз; при ошибке фильтр просто не появится,
            // поиск продолжает работать без него.
            _ = LoadGameVersionsSafeAsync();
            _ = LoadCategoriesSafeAsync();
        }

        /// <summary>
        /// Переключение вкладок «каталог» ↔ «избранное». Избранное собирается
        /// offline из настроек; назад — честный поиск с тем же запросом.
        /// </summary>
        private void ToggleFavoritesMode()
        {
            if (_isFavoritesMode)
            {
                IsFavoritesMode = false;
                _ = ExecuteSearchAsync();
                return;
            }

            IsFavoritesMode = true;
            ShowFavorites();
        }

        private void ShowFavorites()
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;
            _hasMore = false;

            var loc = LocalizationService.Instance;
            var favorites = _settingsService.Settings.FavoriteMods;

            Mods.Clear();
            foreach (var favorite in favorites)
            {
                var kind = ModCategoryKinds.FromKey(favorite.ProjectType);
                Mods.Add(new ModItem
                {
                    Id = favorite.Id,
                    Slug = favorite.Slug,
                    Title = string.IsNullOrWhiteSpace(favorite.Title)
                        ? loc.GetString("Str_Mods_UnknownTitle")
                        : favorite.Title,
                    Description = "",
                    Author = favorite.Author,
                    IconUrl = favorite.IconUrl,
                    Downloads = "",
                    UpdatedText = "",
                    ProjectType = favorite.ProjectType,
                    CanInstall = ModCategoryKinds.IsInstallable(kind),
                    Source = favorite.Source,
                    IsFavorite = true
                });
            }

            IsLoading = false;
            LoadingText = Mods.Count == 0
                ? loc.GetString("Str_Mods_NoFavorites")
                : "";
        }

        /// <summary>
        /// Кнопка ★ на карточке: добавляет/убирает проект из избранного
        /// (settings.json). На вкладке избранного карточка сразу исчезает.
        /// </summary>
        private void ExecuteToggleFavorite(ModItem? item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Id)) return;

            var settings = _settingsService.Settings;
            var existing = settings.FavoriteMods.FirstOrDefault(f =>
                string.Equals(f.Source, item.Source, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(f.Id, item.Id, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                settings.FavoriteMods.Remove(existing);
                item.IsFavorite = false;
                _toastService.ShowInfo(
                    _loc.Format("Str_Fav_Removed", item.Title),
                    _loc.GetString("Str_Mods_Favorites"));
            }
            else
            {
                settings.FavoriteMods.Add(new FavoriteMod
                {
                    Id = item.Id,
                    Source = item.Source,
                    Slug = item.Slug,
                    Title = item.Title,
                    Author = item.Author,
                    IconUrl = item.IconUrl,
                    ProjectType = item.ProjectType
                });
                item.IsFavorite = true;
                _toastService.ShowSuccess(
                    _loc.Format("Str_Fav_Added", item.Title),
                    _loc.GetString("Str_Mods_Favorites"));
            }

            _settingsService.Save();
            if (_isFavoritesMode && !item.IsFavorite)
            {
                Mods.Remove(item);
                if (Mods.Count == 0)
                {
                    LoadingText = _loc.GetString("Str_Mods_NoFavorites");
                }
            }
        }

        private ModSource CurrentSource =>
            string.Equals(_selectedSource, "curseforge", StringComparison.OrdinalIgnoreCase)
                ? ModSource.CurseForge
                : ModSource.Modrinth;

        private ModCategoryKind CurrentKind => ModCategoryKinds.FromKey(_selectedCategory);

        private void UpdateCategoryHint()
        {
            var loc = LocalizationService.Instance;
            CategoryHint = SelectedCategory switch
            {
                "resourcepack" => loc.GetString("Str_Mods_HintResourcePack"),
                "shader" => loc.GetString("Str_Mods_HintShader"),
                "datapack" => loc.GetString("Str_Mods_HintDataPack"),
                "plugin" => loc.GetString("Str_Mods_HintPlugin"),
                "modpack" => loc.GetString("Str_Mods_HintModpack"),
                "world" => loc.GetString("Str_Mods_HintWorld"),
                "customization" => loc.GetString("Str_Mods_HintCustomization"),
                "addons" => loc.GetString("Str_Mods_HintAddons"),
                _ => loc.GetString("Str_Mods_HintMod")
            };
        }

        /// <summary>
        /// Типы миров/кастомизации/аддонов есть только у CurseForge: Modrinth
        /// их не ищет, поэтому источник тихо переключаем на CurseForge.
        /// </summary>
        private string? EnforceSourceForKind(string kindKey)
        {
            var kind = ModCategoryKinds.FromKey(kindKey);

            if (!ModCategoryKinds.IsModrinthSupported(kind) &&
                string.Equals(_selectedSource, "modrinth", StringComparison.OrdinalIgnoreCase))
            {
                _selectedSource = "curseforge";
                OnPropertyChanged(nameof(SelectedSource));
                return "curseforge";
            }
            return null;
        }

        private async Task LoadGameVersionsSafeAsync()
        {
            try
            {
                var versions = await _browser.GetGameVersionsAsync().ConfigureAwait(true);
                if (versions.Count > 0)
                {
                    GameVersions.Clear();
                    foreach (string v in versions) GameVersions.Add(v);
                    AreVersionsLoaded = true;
                }
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("ModsViewModel", "Game versions load failed", ex);
            }
        }

        private async Task LoadCategoriesSafeAsync()
        {
            try
            {
                var categories = await _browser.GetCategoriesAsync(CurrentSource, CurrentKind).ConfigureAwait(true);

                Categories.Clear();
                // «Все категории» держим первым пунктом с пустым id,
                // чтобы сброс фильтра не требовал дефолтного значения ComboBox.
                Categories.Add(new ModCategory
                {
                    Id = "",
                    Name = _loc.GetString("Str_Mods_Category_All")
                });

                foreach (var category in categories) Categories.Add(category);
                SelectedCategoryId = "";
            }
            catch (CurseForgeApiKeyException)
            {
                // Категории CurseForge без ключа недоступны — список просто остаётся пустым.
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("ModsViewModel", "Categories load failed", ex);
            }
        }

        private ModSearchRequest BuildRequest(bool nextPage)
        {
            return new ModSearchRequest
            {
                Query = _searchQuery.Trim(),
                GameVersion = _selectedGameVersion.Length > 0 ? _selectedGameVersion : null,
                Loader = _selectedLoader.Length > 0 ? _selectedLoader : null,
                CategoryId = _selectedCategoryId.Length > 0 ? _selectedCategoryId : null,
                Sort = (ModSortMode)Array.IndexOf(SortOptions, _selectedSort),
                Page = nextPage ? _currentPage : 0,
                PageSize = 20
            };
        }

        private async Task ExecuteSearchAsync()
        {
            // Обычный поиск всегда работает в режиме каталога: пользователь
            // мог нажать «Поиск», находясь на вкладке избранного.
            if (_isFavoritesMode)
            {
                IsFavoritesMode = false;
            }

            if (CurrentSource == ModSource.CurseForge && !_browser.HasCurseForgeKey())
            {
                IsApiKeyPanelVisible = true;
                _toastService.ShowWarning(_loc.GetString("Str_Mods_CfKeyNeed"), _loc.GetString("Str_T_CurseForge"));
                return;
            }

            if (string.IsNullOrWhiteSpace(_searchQuery) && string.IsNullOrEmpty(_selectedCategoryId))
            {
                // Пустой запрос без категории = весь каталог; не блокируем —
                // просто ищем популярное, как это делают обычные браузеры модов.
            }

            _currentPage = 0;
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = new CancellationTokenSource();

            IsLoading = true;
            LoadingText = CurrentSource == ModSource.CurseForge
                ? _loc.GetString("Str_Mods_SearchingCf")
                : _loc.GetString("Str_Mods_Searching");
            Mods.Clear();

            try
            {
                var results = await _browser.SearchAsync(CurrentSource, CurrentKind, BuildRequest(nextPage: false), _searchCts.Token).ConfigureAwait(true);

                foreach (var entry in results) Mods.Add(ToViewItem(entry));

                // Страница почти пуста — каталог закончился, «Показать ещё» прячем.
                if (results.Count < 16)
                {
                    _hasMore = false;
                }
                else
                {
                    _hasMore = true;
                }

                if (results.Count == 0)
                {
                    LoadingText = _loc.GetString("Str_Mods_NoResults");
                }
            }
            catch (CurseForgeApiKeyException)
            {
                IsApiKeyPanelVisible = true;
                _toastService.ShowWarning(_loc.GetString("Str_Mods_CfKeyNeed"), _loc.GetString("Str_T_CurseForge"));
            }
            catch (Exception ex)
            {
                _toastService.ShowError(_loc.Format("Str_Mods_SearchError", ex.Message), _loc.GetString("Str_T_ModrinthApi"));
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task ExecuteLoadMoreAsync()
        {
            if (!_hasMore) return;
            if (CurrentSource == ModSource.CurseForge && !_browser.HasCurseForgeKey()) return;

            IsLoading = true;
            LoadingText = _loc.GetString("Str_Mods_LoadingMore");
            _currentPage++;

            try
            {
                var results = await _browser.SearchAsync(CurrentSource, CurrentKind, BuildRequest(nextPage: true), CancellationToken.None).ConfigureAwait(true);
                foreach (var entry in results) Mods.Add(ToViewItem(entry));
                _hasMore = results.Count >= 16;
            }
            catch (CurseForgeApiKeyException)
            {
                _hasMore = false;
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("ModsViewModel", "Load more failed", ex);
                _hasMore = false;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ExecuteSaveApiKey()
        {
            string key = _curseForgeKeyInput.Trim();
            var settings = _settingsService.Settings;

            if (key.Length == 0)
            {
                // Пустая вставка = просто закрыть панель, ничего не трогая.
                IsApiKeyPanelVisible = false;
                return;
            }

            settings.CurseForgeApiKey = key;
            _settingsService.Save();
            CurseForgeKeyInput = "";
            IsApiKeyPanelVisible = false;

            _toastService.ShowSuccess(_loc.GetString("Str_Mods_KeySaved"), _loc.GetString("Str_T_CurseForge"));

            // Перезагружаем категории CurseForge, раз ключ появился.
            _ = LoadCategoriesSafeAsync();
        }

        private ModItem ToViewItem(OnlineModEntry entry)
        {
            var kind = CurrentKind;
            var loc = LocalizationService.Instance;
            bool installable = ModCategoryKinds.IsInstallable(kind);
            var settings = _settingsService.Settings;

            var favorites = settings.FavoriteMods;

            bool isFavorite = favorites.Any(f =>
                string.Equals(f.Source, entry.Source == ModSource.CurseForge ? "curseforge" : "modrinth", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(f.Id, entry.Id, StringComparison.OrdinalIgnoreCase));

            return new ModItem
            {
                Id = entry.Id,
                Slug = entry.Slug,
                Title = string.IsNullOrWhiteSpace(entry.Title) ? loc.GetString("Str_Mods_UnknownTitle") : entry.Title,
                Description = entry.Description,
                Author = entry.Author,
                IconUrl = entry.IconUrl,
                Downloads = loc.Format("Str_Mods_Card_Downloads",
                    entry.Downloads.ToString("N0", CultureInfo.CurrentCulture)),
                UpdatedText = entry.UpdatedAt == DateTimeOffset.MinValue ? "" : loc.Format("Str_Mods_Card_Updated",
                    entry.UpdatedAt.LocalDateTime.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture)),
                ProjectType = _selectedCategory,
                CanInstall = installable,
                Source = entry.Source == ModSource.CurseForge ? "curseforge" : "modrinth",
                IsFavorite = isFavorite
            };
        }

        /// <summary>
        /// Папка установки: корень выбранной сборки, иначе общий GamePath.
        /// Выделено из строки поиска, потому что используется и для зависимостей.
        /// </summary>
        private (string TargetFolder, string GameVersion, string? Loader) ResolveInstallContext()
        {
            var settings = _settingsService.Settings;
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

                    // "+ Fabric" иногда пишется как "(Fabric Loader)" — берём первое слово.
                    string first = loaderInTag.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                    if (first.Length > 0) loader = NormalizeLoader(first);
                }

                var pack = settings.Modpacks.Find(p => p.Name == packName);
                if (pack != null)
                {
                    if (Directory.Exists(pack.FolderPath)) targetFolder = pack.FolderPath;
                    if (!string.IsNullOrWhiteSpace(pack.GameVersion)) gameVersion = pack.GameVersion;
                    if (loader == null && !string.IsNullOrWhiteSpace(pack.Loader)) loader = NormalizeLoader(pack.Loader);
                }
            }
            else if (selected.Length > 0)
            {
                // Обычная версия — просто имя версии Mojang ("1.21.5" и т.п.).
                gameVersion = selected;
            }

            // Явный выбор в фильтрах сильнее автоопределения.
            if (_selectedGameVersion.Length > 0) gameVersion = _selectedGameVersion;
            if (_selectedLoader.Length > 0) loader = _selectedLoader;

            return (targetFolder, gameVersion, loader);
        }

        private static string? NormalizeLoader(string raw)
        {
            string value = raw.Trim().ToLowerInvariant();
            return value switch
            {
                "fabric" or "fabricloader" or "fabric loader" => "fabric",
                "forge" => "forge",
                "neoforge" or "neo" => "neoforge",
                "quilt" => "quilt",
                _ => null
            };
        }

        private async Task ExecuteInstallModAsync(ModItem? item)
        {
            if (item == null) return;
            if (!Enum.TryParse(item.Source == "curseforge" ? "CurseForge" : "Modrinth", ignoreCase: true, out ModSource source)) source = ModSource.Modrinth;

            var (targetFolder, gameVersion, loader) = ResolveInstallContext();
            var kind = ModCategoryKinds.FromKey(item.ProjectType);
            string? installFolder = ModCategoryKinds.InstallFolder(kind);
            if (installFolder == null)
            {
                _toastService.ShowInfo(_loc.GetString("Str_Mods_NoInstall"), _loc.GetString("Str_T_Error"));
                return;
            }
            string destDir = Path.Combine(targetFolder, installFolder);

            IsLoading = true;
            LoadingText = _loc.Format("Str_Mods_SearchingTitle", item.Title);

            try
            {
                Directory.CreateDirectory(destDir);

                var file = await _browser.ResolveFileAsync(source, ToEntry(item), gameVersion, loader).ConfigureAwait(true);
                if (file == null)
                {
                    _toastService.ShowWarning(gameVersion.Length > 0
                        ? _loc.Format("Str_Mods_NoCompatibleFile", gameVersion)
                        : _loc.GetString("Str_Mods_NoVersions"), _loc.GetString("Str_T_Error"));
                    return;
                }

                int installed = 0;
                string? firstSavedName = await DownloadAndSaveAsync(file, destDir).ConfigureAwait(true);
                if (firstSavedName != null) installed++;

                // Зависимости: несколько файлов, каждый — тот же resolve/download/save.
                // Лимит защищает от кольца зависимостей и разорванной сети.
                var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { $"{item.Source}:{item.Id}" };
                var queue = new System.Collections.Generic.Queue<OnlineModDependency>(file.RequiredDependencies);

                while (queue.Count > 0 && installed < 25)
                {
                    var dep = queue.Dequeue();
                    string depKey = $"{item.Source}:{dep.Id}";
                    if (!processed.Add(depKey)) continue;

                    LoadingText = _loc.Format("Str_Mods_DownloadTitle", dep.Title);

                    var depFile = await _browser.ResolveDependencyFileAsync(source, dep.Id, gameVersion, loader).ConfigureAwait(true);
                    if (depFile == null)
                    {
                        CrashLogWriter.Write("ModsViewModel", $"Dependency file not found: {dep.Id}", null);
                        continue;
                    }

                    string? saved = await DownloadAndSaveAsync(depFile, destDir).ConfigureAwait(true);
                    if (saved != null) installed++;

                    foreach (var nested in depFile.RequiredDependencies) queue.Enqueue(nested);
                }

                string[] names = new string[] { firstSavedName ?? "" };
                if (installed == 0)
                {
                    _toastService.ShowError(_loc.GetString("Str_Mods_NothingSaved"), _loc.GetString("Str_T_Error"));
                    return;
                }

                if (installed == 1)
                {
                    _toastService.ShowSuccess(_loc.Format("Str_Mods_Installed", names[0], installFolder), _loc.GetString("Str_T_Install"));
                }
                else
                {
                    _toastService.ShowSuccess(_loc.Format("Str_Mods_InstalledWithDeps", names[0], installed - 1, installFolder), _loc.GetString("Str_T_Install"));
                }
            }
            catch (CurseForgeApiKeyException)
            {
                IsApiKeyPanelVisible = true;
                _toastService.ShowWarning(_loc.GetString("Str_Mods_CfKeyNeed"), _loc.GetString("Str_T_CurseForge"));
            }
            catch (Exception ex)
            {
                _toastService.ShowError(_loc.Format("Str_Mods_DownloadError", ex.Message), _loc.GetString("Str_T_Error"));
            }
            finally
            {
                IsLoading = false;
            }
        }

        private static OnlineModEntry ToEntry(ModItem item) => new()
        {
            Source = item.Source == "curseforge" ? ModSource.CurseForge : ModSource.Modrinth,
            Id = item.Id,
            Slug = item.Slug,
            Title = item.Title
        };

        /// <summary>
        /// Скачивает файл (с запасным URL и сверкой SHA-1) и пишет в папку.
        /// null означает, что сохранить не удалось — вызывающий код решает, что показывать.
        /// </summary>
        private async Task<string?> DownloadAndSaveAsync(OnlineModFile file, string destDir)
        {
            string targetPath = Path.Combine(destDir, file.FileName);
            if (File.Exists(targetPath))
            {
                // Уже стоит тот же файл — не качаем заново и зависимостей не трогаем.
                return file.FileName;
            }

            byte[]? bytes = await SecureHttp.TryDownloadBytesAsync(
                new[] { file.Url, file.FallbackUrl }, TimeSpan.FromMinutes(5)).ConfigureAwait(true);

            bytes ??= file.Alternative != null
                ? await SecureHttp.TryDownloadBytesAsync(
                    new[] { file.Alternative.Url, file.Alternative.FallbackUrl }, TimeSpan.FromMinutes(5)).ConfigureAwait(true)
                : null;

            if (bytes == null || bytes.Length == 0)
            {
                return null;
            }

            // Мод — исполняемый код. Хеш, который каталог отдал, обязан совпасть.
            if (!string.IsNullOrEmpty(file.Sha1) && !HashHelper.VerifySha1(bytes, file.Sha1))
            {
                _toastService.ShowError(_loc.Format("Str_Mods_HashMismatch", file.FileName), _loc.GetString("Str_T_Error"));
                return null;
            }

            await File.WriteAllBytesAsync(targetPath, bytes).ConfigureAwait(true);
            return file.FileName;
        }
    }
}
