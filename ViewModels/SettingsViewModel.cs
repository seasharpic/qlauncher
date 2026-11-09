using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
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

        // Локализация для строк, которые собираются в коде: в XAML подстановкой
        // занимается DynamicResource, здесь нужен явный вызов.
        private readonly ILocalizationService _loc = LocalizationService.Instance;

        private bool _closeOnLaunch;
        private bool _enableDiscordRpc;
        private bool _enableUiSounds;
        private bool _autoAddServers;
        private bool _hideServerIp;
        private int _ramMb;
        private string _javaPath = "";
        private string _jvmPreset = JvmOptimizationHelper.AutoPresetName;
        private string _customJvmArgs = "";
        private bool _useOptimizedJvmArgs = true;
        private string _resolvedJvmArgs = "";
        private string _jvmArgsWarning = "";
        private bool _isFullScreen;
        private int _screenWidth;
        private int _screenHeight;
        private string _customWallpaperPath = "";
        private string _cacheInfoText = "";
        private string _offlineNicknameInput = "";
        private string _elyLoginInput = "";
        private string _elyPasswordInput = "";
        private bool _elyNeedsTotp;
        private string _gpuPreference = "HighPerformance";
        private GpuOptionItem? _selectedGpuOption;
        private string _activeGpuSummary = "";
        private string _activeGpuDetails = "";
        private bool _checkUpdatesOnStartup = true;
        private bool _showSplashOnStartup = true;
        private string _updateChannel = "stable";
        private string _updateMirror = "auto";
        private string _mirrorPreference = "Auto";
        private string _sourcesStatusText = "";
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
                if (SetProperty(ref _ramMb, value))
                {
                    SaveSettings();
                    // Пресет "Auto" выбирает сборщик по объёму памяти, поэтому
                    // изменение RAM меняет и набор флагов.
                    _ = RefreshJvmPreviewAsync();
                    // Пилюля над слайдером и позиция бегунка зависят от значения.
                    OnPropertyChanged(nameof(RamPill));
                    OnPropertyChanged(nameof(RamIndex));
                }
            }
        }

        /// <summary>
        /// Шаг RAM-слайдера как в современных браузерах модов: равные позиции,
        /// неравные объёмы (1, 2, 4, 6, 8 ... 16 GB). Прежний ползунок щёлкал
        /// каждый гигабайт, и вся полоса уходила под согласование с подписями.
        /// </summary>
        public static readonly int[] RamOptions = { 1024, 2048, 4096, 6144, 8192, 10240, 12288, 14336, 16384 };

        public int RamIndex
        {
            get
            {
                int best = 0;
                for (int i = 1; i < RamOptions.Length; i++)
                {
                    // Начальное значение после загрузки настроек может не совпадать
                    // ни с одним шагом (RAM задавали вручную) — привязка к ближайшему.
                    if (Math.Abs(RamOptions[i] - RamMb) < Math.Abs(RamOptions[best] - RamMb))
                    {
                        best = i;
                    }
                }
                return best;
            }
            set
            {
                value = Math.Clamp(value, 0, RamOptions.Length - 1);
                RamMb = RamOptions[value];
            }
        }

        /// <summary>Подпись над бегунком слайдера, например "8192 MB".</summary>
        public string RamPill => $"{RamMb} MB";

        public string JavaPath
        {
            get => _javaPath;
            set
            {
                if (SetProperty(ref _javaPath, value))
                {
                    SaveSettings();
                    // Версия JVM определяет доступные сборщики мусора, а конкретная
                    // сборка — реальный набор опций.
                    _ = RefreshJvmPreviewAsync();
                }
            }
        }

        public string JvmPreset
        {
            get => _jvmPreset;
            set
            {
                if (SetProperty(ref _jvmPreset, value))
                {
                    SaveSettings();
                    _ = RefreshJvmPreviewAsync();
                }
            }
        }

        public string CustomJvmArgs
        {
            get => _customJvmArgs;
            set
            {
                if (SetProperty(ref _customJvmArgs, value))
                {
                    SaveSettings();
                    _ = RefreshJvmPreviewAsync();
                }
            }
        }

        public bool UseOptimizedJvmArgs
        {
            get => _useOptimizedJvmArgs;
            set
            {
                if (SetProperty(ref _useOptimizedJvmArgs, value))
                {
                    SaveSettings();
                    _ = RefreshJvmPreviewAsync();
                }
            }
        }

        /// <summary>
        /// Аргументы, которые лаунчер реально подставит при следующем запуске.
        /// Показывается пользователю, чтобы выбор пресета не был «чёрным ящиком».
        /// </summary>
        public string ResolvedJvmArgs
        {
            get => _resolvedJvmArgs;
            private set => SetProperty(ref _resolvedJvmArgs, value);
        }

        /// <summary>
        /// Предупреждение о понижении пресета (например, ZGC на Java 8).
        /// Пустая строка — всё в порядке.
        /// </summary>
        public string JvmArgsWarning
        {
            get => _jvmArgsWarning;
            private set
            {
                if (SetProperty(ref _jvmArgsWarning, value))
                {
                    OnPropertyChanged(nameof(HasJvmArgsWarning));
                }
            }
        }

        public bool HasJvmArgsWarning => !string.IsNullOrEmpty(_jvmArgsWarning);

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

        /// <summary>Логин (E-mail или ник) для входа через Ely.by.</summary>
        public string ElyLoginInput
        {
            get => _elyLoginInput;
            set => SetProperty(ref _elyLoginInput, value);
        }

        /// <summary>
        /// Пароль Ely.by. Сеттер вызывается из code-behind страницы настроек:
        /// PasswordBox не умеет обычного биндинга, свойства Password является
        /// зависимым от защищённого ввода. Пароль живёт только в памяти.
        /// </summary>
        public string ElyPasswordInput
        {
            get => _elyPasswordInput;
            set => SetProperty(ref _elyPasswordInput, value);
        }

        /// <summary>
        /// Сервер Ely.by запросил код 2FA. Разблокирует поле ввода кода
        /// и меняет кнопку входа на «повторить с кодом».
        /// </summary>
        public bool ElyNeedsTotp
        {
            get => _elyNeedsTotp;
            set
            {
                if (SetProperty(ref _elyNeedsTotp, value))
                {
                    OnPropertyChanged(nameof(ElyTotpVisibility));
                }
            }
        }

        public string ElyTotpCodeInput { get; set; } = "";

        public Visibility ElyTotpVisibility => ElyNeedsTotp ? Visibility.Visible : Visibility.Collapsed;

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

        public string MirrorPreference
        {
            get => _mirrorPreference;
            set
            {
                if (!SetProperty(ref _mirrorPreference, value)) return;

                // Применяем сразу: от этого зависит порядок источников при
                // следующей загрузке, и он должен быть виден без перезапуска.
                MirrorService.Instance.Preference = MirrorService.ParsePreference(value);
                _settingsService.Settings.MirrorPreference =
                    MirrorService.PreferenceToString(MirrorService.Instance.Preference);
                SaveSettings();
                _ = RefreshSourcesStatusAsync();
            }
        }

        /// <summary>
        /// Что показать пользователю о состоянии источников: какие хосты
        /// отвечают. Пусто, пока проверка не выполнена или пользователь
        /// выбрал OfficialOnly (там результат не влияет на поведение).
        /// </summary>
        public string SourcesStatusText
        {
            get => _sourcesStatusText;
            private set => SetProperty(ref _sourcesStatusText, value);
        }

        public async Task RefreshSourcesStatusAsync()
        {
            try
            {
                await MirrorService.Instance.ProbeAsync();
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("SettingsViewModel", "Failed to refresh sources status", ex);
            }

            if (IsClosed) return;

            MirrorMode preference = MirrorService.Instance.Preference;

            string text;
            if (preference == MirrorMode.OfficialOnly)
            {
                text = _loc.GetString("Str_Sources_OfficialOnlyNote");
            }
            else if (!MirrorService.Instance.HasProbeResult)
            {
                text = _loc.GetString("Str_Sources_Checking");
            }
            else
            {
                // Список недоступных хостов: он зависит от результата проверки,
                // поэтому собирается в коде, а не берётся готовой строкой.
                string blocked = MirrorService.DescribeBlockedHosts();
                text = string.IsNullOrEmpty(blocked)
                    ? _loc.GetString("Str_Sources_AllReachable")
                    : _loc.Format("Str_Sources_Blocked", blocked);
            }

            _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                SourcesStatusText = text;
            }));
        }

        public int BandwidthLimitMbps
        {
            get => _bandwidthLimitMbps;
            set
            {
                if (SetProperty(ref _bandwidthLimitMbps, value))
                {
                    OnPropertyChanged(nameof(BandwidthLimitTag));

                    // Применяем сразу: загрузка могла уже идти, и лимит
                    // обязан влиять на неё без перезапуска лаунчера.
                    BandwidthLimiter.Configure(value);

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

        /// <summary>Кнопка «включить портативный режим» скрыта, когда он уже включён.</summary>
        public bool CanEnablePortableMode => !IsPortableMode;

        /// <summary>Кнопка «выключить портативный режим» скрыта в обычном режиме.</summary>
        public bool CanDisablePortableMode => IsPortableMode;

        public string PortableModeStatusText => IsPortableMode
            ? _loc.GetString("Str_Settings_PortableActive")
            : _loc.GetString("Str_Settings_PortableInactive");

        public string CurrentVersionText => $"{UpdateService.CurrentVersion}{(IsPortableMode ? _loc.GetString("Str_Settings_PortableSuffix") : "")}";

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
        public AsyncRelayCommand LoginElyByCommand { get; }
        public AsyncRelayCommand LoginElyByBrowserCommand { get; }
        public AsyncRelayCommand RetryElyTwoFactorCommand { get; }
        public RelayCommand<AccountProfile> DeleteAccountCommand { get; }
        public RelayCommand<string> SelectAccentPresetCommand { get; }
        public RelayCommand<string> SelectThemeCommand { get; }
        public RelayCommand RefreshGpuCommand { get; }
        public RelayCommand AutoTuneHardwareCommand { get; }
        public AsyncRelayCommand ExportPortableCommand { get; }
        public RelayCommand EnablePortableModeCommand { get; }
        public RelayCommand DisablePortableModeCommand { get; }
        public AsyncRelayCommand RestartToApplyPortableModeCommand { get; }
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
            LoginElyByCommand = new AsyncRelayCommand(ExecuteLoginElyByAsync);
            LoginElyByBrowserCommand = new AsyncRelayCommand(ExecuteLoginElyByBrowserAsync);
            RetryElyTwoFactorCommand = new AsyncRelayCommand(ExecuteLoginElyByAsync);
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
                _toastService.ShowSuccess(_loc.GetString("Str_Gpu_ListUpdated"));
            });

            AutoTuneHardwareCommand = new RelayCommand(ExecuteAutoTuneHardware);
            ExportPortableCommand = new AsyncRelayCommand(ExecuteExportPortableAsync);
            EnablePortableModeCommand = new RelayCommand(ExecuteEnablePortableMode);
            DisablePortableModeCommand = new RelayCommand(ExecuteDisablePortableMode);
            RestartToApplyPortableModeCommand = new AsyncRelayCommand(ExecuteRestartToApplyPortableModeAsync);
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

            // Фоновый подсчёт кэша мог завершиться после ухода со страницы
            // и обновить свойства уже невидимого ViewModel.
            IsClosed = true;
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
            _jvmPreset = JvmOptimizationHelper.NormalizePresetName(s.JvmPreset);
            _customJvmArgs = s.CustomJvmArgs;
            _useOptimizedJvmArgs = s.UseOptimizedJvmArgs;
            _isFullScreen = s.IsFullScreen;
            _screenWidth = s.ScreenWidth;
            _screenHeight = s.ScreenHeight;
            _customWallpaperPath = s.CustomWallpaperPath;
            _gpuPreference = string.IsNullOrWhiteSpace(s.GpuPreference) ? "HighPerformance" : s.GpuPreference;
            _checkUpdatesOnStartup = s.CheckUpdatesOnStartup;
            _showSplashOnStartup = s.ShowSplashOnStartup;
            _updateChannel = string.IsNullOrWhiteSpace(s.UpdateChannel) ? "stable" : s.UpdateChannel;
            _updateMirror = string.IsNullOrWhiteSpace(s.UpdateMirror) ? "auto" : s.UpdateMirror;
            _mirrorPreference = MirrorService.PreferenceToString(MirrorService.ParsePreference(s.MirrorPreference));
            _bandwidthLimitMbps = s.BandwidthLimitMbps;

            // Синхронизируем рантайм с настройками: MirrorService — синглтон,
            // и он должен знать предпочтение ещё до первого запроса.
            MirrorService.Instance.Preference = MirrorService.ParsePreference(s.MirrorPreference);

            OnPropertyChanged(string.Empty);

            PopulateGpuOptions();
            UpdateCacheInfo();
            _ = RefreshJvmPreviewAsync();
            _ = RefreshSourcesStatusAsync();

            Accounts.Clear();
            foreach (var acc in s.Accounts)
            {
                acc.AvatarImage = AvatarHelper.GetDefaultAvatar();
                Accounts.Add(acc);

                // Задача запускается без await, поэтому её исключение наблюдалось бы
                // только через UnobservedTaskException. Ошибки сети при загрузке
                // аватара молча терялись.
                _ = LoadAvatarAsync(acc);
            }
        }

        /// <summary>
        /// Пересчитывает предпросмотр аргументов JVM.
        ///
        /// Проба опций запускает javaw.exe, поэтому делается на фоне и не блокирует
        /// UI. Результат кэшируется в JavaService по пути, так что при переборе
        /// пресетов новый процесс не создаётся.
        /// </summary>
        private async Task RefreshJvmPreviewAsync()
        {
            try
            {
                string javaPath = JavaPath;

                int javaMajor = await Task.Run(() =>
                {
                    if (!string.IsNullOrWhiteSpace(javaPath) && File.Exists(javaPath))
                    {
                        return JavaService.DetectJavaMajorVersion(javaPath);
                    }

                    // Путь не задан: лаунчер сам скачает Java по требованиям версии
                    // игры. Для предпросмотра этого достаточно.
                    string mcVersion = _settingsService.Settings.LastSelectedVersion;
                    return string.IsNullOrWhiteSpace(mcVersion)
                        ? JavaCompatibilityService.DefaultJavaVersion
                        : JavaCompatibilityService.GetRequiredJavaVersionCore(mcVersion);
                });

                var supportedFlags = string.IsNullOrWhiteSpace(javaPath)
                    ? null
                    : await JavaService.GetSupportedFlagsAsync(javaPath);

                string? warning;
                var flags = JvmOptimizationHelper.GetOptimizedJvmArguments(
                    JvmPreset,
                    RamMb,
                    javaMajor,
                    CustomJvmArgs,
                    UseOptimizedJvmArgs,
                    supportedFlags,
                    out warning);

                int ram = RamMb;
                string memoryArgs = $"-Xmx{ram}M -Xms{Math.Max(512, ram / 2)}M";
                string text = string.Join(' ', flags.Prepend(memoryArgs));

                if (IsClosed) return;

                _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    ResolvedJvmArgs = text;
                    JvmArgsWarning = warning ?? "";
                }));
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("SettingsViewModel", "Failed to build JVM arguments preview", ex);
            }
        }

        private async Task LoadAvatarAsync(AccountProfile acc)
        {
            try
            {
                var img = await AvatarHelper.GetAvatarAsync(acc.Nickname, acc.Uuid);

                if (img == null || IsClosed) return;

                // Присваивание через "_ =" подавляет предупреждение CS4014:
                // результат намеренно не ожидается, задача наблюдает свои ошибки сама.
                _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    acc.AvatarImage = img;
                }));
            }
            catch (Exception ex)
            {
                // Аватар не критичен: оставляем дефолтный и пишем в лог.
                CrashLogWriter.Write("AvatarLoad", $"Failed to load avatar for '{acc.Nickname}'", ex);
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
            s.UseOptimizedJvmArgs = UseOptimizedJvmArgs;
            s.IsFullScreen = IsFullScreen;
            s.ScreenWidth = ScreenWidth;
            s.ScreenHeight = ScreenHeight;
            s.CustomWallpaperPath = CustomWallpaperPath;
            s.GpuPreference = _gpuPreference;
            s.CheckUpdatesOnStartup = CheckUpdatesOnStartup;
            s.ShowSplashOnStartup = ShowSplashOnStartup;
            s.UpdateChannel = UpdateChannel;
            s.UpdateMirror = UpdateMirror;
            s.MirrorPreference = MirrorService.PreferenceToString(MirrorService.ParsePreference(MirrorPreference));
            s.BandwidthLimitMbps = BandwidthLimitMbps;

            // SaveSettings вызывается из сеттера любого свойства, поэтому обычный
            // Save писал и сериализовал settings.json на UI-потоке при каждом
            // нажатии чекбокса. Отложенная запись убирает это.
            _settingsService.SaveDebounced();
        }

        public void PopulateGpuOptions()
        {
            GpuOptions.Clear();

            GpuOptions.Add(new GpuOptionItem
            {
                Id = "HighPerformance",
                Title = _loc.GetString("Str_Gpu_HighPerf"),
                Subtitle = _loc.GetString("Str_Gpu_HighPerfSub")
            });

            GpuOptions.Add(new GpuOptionItem
            {
                Id = "PowerSaving",
                Title = _loc.GetString("Str_Gpu_PowerSaving"),
                Subtitle = _loc.GetString("Str_Gpu_PowerSavingSub")
            });

            GpuOptions.Add(new GpuOptionItem
            {
                Id = "Default",
                Title = _loc.GetString("Str_Gpu_Default"),
                Subtitle = _loc.GetString("Str_Gpu_DefaultSub")
            });

            var detectedGpus = GpuService.Instance.GetAvailableGpus();
            foreach (var gpu in detectedGpus)
            {
                GpuOptions.Add(new GpuOptionItem
                {
                    Id = gpu.Name,
                    Title = gpu.DisplayName,
                    Subtitle = _loc.Format(
                        "Str_Gpu_DriverLine",
                        gpu.DriverVersion,
                        _loc.GetString(gpu.IsDiscrete ? "Str_Gpu_KindDiscrete" : "Str_Gpu_KindIntegrated"))
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
                    ? _loc.Format("Str_Gpu_SummaryHighPerf", discreteGpu.DisplayName)
                    : _loc.GetString("Str_Gpu_ModeHighPerf");
                ActiveGpuDetails = _loc.GetString("Str_Gpu_DetailHighPerf");
            }
            else if (string.Equals(_gpuPreference, "PowerSaving", StringComparison.OrdinalIgnoreCase))
            {
                var integratedGpu = detectedGpus.FirstOrDefault(g => !g.IsDiscrete);
                ActiveGpuSummary = integratedGpu != null
                    ? _loc.Format("Str_Gpu_SummaryPowerSaving", integratedGpu.DisplayName)
                    : _loc.GetString("Str_Gpu_ModePowerSaving");
                ActiveGpuDetails = _loc.GetString("Str_Gpu_DetailPowerSaving");
            }
            else if (string.Equals(_gpuPreference, "Default", StringComparison.OrdinalIgnoreCase))
            {
                ActiveGpuSummary = primaryGpu != null
                    ? _loc.Format("Str_Gpu_SummaryDefault", primaryGpu.Name)
                    : _loc.GetString("Str_Gpu_ModeDefault");
                ActiveGpuDetails = _loc.GetString("Str_Gpu_DetailDefault");
            }
            else
            {
                var specificGpu = detectedGpus.FirstOrDefault(g => string.Equals(g.Name, _gpuPreference, StringComparison.OrdinalIgnoreCase));
                if (specificGpu != null)
                {
                    ActiveGpuSummary = _loc.Format("Str_Gpu_SummarySpecific", specificGpu.DisplayName);
                    ActiveGpuDetails = _loc.Format("Str_Gpu_DetailSpecific", specificGpu.DriverVersion, specificGpu.VramFormatted);
                }
                else
                {
                    ActiveGpuSummary = _loc.Format("Str_Gpu_SummarySpecific", _gpuPreference);
                    ActiveGpuDetails = _loc.GetString("Str_Gpu_DetailUnknown");
                }
            }
        }

        /// <summary>
        /// Обновляет индикатор размера кэша.
        ///
        /// Раньше подсчёт шёл на UI-потоке прямо из LoadFromSettings, то есть
        /// рекурсивный обход каталогов блокировал интерфейс при открытии настроек.
        /// Теперь считаем в Task.Run и обновляем текст по завершении.
        /// </summary>
        private async void UpdateCacheInfo()
        {
            string gamePath = _settingsService.Settings.GamePath;

            long bytes = await Task.Run(() => CacheCleanerHelper.CalculateCacheSize(gamePath));

            double mb = bytes / (1024.0 * 1024.0);

            // Страница могла быть закрыта, пока шёл подсчёт.
            if (IsClosed) return;

            CacheInfoText = _loc.Format("Str_Cache_Size", $"{mb:F1}");
        }

        private bool IsClosed { get; set; }

        private void ExecuteBrowseJava()
        {
            var dlg = new OpenFileDialog
            {
                Filter = _loc.GetString("Str_Dialog_JavaFilter"),
                Title = _loc.GetString("Str_Dialog_JavaTitle")
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
                Filter = _loc.GetString("Str_Dialog_ImageFilter"),
                Title = _loc.GetString("Str_Dialog_WallpaperTitle")
            };

            if (dlg.ShowDialog() == true)
            {
                CustomWallpaperPath = dlg.FileName;
            }
        }

        private async void ExecuteCleanCache()
        {
            string gamePath = _settingsService.Settings.GamePath;

            // Удаление каталогов — дисковая операция, раньше выполнялась на UI-потоке.
            double freedMb = await Task.Run(() => CacheCleanerHelper.CleanCache(gamePath));

            UpdateCacheInfo();

            if (freedMb < 0.05)
            {
                _toastService.ShowInfo(_loc.GetString("Str_Cache_NothingToClean"), _loc.GetString("Str_T_Cache"));
                return;
            }

            _toastService.ShowSuccess(_loc.Format("Str_Cache_Freed", $"{freedMb:F1}"), _loc.GetString("Str_T_Cache"));
        }

        private void ExecuteAddOfflineAccount()
        {
            string nick = OfflineNicknameInput.Trim();
            if (string.IsNullOrWhiteSpace(nick))
            {
                _toastService.ShowWarning(_loc.GetString("Str_Offline_NickRequired"), _loc.GetString("Str_T_Account"));
                return;
            }

            var s = _settingsService.Settings;
            if (s.Accounts.Any(a => string.Equals(a.Nickname, nick, StringComparison.OrdinalIgnoreCase)))
            {
                _toastService.ShowWarning(_loc.GetString("Str_Offline_NickExists"), _loc.GetString("Str_T_Account"));
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
            _toastService.ShowSuccess(_loc.Format("Str_Offline_AddedOk", nick), _loc.GetString("Str_T_Account"));
        }

        private async Task ExecuteLoginMicrosoftAsync()
        {
            try
            {
                _toastService.ShowInfo(_loc.GetString("Str_MsLoginOpening"), _loc.GetString("Str_MsLogin"));

                // Через сервис, а не через JELoginHandlerBuilder.BuildDefault():
                // встроенный обработчик держит refresh-токен в памяти, и после
                // закрытия лаунчера продлевать сессию было бы нечем.
                var session = await MicrosoftAuthService.Instance.LoginInteractivelyAsync();

                if (session != null && session.IsUsable)
                {
                    var s = _settingsService.Settings;
                    var existing = s.Accounts.FirstOrDefault(a => a.Nickname == session.Username);

                    if (existing != null)
                    {
                        existing.AccessToken = session.AccessToken;
                        existing.Uuid = session.Uuid;
                    }
                    else
                    {
                        s.Accounts.Add(new AccountProfile
                        {
                            Nickname = session.Username,
                            AccessToken = session.AccessToken,
                            Uuid = session.Uuid
                        });
                    }

                    s.ActiveAccount = session.Username;
                    _settingsService.Save(s);
                    LoadFromSettings();

                    _toastService.ShowSuccess(_loc.Format("Str_MsLoginSuccess", session.Username), _loc.GetString("Str_T_OAuth"));
                }
            }
            catch (Exception ex)
            {
                _toastService.ShowError(_loc.Format("Str_MsLoginError", ex.Message), _loc.GetString("Str_T_Error"));
            }
        }

        /// <summary>
        /// Вход через Ely.by по Yggdrasil-протоколу. При первом ответе сервера
        /// «аккаунт защищён двухфакторной аутентификацией» поле с кодом 2FA
        /// раскрывается, и пользователь отправляет форму ещё раз — запрос
        /// повторяется уже с паролем вида «пароль:код».
        /// </summary>
        private async Task ExecuteLoginElyByAsync()
        {
            string login = ElyLoginInput.Trim();
            string password = ElyPasswordInput;

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            {
                _toastService.ShowWarning(_loc.GetString("Str_Ely_LoginRequired"), _loc.GetString("Str_T_Account"));
                return;
            }

            string? totp = ElyNeedsTotp ? ElyTotpCodeInput.Trim() : null;
            if (ElyNeedsTotp && string.IsNullOrWhiteSpace(totp))
            {
                _toastService.ShowWarning(_loc.GetString("Str_Ely_LoginRequired"), _loc.GetString("Str_T_Account"));
                return;
            }

            try
            {
                var session = await ElyByAuthService.Instance.LoginAsync(login, password, totp);

                if (session.NeedsTwoFactor)
                {
                    ElyNeedsTotp = true;
                    ElyTotpCodeInput = "";
                    OnPropertyChanged(nameof(ElyTotpCodeInput));
                    _toastService.ShowWarning(_loc.GetString("Str_Ely_TotpNeeded"), _loc.GetString("Str_T_Account"));
                    return;
                }

                if (!session.IsUsable)
                {
                    _toastService.ShowError(
                        _loc.Format("Str_Ely_LoginError", string.IsNullOrWhiteSpace(session.ErrorMessage)
                            ? _loc.GetString("Str_Ely_AuthFailedGeneric")
                            : session.ErrorMessage),
                        _loc.GetString("Str_T_Error"));
                    return;
                }

                // Форма сбрасывается целиком, включая скрытие поля 2FA.
                ElyNeedsTotp = false;
                ElyLoginInput = "";
                ElyPasswordInput = "";
                ElyTotpCodeInput = "";
                OnPropertyChanged(nameof(ElyTotpCodeInput));

                await FinalizeElyLoginAsync(session);

                _toastService.ShowSuccess(_loc.Format("Str_Ely_LoginSuccess", session.Username), _loc.GetString("Str_T_Account"));
            }
            catch (Exception ex)
            {
                _toastService.ShowError(_loc.Format("Str_Ely_LoginError", ex.Message), _loc.GetString("Str_T_Error"));
            }
        }

        /// <summary>
        /// Вход через браузер по Device Code Flow — стиль PineconeMC:
        /// лаунчер показывает короткий код, пользователь вводит его на
        /// странице account.ely.by, и пароль нигде не участвует. Опрос
        /// токенов по RFC 8628 идёт до подтверждения или до истечения срока.
        /// </summary>
        private async Task ExecuteLoginElyByBrowserAsync()
        {
            try
            {
                var start = await ElyByAuthService.Instance.StartDeviceAuthAsync();

                if (start == null)
                {
                    _toastService.ShowError(_loc.GetString("Str_Ely_DeviceStartFailed"), _loc.GetString("Str_T_Error"));
                    return;
                }

                // Страница авторизации Ely.by открывается в системном браузере,
                // а не внутри лаунчера: пользователь авторизуется на доверенном
                // сайте и лаунчер паролю не доверяет и не хранит.
                Process.Start(new ProcessStartInfo(start.VerificationUriComplete) { UseShellExecute = true });

                _toastService.ShowInfo(_loc.Format("Str_Ely_DeviceHint", start.UserCode), _loc.GetString("Str_T_Account"));

                var session = await ElyByAuthService.Instance.WaitForDeviceTokenAsync(start);

                if (session == null || !session.IsUsable)
                {
                    _toastService.ShowError(
                        _loc.Format("Str_Ely_LoginError",
                            string.IsNullOrWhiteSpace(session?.ErrorMessage)
                                ? _loc.GetString("Str_Ely_DeviceExpired")
                                : session.ErrorMessage),
                        _loc.GetString("Str_T_Error"));
                    return;
                }

                await FinalizeElyLoginAsync(session);

                _toastService.ShowSuccess(_loc.Format("Str_Ely_LoginSuccess", session.Username), _loc.GetString("Str_T_Account"));
            }
            catch (Exception ex)
            {
                _toastService.ShowError(_loc.Format("Str_Ely_LoginError", ex.Message), _loc.GetString("Str_T_Error"));
            }
        }

        /// <summary>
        /// Общая часть обоих стилей входа Ely.by (пароль и браузер):
        /// обновляет существующий аккаунт или добавляет новый, ставит его
        /// активным и перечитывает список в UI.
        /// </summary>
        private async Task FinalizeElyLoginAsync(ElyBySession session)
        {
            var s = _settingsService.Settings;
            var existing = s.Accounts.FirstOrDefault(a =>
                string.Equals(a.Nickname, session.Username, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.AccessToken = session.AccessToken;
                existing.Uuid = session.Uuid;
                existing.IsElyBy = true;

                // OAuth-токены приходят с refresh-токеном, без него — остались
                // на Yggdrasil-входе. Не затираем пустым, чтобы не убить
                // сохранённую возможность продлевать.
                if (!string.IsNullOrEmpty(session.RefreshToken))
                {
                    existing.ElyOauthRefreshToken = session.RefreshToken;
                }
            }
            else
            {
                s.Accounts.Add(new AccountProfile
                {
                    Nickname = session.Username,
                    AccessToken = session.AccessToken,
                    Uuid = session.Uuid,
                    IsElyBy = true,
                    ElyOauthRefreshToken = session.RefreshToken
                });
            }

            s.ActiveAccount = session.Username;
            _settingsService.Save(s);

            // Оба входа (пароль и браузер) командами AsyncRelayCommand, а те
            // выполняются на UI-потоке, поэтому перечитать список можно сразу.
            LoadFromSettings();
        }

        private void ExecuteDeleteAccount(AccountProfile? account)
        {
            if (account == null) return;

            var s = _settingsService.Settings;
            var toRemove = s.Accounts.FirstOrDefault(a => a.Nickname == account.Nickname);
            if (toRemove != null)
            {
                s.Accounts.Remove(toRemove);

                // Удаление аккаунта Microsoft должно убирать и сохранённый
                // refresh-токен: иначе секрет, позволяющий войти без пароля,
                // остаётся лежать на диске. Сброс общий — по одному аккаунту
                // библиотека удалять не даёт.
                if (!string.IsNullOrEmpty(account.AccessToken) &&
                    !string.Equals(account.AccessToken, Services.LaunchEngine.Models.LaunchOptions.OfflineAccessToken, StringComparison.OrdinalIgnoreCase))
                {
                    if (account.IsElyBy)
                    {
                        // Отзываем токен на стороне Ely.by (best-effort, без
                        // ожидания: удаление аккаунта не должно висеть на сети).
                        _ = ElyByAuthService.Instance.InvalidateAsync(account);
                    }
                    else
                    {
                        MicrosoftAuthService.Instance.SignOutAll();
                    }
                }

                if (s.ActiveAccount == account.Nickname)
                {
                    s.ActiveAccount = s.Accounts.FirstOrDefault()?.Nickname ?? "";
                }
                _settingsService.Save(s);
                LoadFromSettings();
                _toastService.ShowInfo(_loc.Format("Str_Offline_DeletedToast", account.Nickname), _loc.GetString("Str_T_Accounts"));
            }
        }

        private void ExecuteSelectAccentPreset(string? preset)
        {
            if (!string.IsNullOrEmpty(preset))
            {
                _themeService.ApplyAccentPreset(preset);
                OnPropertyChanged(nameof(SelectedAccentPreset));
            }
        }

        /// <summary>
        /// Какое имя пресета считать выбранным.
        /// Нужно, чтобы подсветить активный вариант: раньше выбор акцента
        /// нигде не отображался, и нельзя было понять, какой применён.
        /// </summary>
        public string SelectedAccentPreset
        {
            get
            {
                string preset = _settingsService.Settings.AccentPreset;
                return string.IsNullOrWhiteSpace(preset) ? "sapphire" : preset.ToLowerInvariant();
            }
        }

        private async Task ExecuteCheckUpdatesManualAsync()
        {
            if (IsCheckingUpdates) return;
            IsCheckingUpdates = true;
            try
            {
                _toastService.ShowInfo(_loc.GetString("Str_Update_CheckingLong"), _loc.GetString("Str_T_Updates"));
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
                    _toastService.ShowSuccess(_loc.Format("Str_Update_Current", UpdateService.CurrentVersion), _loc.GetString("Str_T_Updates"));
                }
            }
            catch (Exception ex)
            {
                _toastService.ShowError(_loc.Format("Str_Update_CheckError", ex.Message), _loc.GetString("Str_T_Error"));
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
                    Title = _loc.GetString("Str_Dialog_DiagnosticTitle"),
                    Filter = _loc.GetString("Str_Dialog_ZipFilter"),
                    FileName = $"qlauncher-report-{DateTime.Now:yyyyMMdd_HHmmss}.zip"
                };

                if (sfd.ShowDialog() == true)
                {
                    string zipPath = await DiagnosticReportService.Instance.GenerateReportZipAsync(sfd.FileName);
                    _toastService.ShowSuccess(_loc.Format("Str_Diagnostic_SavedPlain", Path.GetFileName(zipPath)), _loc.GetString("Str_T_Diagnostics"));
                }
            }
            catch (Exception ex)
            {
                _toastService.ShowError(_loc.Format("Str_Diagnostic_ErrorPlain", ex.Message), _loc.GetString("Str_T_Diagnostics"));
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

                // "Auto" вместо жёсткого пресета: сборщик подбирается под объём
                // памяти и под версию Java, которую реально скачает лаунчер.
                JvmPreset = JvmOptimizationHelper.AutoPresetName;
                UseOptimizedJvmArgs = true;

                // Отбор дискретной карты по Id, а не по русской подписи в Subtitle:
                // подпись меняется при смене языка, Id — нет.
                var discreteGpu = GpuOptions.FirstOrDefault(g => g.Id == "HighPerformance");
                if (discreteGpu != null)
                {
                    SelectedGpuOption = discreteGpu;
                }

                SaveSettings();
                _toastService.ShowSuccess(
                    _loc.Format("Str_AutoTune_Done", optimalRam),
                    _loc.GetString("Str_T_AutoTune"));
            }
            catch (Exception ex)
            {
                _toastService.ShowError(
                    _loc.Format("Str_AutoTune_Error", ex.Message),
                    _loc.GetString("Str_T_Error"));
            }
        }

        private async Task ExecuteExportPortableAsync()
        {
            try
            {
                // По умолчанию предлагаем полный перенос: пустой пакет на флешке
                // придётся заново настраивать. Если пользователю нужна «чистая»
                // копия для раздачи, снимаем галочку.
                bool includeGameData = _settingsService.Settings.GamePath.Length > 0;

                var result = System.Windows.MessageBox.Show(
                    _loc.GetString("Str_Export_QuestionRu"),
                    _loc.GetString("Str_Export_DialogTitleRu"),
                    includeGameData ? MessageBoxButton.OKCancel : MessageBoxButton.OK,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel)
                {
                    includeGameData = false;
                }
                else if (result != MessageBoxResult.OK)
                {
                    return;
                }

                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Title = _loc.GetString("Str_SaveFile_Title"),
                    Filter = _loc.GetString("Str_Dialog_ZipFilter"),
                    FileName = "QLauncher-Portable.zip"
                };

                if (sfd.ShowDialog() == true)
                {
                    _toastService.ShowInfo(_loc.GetString("Str_Portable_Exporting"), _loc.GetString("Str_T_PortableTitle"));
                    string path = await LauncherPathHelper.ExportPortablePackageAsync(sfd.FileName, includeGameData);
                    _toastService.ShowSuccess(
                        _loc.Format("Str_Portable_Exported", Path.GetFileName(path)),
                        _loc.GetString("Str_T_PortableTitle"));
                }
            }
            catch (Exception ex)
            {
                _toastService.ShowError(
                    _loc.Format("Str_Portable_ExportError", ex.Message),
                    _loc.GetString("Str_T_Error"));
            }
        }

        private void ExecuteEnablePortableMode()
        {
            if (LauncherPathHelper.EnablePortableMode())
            {
                // Раньше здесь стоял выбор строки через IsRussianLanguage: две
                // копии текста на каждое сообщение. Теперь язык определяется
                // ресурсами, поэтому отдельные варианты под каждый язык не нужны.
                _toastService.ShowSuccess(
                    _loc.GetString("Str_Portable_EnabledRu"),
                    _loc.GetString("Str_T_PortableTitle"));

                // settings.json сейчас читается из %APPDATA%, а после перезапуска
                // уже из ./data. Запись идёт по старому пути, поэтому сохраняем
                // настройки вручную до перезапуска — иначе перенесённый файл
                // окажется старым.
                _settingsService.Save();
                RaisePortableStateChanged();
            }
            else
            {
                _toastService.ShowError(
                    _loc.GetString("Str_Portable_EnableFailed"),
                    _loc.GetString("Str_T_Error"));
            }
        }

        private void ExecuteDisablePortableMode()
        {
            if (LauncherPathHelper.DisablePortableMode())
            {
                _toastService.ShowSuccess(
                    _loc.GetString("Str_Portable_DisabledRu"),
                    _loc.GetString("Str_T_PortableTitle"));

                RaisePortableStateChanged();
            }
            else
            {
                _toastService.ShowError(
                    _loc.GetString("Str_Portable_DisableFailed"),
                    _loc.GetString("Str_T_Error"));
            }
        }

        /// <summary>
        /// Сигнализирует UI об изменении портативного режима.
        /// Отдельный метод, потому что список свойств повторялся в двух
        /// обработчиках и раньше легко расходился.
        /// </summary>
        private void RaisePortableStateChanged()
        {
            OnPropertyChanged(nameof(IsPortableMode));
            OnPropertyChanged(nameof(CanEnablePortableMode));
            OnPropertyChanged(nameof(CanDisablePortableMode));
            OnPropertyChanged(nameof(PortableModeStatusText));
            OnPropertyChanged(nameof(DataDirectoryPath));
            OnPropertyChanged(nameof(CurrentVersionText));
        }

        private async Task ExecuteRestartToApplyPortableModeAsync()
        {
            string exePath = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                _toastService.ShowError(_loc.GetString("Str_Restart_ExeFailed"), _loc.GetString("Str_T_Error"));
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true
                });

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                _toastService.ShowError(
                    _loc.Format("Str_Restart_Failed", ex.Message),
                    _loc.GetString("Str_T_Error"));
            }

            await Task.CompletedTask;
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
                // Ключ выбирается по новому языку, а не по текущему: к моменту показа
                // подтверждения SetLanguage уже переключил словарь.
                _toastService.ShowSuccess(
                    LocalizationService.Instance.GetString(
                        lang == "en" ? "Str_Lang_ChangedToEn" : "Str_Lang_ChangedToRu"),
                    LocalizationService.Instance.GetString("Str_T_Lang"));
            }
        }
    }
}
