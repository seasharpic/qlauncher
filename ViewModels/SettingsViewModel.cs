using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CmlLib.Core.Auth.Microsoft;
using Microsoft.Win32;
using MinecraftLauncher.Common;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Models;
using MinecraftLauncher.Services;

namespace MinecraftLauncher.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly IToastService _toastService;
        private readonly IThemeService _themeService;

        private bool _closeOnLaunch;
        private bool _enableDiscordRpc;
        private bool _enableUiSounds;
        private bool _autoAddServers;
        private bool _hideServerIp;
        private int _ramMb;
        private string _javaPath = "";
        private string _jvmPreset = "default";
        private string _customJvmArgs = "";
        private bool _isFullScreen;
        private int _screenWidth;
        private int _screenHeight;
        private string _customWallpaperPath = "";
        private string _cacheInfoText = "";
        private string _offlineNicknameInput = "";
        private string _gpuPreference = "HighPerformance";
        private GpuOptionItem? _selectedGpuOption;
        private string _activeGpuSummary = "";
        private string _activeGpuDetails = "";
        private bool _checkUpdatesOnStartup = true;
        private bool _showSplashOnStartup = true;
        private string _updateChannel = "stable";
        private string _updateMirror = "auto";
        private int _bandwidthLimitMbps = 0;
        private bool _isCheckingUpdates = false;

        public ObservableCollection<AccountProfile> Accounts { get; } = new();
        public ObservableCollection<GpuOptionItem> GpuOptions { get; } = new();

        public bool CloseOnLaunch
        {
            get => _closeOnLaunch;
            set
            {
                if (SetProperty(ref _closeOnLaunch, value)) SaveSettings();
            }
        }

        public bool EnableDiscordRpc
        {
            get => _enableDiscordRpc;
            set
            {
                if (SetProperty(ref _enableDiscordRpc, value)) SaveSettings();
            }
        }

        public bool EnableUiSounds
        {
            get => _enableUiSounds;
            set
            {
                if (SetProperty(ref _enableUiSounds, value)) SaveSettings();
            }
        }

        public bool AutoAddServers
        {
            get => _autoAddServers;
            set
            {
                if (SetProperty(ref _autoAddServers, value)) SaveSettings();
            }
        }

        public bool HideServerIp
        {
            get => _hideServerIp;
            set
            {
                if (SetProperty(ref _hideServerIp, value)) SaveSettings();
            }
        }

        public int RamMb
        {
            get => _ramMb;
            set
            {
                if (SetProperty(ref _ramMb, value)) SaveSettings();
            }
        }

        public string JavaPath
        {
            get => _javaPath;
            set
            {
                if (SetProperty(ref _javaPath, value)) SaveSettings();
            }
        }

        public string JvmPreset
        {
            get => _jvmPreset;
            set
            {
                if (SetProperty(ref _jvmPreset, value)) SaveSettings();
            }
        }

        public string CustomJvmArgs
        {
            get => _customJvmArgs;
            set
            {
                if (SetProperty(ref _customJvmArgs, value)) SaveSettings();
            }
        }

        public bool IsFullScreen
        {
            get => _isFullScreen;
            set
            {
                if (SetProperty(ref _isFullScreen, value)) SaveSettings();
            }
        }

        public int ScreenWidth
        {
            get => _screenWidth;
            set
            {
                if (SetProperty(ref _screenWidth, value)) SaveSettings();
            }
        }

        public int ScreenHeight
        {
            get => _screenHeight;
            set
            {
                if (SetProperty(ref _screenHeight, value)) SaveSettings();
            }
        }

        public string CustomWallpaperPath
        {
            get => _customWallpaperPath;
            set
            {
                if (SetProperty(ref _customWallpaperPath, value)) SaveSettings();
            }
        }

        public string CacheInfoText
        {
            get => _cacheInfoText;
            set => SetProperty(ref _cacheInfoText, value);
        }

        public string OfflineNicknameInput
        {
            get => _offlineNicknameInput;
            set => SetProperty(ref _offlineNicknameInput, value);
        }

        public GpuOptionItem? SelectedGpuOption
        {
            get => _selectedGpuOption;
            set
            {
                if (SetProperty(ref _selectedGpuOption, value) && value != null)
                {
                    _gpuPreference = value.Id;
                    UpdateGpuStatusDisplay();
                    SaveSettings();
                }
            }
        }

        public string ActiveGpuSummary
        {
            get => _activeGpuSummary;
            set => SetProperty(ref _activeGpuSummary, value);
        }

        public string ActiveGpuDetails
        {
            get => _activeGpuDetails;
            set => SetProperty(ref _activeGpuDetails, value);
        }

        public bool IsDarkTheme
        {
            get => _themeService.IsDarkTheme;
            set
            {
                if (value && !_themeService.IsDarkTheme)
                {
                    _themeService.SetTheme(true);
                    OnPropertyChanged(nameof(IsDarkTheme));
                    OnPropertyChanged(nameof(IsLightTheme));
                }
            }
        }

        public bool IsLightTheme
        {
            get => !_themeService.IsDarkTheme;
            set
            {
                if (value && _themeService.IsDarkTheme)
                {
                    _themeService.SetTheme(false);
                    OnPropertyChanged(nameof(IsDarkTheme));
                    OnPropertyChanged(nameof(IsLightTheme));
                }
            }
        }

        public bool CheckUpdatesOnStartup
        {
            get => _checkUpdatesOnStartup;
            set
            {
                if (SetProperty(ref _checkUpdatesOnStartup, value)) SaveSettings();
            }
        }

        public bool ShowSplashOnStartup
        {
            get => _showSplashOnStartup;
            set
            {
                if (SetProperty(ref _showSplashOnStartup, value)) SaveSettings();
            }
        }

        public string UpdateChannel
        {
            get => _updateChannel;
            set
            {
                if (SetProperty(ref _updateChannel, value)) SaveSettings();
            }
        }

        public string UpdateMirror
        {
            get => _updateMirror;
            set
            {
                if (SetProperty(ref _updateMirror, value)) SaveSettings();
            }
        }

        public int BandwidthLimitMbps
        {
            get => _bandwidthLimitMbps;
            set
            {
                if (SetProperty(ref _bandwidthLimitMbps, value))
                {
                    OnPropertyChanged(nameof(BandwidthLimitTag));
                    SaveSettings();
                }
            }
        }

        public string BandwidthLimitTag
        {
            get => _bandwidthLimitMbps.ToString();
            set
            {
                if (int.TryParse(value, out int parsed) && parsed != _bandwidthLimitMbps)
                {
                    BandwidthLimitMbps = parsed;
                }
            }
        }

        public bool IsCheckingUpdates
        {
            get => _isCheckingUpdates;
            set => SetProperty(ref _isCheckingUpdates, value);
        }

        public bool IsPortableMode => LauncherPathHelper.IsPortableMode;

        public string PortableModeStatusText => IsPortableMode
            ? (IsRussianLanguage ? "Портативный режим активен (хранилище ./data)" : "Portable mode is active (storage ./data)")
            : (IsRussianLanguage ? "Стандартный режим (хранилище %APPDATA%/.qlauncher)" : "Standard mode (storage %APPDATA%/.qlauncher)");

        public string CurrentVersionText => $"{UpdateService.CurrentVersion}{(IsPortableMode ? " (Портативный режим)" : "")}";

        public string CurrentLanguage => LocalizationService.Instance.CurrentLanguage;
        public bool IsRussianLanguage => CurrentLanguage == "ru";
        public bool IsEnglishLanguage => CurrentLanguage == "en";
        public string DataDirectoryPath => LauncherPathHelper.GetDefaultDataDirectory();

        public RelayCommand BrowseJavaCommand { get; }
        public RelayCommand ResetJavaCommand { get; }
        public RelayCommand BrowseWallpaperCommand { get; }
        public RelayCommand ResetWallpaperCommand { get; }
        public RelayCommand CleanCacheCommand { get; }
        public RelayCommand AddOfflineAccountCommand { get; }
        public AsyncRelayCommand LoginMicrosoftCommand { get; }
        public RelayCommand<AccountProfile> DeleteAccountCommand { get; }
        public RelayCommand<string> SelectAccentPresetCommand { get; }
        public RelayCommand<string> SelectThemeCommand { get; }
        public RelayCommand RefreshGpuCommand { get; }
        public RelayCommand AutoTuneHardwareCommand { get; }
        public AsyncRelayCommand ExportPortableCommand { get; }
        public RelayCommand OpenDataFolderCommand { get; }
        public RelayCommand<string> ChangeLanguageCommand { get; }
        public AsyncRelayCommand CheckUpdatesManualCommand { get; }
        public AsyncRelayCommand ExportDiagnosticReportCommand { get; }

        public SettingsViewModel() : this(SettingsService.Instance, ToastService.Instance, ThemeService.Instance)
        {
        }

        public SettingsViewModel(ISettingsService settingsService, IToastService toastService, IThemeService themeService)
        {
            _settingsService = settingsService;
            _toastService = toastService;
            _themeService = themeService;

            BrowseJavaCommand = new RelayCommand(ExecuteBrowseJava);
            ResetJavaCommand = new RelayCommand(() => JavaPath = "");
            BrowseWallpaperCommand = new RelayCommand(ExecuteBrowseWallpaper);
            ResetWallpaperCommand = new RelayCommand(() => CustomWallpaperPath = "");
            CleanCacheCommand = new RelayCommand(ExecuteCleanCache);
            AddOfflineAccountCommand = new RelayCommand(ExecuteAddOfflineAccount);
            LoginMicrosoftCommand = new AsyncRelayCommand(ExecuteLoginMicrosoftAsync);
            DeleteAccountCommand = new RelayCommand<AccountProfile>(ExecuteDeleteAccount);
            SelectAccentPresetCommand = new RelayCommand<string>(ExecuteSelectAccentPreset);
            SelectThemeCommand = new RelayCommand<string>(mode =>
            {
                if (string.Equals(mode, "dark", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_themeService.IsDarkTheme)
                    {
                        _themeService.SetTheme(true);
                        OnPropertyChanged(nameof(IsDarkTheme));
                        OnPropertyChanged(nameof(IsLightTheme));
                    }
                }
                else if (string.Equals(mode, "light", StringComparison.OrdinalIgnoreCase))
                {
                    if (_themeService.IsDarkTheme)
                    {
                        _themeService.SetTheme(false);
                        OnPropertyChanged(nameof(IsDarkTheme));
                        OnPropertyChanged(nameof(IsLightTheme));
                    }
                }
            });

            RefreshGpuCommand = new RelayCommand(() =>
            {
                PopulateGpuOptions();
                _toastService.ShowSuccess("Список видеокарт обновлен");
            });

            AutoTuneHardwareCommand = new RelayCommand(ExecuteAutoTuneHardware);
            ExportPortableCommand = new AsyncRelayCommand(ExecuteExportPortableAsync);
            OpenDataFolderCommand = new RelayCommand(ExecuteOpenDataFolder);
            ChangeLanguageCommand = new RelayCommand<string>(ExecuteChangeLanguage);

            CheckUpdatesManualCommand = new AsyncRelayCommand(ExecuteCheckUpdatesManualAsync);
            ExportDiagnosticReportCommand = new AsyncRelayCommand(ExecuteExportDiagnosticReportAsync);

            _themeService.ThemeChanged += OnThemeChanged;

            LoadFromSettings();
        }

        private void OnThemeChanged()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                OnPropertyChanged(nameof(IsDarkTheme));
                OnPropertyChanged(nameof(IsLightTheme));
            });
        }

        public void Cleanup()
        {
            _themeService.ThemeChanged -= OnThemeChanged;
        }

        public void LoadFromSettings()
        {
            var s = _settingsService.Load();

            _closeOnLaunch = s.CloseOnLaunch;
            _enableDiscordRpc = s.EnableDiscordRpc;
            _enableUiSounds = s.EnableUiSounds;
            _autoAddServers = s.AutoAddServers;
            _hideServerIp = s.HideServerIp;
            _ramMb = s.RamMb;
            _javaPath = s.JavaPath;
            _jvmPreset = s.JvmPreset;
            _customJvmArgs = s.CustomJvmArgs;
            _isFullScreen = s.IsFullScreen;
            _screenWidth = s.ScreenWidth;
            _screenHeight = s.ScreenHeight;
            _customWallpaperPath = s.CustomWallpaperPath;
            _gpuPreference = string.IsNullOrWhiteSpace(s.GpuPreference) ? "HighPerformance" : s.GpuPreference;
            _checkUpdatesOnStartup = s.CheckUpdatesOnStartup;
            _showSplashOnStartup = s.ShowSplashOnStartup;
            _updateChannel = string.IsNullOrWhiteSpace(s.UpdateChannel) ? "stable" : s.UpdateChannel;
            _updateMirror = string.IsNullOrWhiteSpace(s.UpdateMirror) ? "auto" : s.UpdateMirror;
            _bandwidthLimitMbps = s.BandwidthLimitMbps;

            OnPropertyChanged(string.Empty);

            PopulateGpuOptions();
            UpdateCacheInfo();

            Accounts.Clear();
            foreach (var acc in s.Accounts)
            {
                acc.AvatarImage = AvatarHelper.GetDefaultAvatar();
                Accounts.Add(acc);

                _ = Task.Run(async () =>
                {
                    var img = await AvatarHelper.GetAvatarAsync(acc.Nickname, acc.Uuid);
                    if (img != null)
                    {
                        Application.Current.Dispatcher.Invoke(() => acc.AvatarImage = img);
                    }
                });
            }
        }

        private void SaveSettings()
        {
            var s = _settingsService.Settings;
            s.CloseOnLaunch = CloseOnLaunch;
            if (s.EnableDiscordRpc != EnableDiscordRpc)
            {
                s.EnableDiscordRpc = EnableDiscordRpc;
                if (EnableDiscordRpc)
                {
                    DiscordService.Instance.StartRpc(true);
                }
                else
                {
                    DiscordService.Instance.StopRpc();
                }
            }
            s.EnableUiSounds = EnableUiSounds;
            s.AutoAddServers = AutoAddServers;
            s.HideServerIp = HideServerIp;
            s.RamMb = RamMb;
            s.JavaPath = JavaPath;
            s.JvmPreset = JvmPreset;
            s.CustomJvmArgs = CustomJvmArgs;
            s.IsFullScreen = IsFullScreen;
            s.ScreenWidth = ScreenWidth;
            s.ScreenHeight = ScreenHeight;
            s.CustomWallpaperPath = CustomWallpaperPath;
            s.GpuPreference = _gpuPreference;
            s.CheckUpdatesOnStartup = CheckUpdatesOnStartup;
            s.ShowSplashOnStartup = ShowSplashOnStartup;
            s.UpdateChannel = UpdateChannel;
            s.UpdateMirror = UpdateMirror;
            s.BandwidthLimitMbps = BandwidthLimitMbps;
            _settingsService.Save(s);
        }

        public void PopulateGpuOptions()
        {
            GpuOptions.Clear();

            GpuOptions.Add(new GpuOptionItem
            {
                Id = "HighPerformance",
                Title = "Высокая производительность (Дискретная)",
                Subtitle = "Максимальный FPS на производительном GPU"
            });

            GpuOptions.Add(new GpuOptionItem
            {
                Id = "PowerSaving",
                Title = "Энергосбережение (Встроенная)",
                Subtitle = "Минимальный нагрев и энергопотребление"
            });

            GpuOptions.Add(new GpuOptionItem
            {
                Id = "Default",
                Title = "По умолчанию Windows (Автовыбор)",
                Subtitle = "Автоматический выбор системы"
            });

            var detectedGpus = GpuService.Instance.GetAvailableGpus();
            foreach (var gpu in detectedGpus)
            {
                GpuOptions.Add(new GpuOptionItem
                {
                    Id = gpu.Name,
                    Title = gpu.DisplayName,
                    Subtitle = $"Драйвер {gpu.DriverVersion} | {(gpu.IsDiscrete ? "Дискретная" : "Встроенная")}"
                });
            }

            var selected = GpuOptions.FirstOrDefault(g => string.Equals(g.Id, _gpuPreference, StringComparison.OrdinalIgnoreCase))
                           ?? GpuOptions.FirstOrDefault();

            _selectedGpuOption = selected;
            OnPropertyChanged(nameof(SelectedGpuOption));
            UpdateGpuStatusDisplay();
        }

        private void UpdateGpuStatusDisplay()
        {
            var detectedGpus = GpuService.Instance.GetAvailableGpus();
            var discreteGpu = detectedGpus.FirstOrDefault(g => g.IsDiscrete);
            var primaryGpu = detectedGpus.FirstOrDefault();

            if (string.Equals(_gpuPreference, "HighPerformance", StringComparison.OrdinalIgnoreCase))
            {
                ActiveGpuSummary = discreteGpu != null
                    ? $"Высокая производительность: {discreteGpu.DisplayName}"
                    : "Режим: Высокая производительность";
                ActiveGpuDetails = "Windows и видеодрайвер направят процесс игры на дискретный графический чип.";
            }
            else if (string.Equals(_gpuPreference, "PowerSaving", StringComparison.OrdinalIgnoreCase))
            {
                var integratedGpu = detectedGpus.FirstOrDefault(g => !g.IsDiscrete);
                ActiveGpuSummary = integratedGpu != null
                    ? $"Энергосбережение: {integratedGpu.DisplayName}"
                    : "Режим: Энергосбережение";
                ActiveGpuDetails = "Используется встроенный видеоадаптер для экономии батареи.";
            }
            else if (string.Equals(_gpuPreference, "Default", StringComparison.OrdinalIgnoreCase))
            {
                ActiveGpuSummary = primaryGpu != null
                    ? $"Автовыбор Windows ({primaryGpu.Name})"
                    : "Режим: По умолчанию Windows";
                ActiveGpuDetails = "Операционная система сама определяет используемый видеоадаптер.";
            }
            else
            {
                var specificGpu = detectedGpus.FirstOrDefault(g => string.Equals(g.Name, _gpuPreference, StringComparison.OrdinalIgnoreCase));
                if (specificGpu != null)
                {
                    ActiveGpuSummary = $"Выбран адаптер: {specificGpu.DisplayName}";
                    ActiveGpuDetails = $"Драйвер: {specificGpu.DriverVersion} | Видеопамять: {specificGpu.VramFormatted}";
                }
                else
                {
                    ActiveGpuSummary = $"Выбран адаптер: {_gpuPreference}";
                    ActiveGpuDetails = "Указан индивидуальный видеоадаптер.";
                }
            }
        }

        private void UpdateCacheInfo()
        {
            long bytes = CacheCleanerHelper.CalculateCacheSize(_settingsService.Settings.GamePath);
            double mb = bytes / (1024.0 * 1024.0);
            CacheInfoText = $"Размер кэша и логов: {mb:F1} МБ";
        }

        private void ExecuteBrowseJava()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Java Executable (javaw.exe;java.exe)|javaw.exe;java.exe|All files (*.*)|*.*",
                Title = "Выберите исполняемый файл Java"
            };

            if (dlg.ShowDialog() == true)
            {
                JavaPath = dlg.FileName;
            }
        }

        private void ExecuteBrowseWallpaper()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Изображения (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                Title = "Выберите фоновое изображение"
            };

            if (dlg.ShowDialog() == true)
            {
                CustomWallpaperPath = dlg.FileName;
            }
        }

        private void ExecuteCleanCache()
        {
            double freedMb = CacheCleanerHelper.CleanCache(_settingsService.Settings.GamePath);
            UpdateCacheInfo();
            _toastService.ShowSuccess($"Освобождено {freedMb:F1} МБ дискового пространства.", "Очистка кэша");
        }

        private void ExecuteAddOfflineAccount()
        {
            string nick = OfflineNicknameInput.Trim();
            if (string.IsNullOrWhiteSpace(nick))
            {
                _toastService.ShowWarning("Введите никнейм для создания оффлайн-аккаунта.", "Аккаунт");
                return;
            }

            var s = _settingsService.Settings;
            if (s.Accounts.Any(a => string.Equals(a.Nickname, nick, StringComparison.OrdinalIgnoreCase)))
            {
                _toastService.ShowWarning("Аккаунт с таким никнеймом уже добавлен.", "Аккаунт");
                return;
            }

            var newAcc = new AccountProfile
            {
                Nickname = nick,
                AccessToken = "offline",
                Uuid = Guid.NewGuid().ToString()
            };

            s.Accounts.Add(newAcc);
            s.ActiveAccount = nick;
            _settingsService.Save(s);

            LoadFromSettings();
            OfflineNicknameInput = "";
            _toastService.ShowSuccess($"Аккаунт '{nick}' успешно добавлен!", "Аккаунт");
        }

        private async Task ExecuteLoginMicrosoftAsync()
        {
            try
            {
                _toastService.ShowInfo("Открывается окно авторизации Microsoft...", "Вход Microsoft");

                var loginHandler = JELoginHandlerBuilder.BuildDefault();
                var session = await loginHandler.AuthenticateInteractively();

                if (session != null && !string.IsNullOrEmpty(session.Username))
                {
                    var s = _settingsService.Settings;
                    var existing = s.Accounts.FirstOrDefault(a => a.Nickname == session.Username);

                    if (existing != null)
                    {
                        existing.AccessToken = session.AccessToken ?? "";
                        existing.Uuid = session.UUID ?? "";
                    }
                    else
                    {
                        s.Accounts.Add(new AccountProfile
                        {
                            Nickname = session.Username,
                            AccessToken = session.AccessToken ?? "",
                            Uuid = session.UUID ?? ""
                        });
                    }

                    s.ActiveAccount = session.Username;
                    _settingsService.Save(s);
                    LoadFromSettings();

                    _toastService.ShowSuccess($"Вы вошли как {session.Username}!", "Microsoft Login");
                }
            }
            catch (Exception ex)
            {
                _toastService.ShowError($"Ошибка входа Microsoft: {ex.Message}", "Ошибка");
            }
        }

        private void ExecuteDeleteAccount(AccountProfile? account)
        {
            if (account == null) return;

            var s = _settingsService.Settings;
            var toRemove = s.Accounts.FirstOrDefault(a => a.Nickname == account.Nickname);
            if (toRemove != null)
            {
                s.Accounts.Remove(toRemove);
                if (s.ActiveAccount == account.Nickname)
                {
                    s.ActiveAccount = s.Accounts.FirstOrDefault()?.Nickname ?? "";
                }
                _settingsService.Save(s);
                LoadFromSettings();
                _toastService.ShowInfo($"Аккаунт '{account.Nickname}' удален.", "Аккаунты");
            }
        }

        private void ExecuteSelectAccentPreset(string? preset)
        {
            if (!string.IsNullOrEmpty(preset))
            {
                _themeService.ApplyAccentPreset(preset);
            }
        }

        private async Task ExecuteCheckUpdatesManualAsync()
        {
            if (IsCheckingUpdates) return;
            IsCheckingUpdates = true;
            try
            {
                _toastService.ShowInfo("Проверка наличия обновлений...", "Обновления");
                var release = await UpdateService.Instance.CheckForUpdatesAsync(isManual: true);
                if (release != null && release.HasUpdate)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var updateWin = new Views.Windows.UpdateWindow(release)
                        {
                            Owner = Application.Current.MainWindow
                        };
                        updateWin.ShowDialog();
                    });
                }
                else
                {
                    _toastService.ShowSuccess($"У вас установлена актуальная версия {UpdateService.CurrentVersion}", "Обновления");
                }
            }
            catch (Exception ex)
            {
                _toastService.ShowError($"Не удалось проверить обновления: {ex.Message}", "Ошибка");
            }
            finally
            {
                IsCheckingUpdates = false;
            }
        }

        private async Task ExecuteExportDiagnosticReportAsync()
        {
            try
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Сохранить диагностический отчёт",
                    Filter = "ZIP-архив (*.zip)|*.zip",
                    FileName = $"qlauncher-report-{DateTime.Now:yyyyMMdd_HHmmss}.zip"
                };

                if (sfd.ShowDialog() == true)
                {
                    string zipPath = await DiagnosticReportService.Instance.GenerateReportZipAsync(sfd.FileName);
                    _toastService.ShowSuccess($"Диагностический отчёт сохранён:\n{Path.GetFileName(zipPath)}", "Диагностика");
                }
            }
            catch (Exception ex)
            {
                _toastService.ShowError($"Ошибка при создании отчёта: {ex.Message}", "Диагностика");
            }
        }

        private void ExecuteAutoTuneHardware()
        {
            try
            {
                long totalRamBytes = 0;
                try
                {
                    var gcMemoryInfo = GC.GetGCMemoryInfo();
                    totalRamBytes = gcMemoryInfo.TotalAvailableMemoryBytes;
                }
                catch { }

                long totalRamMb = totalRamBytes > 0 ? (totalRamBytes / (1024 * 1024)) : 8192;

                int optimalRam;
                if (totalRamMb <= 4096)
                    optimalRam = 2048;
                else if (totalRamMb <= 8192)
                    optimalRam = 3584;
                else if (totalRamMb <= 16384)
                    optimalRam = 5120;
                else
                    optimalRam = 6144;

                RamMb = optimalRam;
                JvmPreset = "aikar";

                var discreteGpu = GpuOptions.FirstOrDefault(g => g.Id == "HighPerformance" || g.Subtitle.Contains("Дискретная"));
                if (discreteGpu != null)
                {
                    SelectedGpuOption = discreteGpu;
                }

                SaveSettings();
                _toastService.ShowSuccess($"Настройки оптимизированы: RAM {optimalRam} МБ, профиль Aikar G1GC, приоритет дискретного GPU.", "Авто-оптимизация");
            }
            catch (Exception ex)
            {
                _toastService.ShowError($"Не удалось выполнить авто-оптимизацию: {ex.Message}", "Ошибка");
            }
        }

        private async Task ExecuteExportPortableAsync()
        {
            try
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Сохранить портативный пакет QLauncher",
                    Filter = "ZIP-архив (*.zip)|*.zip",
                    FileName = "QLauncher-Portable.zip"
                };

                if (sfd.ShowDialog() == true)
                {
                    _toastService.ShowInfo("Упаковка портативной версии...", "Портативный режим");
                    string path = await LauncherPathHelper.ExportPortablePackageAsync(sfd.FileName);
                    _toastService.ShowSuccess($"Портативная версия успешно экспортирована:\n{Path.GetFileName(path)}", "Портативный режим");
                }
            }
            catch (Exception ex)
            {
                _toastService.ShowError($"Ошибка экспорта портативной версии: {ex.Message}", "Ошибка");
            }
        }

        private void ExecuteOpenDataFolder()
        {
            try
            {
                string dir = LauncherPathHelper.GetDefaultDataDirectory();
                Directory.CreateDirectory(dir);
                Process.Start("explorer.exe", dir);
            }
            catch { }
        }

        private void ExecuteChangeLanguage(string? lang)
        {
            if (!string.IsNullOrEmpty(lang))
            {
                LocalizationService.Instance.SetLanguage(lang);
                var s = _settingsService.Settings;
                s.Language = lang;
                _settingsService.Save(s);
                OnPropertyChanged(nameof(CurrentLanguage));
                OnPropertyChanged(nameof(IsRussianLanguage));
                OnPropertyChanged(nameof(IsEnglishLanguage));
                OnPropertyChanged(nameof(PortableModeStatusText));
                OnPropertyChanged(nameof(CurrentVersionText));
                _toastService.ShowSuccess(lang == "en" ? "Language changed to English" : "Язык интерфейса изменён на Русский", "Language");
            }
        }
    }
}
