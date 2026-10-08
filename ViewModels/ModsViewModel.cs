using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MinecraftLauncher.Common;
using MinecraftLauncher.Models;
using MinecraftLauncher.Services;

namespace MinecraftLauncher.ViewModels
{
    public class ModsViewModel : ViewModelBase
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private readonly ISettingsService _settingsService;
        private readonly IToastService _toastService;

        // Локализация для строк, которые собираются в коде.
        private readonly ILocalizationService _loc = LocalizationService.Instance;
        private readonly IMinecraftLaunchService _launchService;

        private string _searchQuery = "";
        private string _selectedCategory = "mod";
        private string _categoryHint = LocalizationService.Instance.GetString("Str_Mods_HintMod");
        private bool _isLoading;
        private string _loadingText = LocalizationService.Instance.GetString("Str_Mods_SearchingEllipsis");

        public ObservableCollection<ModItem> Mods { get; } = new();

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
                    UpdateCategoryHint();
                }
            }
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

        public AsyncRelayCommand SearchCommand { get; }
        public AsyncRelayCommand<ModItem> InstallModCommand { get; }

        static ModsViewModel()
        {
            HttpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "QLauncher_ModsCatalog/2.0");
        }

        public ModsViewModel() : this(SettingsService.Instance, ToastService.Instance, MinecraftLaunchService.Instance)
        {
        }

        public ModsViewModel(ISettingsService settingsService, IToastService toastService, IMinecraftLaunchService launchService)
        {
            _settingsService = settingsService;
            _toastService = toastService;
            _launchService = launchService;

            SearchCommand = new AsyncRelayCommand(ExecuteSearchAsync);
            InstallModCommand = new AsyncRelayCommand<ModItem>(ExecuteInstallModAsync);
        }

        private void UpdateCategoryHint()
        {
            CategoryHint = SelectedCategory switch
            {
                "resourcepack" => LocalizationService.Instance.GetString("Str_Mods_HintResourcePack"),
                "shader" => LocalizationService.Instance.GetString("Str_Mods_HintShader"),
                _ => LocalizationService.Instance.GetString("Str_Mods_HintMod")
            };
        }

        private async Task ExecuteSearchAsync()
        {
            string query = SearchQuery.Trim();
            if (string.IsNullOrEmpty(query)) return;

            IsLoading = true;
            LoadingText = LocalizationService.Instance.GetString("Str_Mods_Searching");
            Mods.Clear();

            try
            {
                string facets = Uri.EscapeDataString($"[[\"project_type:{SelectedCategory}\"]]");
                string url = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query)}&facets={facets}&limit=20";
                string json = await HttpClient.GetStringAsync(url);

                using var doc = JsonDocument.Parse(json);
                var hits = doc.RootElement.GetProperty("hits");

                foreach (var hit in hits.EnumerateArray())
                {
                    Mods.Add(new ModItem
                    {
                        Slug = hit.GetProperty("slug").GetString() ?? "",
                        Title = hit.GetProperty("title").GetString() ?? LocalizationService.Instance.GetString("Str_Mods_UnknownTitle"),
                        Description = hit.GetProperty("description").GetString() ?? "",
                        Author = hit.GetProperty("author").GetString() ?? LocalizationService.Instance.GetString("Str_Mods_UnknownAuthor"),
                        IconUrl = hit.GetProperty("icon_url").GetString() ?? "",
                        Downloads = hit.GetProperty("downloads").GetInt32().ToString("N0"),
                        ProjectType = SelectedCategory
                    });
                }
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

        private async Task ExecuteInstallModAsync(ModItem? item)
        {
            if (item == null) return;

            var settings = _settingsService.Settings;
            string targetFolder = settings.GamePath;

            string selectedVer = settings.LastSelectedVersion;
            if (!string.IsNullOrEmpty(selectedVer) && selectedVer.StartsWith("⭐"))
            {
                string packName = selectedVer.Replace("⭐", "").Trim();
                if (packName.Contains(" (")) packName = packName.Substring(0, packName.LastIndexOf(" (")).Trim();

                var pack = settings.Modpacks.Find(p => p.Name == packName);
                if (pack != null && Directory.Exists(pack.FolderPath))
                {
                    targetFolder = pack.FolderPath;
                }
            }

            string subFolder = item.ProjectType switch
            {
                "resourcepack" => "resourcepacks",
                "shader" => "shaderpacks",
                _ => "mods"
            };

            string destDir = Path.Combine(targetFolder, subFolder);
            Directory.CreateDirectory(destDir);

            IsLoading = true;
            LoadingText = LocalizationService.Instance.Format("Str_Mods_SearchingTitle", item.Title);

            try
            {
                string url = $"https://api.modrinth.com/v2/project/{item.Slug}/version";
                string json = await HttpClient.GetStringAsync(url);

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.GetArrayLength() == 0)
                {
                    _toastService.ShowWarning(_loc.GetString("Str_Mods_NoVersions"), _loc.GetString("Str_T_Modrinth"));
                    return;
                }

                var firstVersion = root[0];
                var files = firstVersion.GetProperty("files");
                if (files.GetArrayLength() == 0)
                {
                    _toastService.ShowWarning(_loc.GetString("Str_Mods_NoFiles"), _loc.GetString("Str_T_Modrinth"));
                    return;
                }

                string downloadUrl = files[0].GetProperty("url").GetString() ?? "";
                string fileName = files[0].GetProperty("filename").GetString() ?? $"{item.Slug}.jar";
                string filePath = Path.Combine(destDir, fileName);

                byte[] fileBytes = await HttpClient.GetByteArrayAsync(downloadUrl);
                await File.WriteAllBytesAsync(filePath, fileBytes);

                _toastService.ShowSuccess(_loc.Format("Str_Mods_Installed", fileName, subFolder), _loc.GetString("Str_T_Install"));
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
    }
}
