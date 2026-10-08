using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MinecraftLauncher.Common;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Models;
using MinecraftLauncher.Services;
using MinecraftLauncher.Services.LaunchEngine;
using MinecraftLauncher.Views.Windows;

namespace MinecraftLauncher.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;

        private readonly object _outputHandlersGate = new();
        private (DataReceivedEventHandler outHandler, DataReceivedEventHandler errHandler)? _outputHandlers;
        private Process? _attachedProcess;
        private readonly IMinecraftLaunchService _launchService;
        private readonly IServerStatusService _serverStatusService;
        private readonly INewsService _newsService;
        private readonly IDiscordService _discordService;
        private readonly IUpdateService _updateService;
        private readonly IAudioService _audioService;
        private readonly IThemeService _themeService;
        private readonly IToastService _toastService;

        // Локализация для строк, которые собираются в коде: в XAML подстановкой
        // занимается DynamicResource, здесь нужен явный вызов.
        private readonly ILocalizationService _loc = LocalizationService.Instance;

        private AccountProfile? _selectedAccount;
        private string _selectedVersion = "";
        private string _progressText = LocalizationService.Instance.GetString("Str_Progress_Ready");
        private double _progressFillRatio = 0.0;
        private bool _isActionOverlayVisible;
        private string _actionOverlayText = LocalizationService.Instance.GetString("Str_Progress_Processing");

        // Пункт списка «+ Создать новую сборку» и префикс строки прогресса
        // хранятся в полях, а не сравниваются с русским литералом: иначе
        // проверка "выбран ли пункт создания сборки" ломалась бы в англоязычном
        // интерфейсе.
        private string _newPackShortcut = "";
        private string _progressPreparingPrefix =
            LocalizationService.Instance.GetString("Str_Progress_Preparing").Split(' ')[0];
        private bool _isShortcutOverlayVisible;
        private string _shortcutVersion = "";
        private string _shortcutIp = "mc.hypixel.net";
        private bool _isModpackOverlayVisible;
        private string _newModpackName = LocalizationService.Instance.GetString("Str_Modpack_DefaultName");
        private bool _newModpackNameEdited;
        private string _newModpackVersion = "1.20.1";
        private string _newModpackLoader = "Vanilla";
        private bool _installSodium = true;
        private bool _installIris = true;
        private ImageBrush? _customBackgroundBrush;
        private bool _isAddServerOverlayVisible;
        private string _newServerName = "";
        private string _newServerIp = "";

        private DispatcherTimer? _newsTimer;
        private DispatcherTimer? _serverTimer;

        // Флаги защиты от наложения тиков (см. RunExclusiveAsync).
        private readonly SemaphoreSlim _newsRefreshRunning = new(1, 1);
        private readonly SemaphoreSlim _serverRefreshRunning = new(1, 1);
        private bool _disposed;

        /// <summary>
        /// Выполняет обновление, не позволяя двум тикам идти одновременно.
        /// Если предыдущий заход ещё не закончился, новый просто пропускается.
        /// </summary>
        private static async Task RunExclusiveAsync(Func<Task> operation, SemaphoreSlim gate)
        {
            if (!await gate.WaitAsync(0))
            {
                return;
            }

            try
            {
                await operation();
            }
            catch (Exception ex)
            {
                // Раньше исключение уходило в DispatcherUnhandledException и
                // всплывало модальным окном поверх игры.
                CrashLogWriter.Write("PeriodicRefresh", "Periodic refresh failed", ex);
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// Останавливает таймеры и освобождает ресурсы.
        /// Раньше они никогда не останавливались и держали сильную ссылку на
        /// MainViewModel, продолжая тикать после закрытия окна.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _newsTimer?.Stop();
            _serverTimer?.Stop();

            _newsTimer = null;
            _serverTimer = null;

            DetachProcessOutputHandlers(_attachedProcess!);
        }

        public ObservableCollection<TelegramPost> NewsPosts { get; } = new();
        public ObservableCollection<ServerItem> Servers { get; } = new();
        public ObservableCollection<AccountProfile> Accounts { get; } = new();
        public ObservableCollection<string> Versions { get; } = new();
        public ObservableCollection<string> VanillaVersions { get; } = new();

        public bool HasServers => Servers.Count > 0;
        public bool HasNews => NewsPosts.Count > 0;

        public IDiscordService DiscordService => _discordService;
        public IUpdateService UpdateService => _updateService;

        public event Action<Process>? GameLaunched;

        /// <summary>Строка вывода процесса игры. Её читает окно консоли.</summary>
        public event Action<string?>? ProcessOutputLine;
        public event Action? RequestClose;
        public event Action? RequestHide;
        public event Action? RequestShow;

        public AccountProfile? SelectedAccount
        {
            get => _selectedAccount;
            set
            {
                if (SetProperty(ref _selectedAccount, value))
                {
                    if (value != null)
                    {
                        var settings = _settingsService.Settings;
                        settings.ActiveAccount = value.Nickname;
                        _settingsService.Save(settings);
                    }
                }
            }
        }

        public string SelectedVersion
        {
            get => _selectedVersion;
            set
            {
                if (SetProperty(ref _selectedVersion, value))
                {
                    if (!string.IsNullOrEmpty(value) && !IsCreateModpackEntry(value))
                    {
                        var settings = _settingsService.Settings;
                        settings.LastSelectedVersion = value;

                        // Отложенная запись: раньше settings.json переписывался
                        // на каждое изменение выбора версии.
                        _settingsService.SaveDebounced();

                        _discordService.UpdateSelectedVersion(value);
                    }
                }
            }
        }

        /// <summary>
/// Варианты текста пункта «+ Создать новую сборку», которые уже показывались
/// пользователю. Сравнивать с текстом словаря нельзя: список версий
/// собирается один раз при старте, а язык можно переключить в настройках
/// позже. После переключения список оставался со старым текстом, а
/// сравнение шло уже с новым — и пункт переставал распознаваться: окно
/// создания сборки не открывалось, а сам пункт оставался выбранным.
/// Поэтому варианты запоминаются, а не сверяются с литералом.
/// </summary>
private static readonly HashSet<string> CreateModpackEntryTexts =
    new(StringComparer.OrdinalIgnoreCase);

/// <summary>
/// Запоминает очередной вариант текста пункта создания сборки.
/// </summary>
public static void RegisterCreateModpackEntryText(string? text)
{
    if (string.IsNullOrWhiteSpace(text)) return;

    lock (CreateModpackEntryTexts)
    {
        CreateModpackEntryTexts.Add(text.Trim());
    }
}

/// <summary>
/// Является ли пункт списка версий командой «создать новую сборку».
/// </summary>
public static bool IsCreateModpackEntry(string? version)
{
    if (string.IsNullOrWhiteSpace(version)) return false;

    lock (CreateModpackEntryTexts)
    {
        if (CreateModpackEntryTexts.Contains(version.Trim())) return true;
    }

    // Страховка на случай, если язык сменился раньше, чем список пересобран:
    // сверяем ещё и с тем текстом, который отдаёт словарь прямо сейчас.
    string current = LocalizationService.Instance.GetString("Str_Progress_NewPackShortcut");
    return !string.IsNullOrWhiteSpace(current)
        && string.Equals(version.Trim(), current.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Пересобирает тексты, которые хранятся в полях, а не берутся из
/// DynamicResource.
///
/// Смена языка на ходу меняет словарь, поэтому такие строки сами не
/// обновятся: в английском интерфейсе пункт «+ Создать новую сборку»
/// оставался русским, и его больше нельзя было опознать.
/// </summary>
private void OnLanguageChanged()
{
    string newShortcut = _loc.GetString("Str_Progress_NewPackShortcut");
    RegisterCreateModpackEntryText(newShortcut);

    _progressPreparingPrefix = _loc.GetString("Str_Progress_Preparing").Split(' ')[0];

    // Имя новой сборки обновляем, только если пользователь его не трогал:
    // поле стартует со строки из словаря и переводится вместе с языком.
    if (!_newModpackNameEdited)
    {
        _newModpackName = _loc.GetString("Str_Modpack_DefaultName");
        OnPropertyChanged(nameof(NewModpackName));
    }

    int index = Versions.IndexOf(_newPackShortcut);
    if (index >= 0)
    {
        string previous = _newPackShortcut;
        _newPackShortcut = newShortcut;
        Versions[index] = newShortcut;

        // Выбранным остаётся тот же пункт, а не первая версия в списке:
        // иначе переключение языка сбрасывало бы выбор версии.
        if (SelectedVersion == previous)
        {
            SelectedVersion = newShortcut;
        }
    }
    else
    {
        _newPackShortcut = newShortcut;
    }

    // Статичные фразы строки прогресса переводим на новый язык. Фразы
    // активной операции («Загрузка библиотек 3/40») не трогаем: они и так
    // сменятся по ходу следующей операции.
    if (ReadyTextLanguages.Contains(ProgressText))
    {
        ProgressText = _loc.GetString("Str_Progress_Ready");
        ReadyTextLanguages.Add(ProgressText);
    }

    OnPropertyChanged(nameof(SelectedVersion));
    OnPropertyChanged(nameof(NewModpackName));
}

/// <summary>
/// Варианты статичной надписи в строке прогресса («Готов к запуску»).
/// Нужны, чтобы переводить её при смене языка, не задев текущий статус.
/// </summary>
private static readonly HashSet<string> ReadyTextLanguages =
    new(StringComparer.Ordinal)
    {
        "Готов к запуску",
        "Ready to play",
        "Готово к запуску",
    };

public string ProgressText
        {
            get => _progressText;
            set => SetProperty(ref _progressText, value);
        }

        public double ProgressFillRatio
        {
            get => _progressFillRatio;
            set => SetProperty(ref _progressFillRatio, value);
        }

        public bool IsActionOverlayVisible
        {
            get => _isActionOverlayVisible;
            set => SetProperty(ref _isActionOverlayVisible, value);
        }

        public string ActionOverlayText
        {
            get => _actionOverlayText;
            set => SetProperty(ref _actionOverlayText, value);
        }

        public bool IsShortcutOverlayVisible
        {
            get => _isShortcutOverlayVisible;
            set => SetProperty(ref _isShortcutOverlayVisible, value);
        }

        public string ShortcutVersion
        {
            get => _shortcutVersion;
            set => SetProperty(ref _shortcutVersion, value);
        }

        public string ShortcutIp
        {
            get => _shortcutIp;
            set => SetProperty(ref _shortcutIp, value);
        }

        public bool IsModpackOverlayVisible
        {
            get => _isModpackOverlayVisible;
            set => SetProperty(ref _isModpackOverlayVisible, value);
        }

        public string NewModpackName
        {
            get => _newModpackName;
            set
            {
                // Отмечаем, что имя ввёл пользователь: после смены языка
                // такое поле переводить уже нельзя.
                if (SetProperty(ref _newModpackName, value))
                {
                    _newModpackNameEdited = !string.IsNullOrWhiteSpace(value);
                }
            }
        }

        public string NewModpackVersion
        {
            get => _newModpackVersion;
            set => SetProperty(ref _newModpackVersion, value);
        }

        public string NewModpackLoader
        {
            get => _newModpackLoader;
            set
            {
                if (SetProperty(ref _newModpackLoader, value))
                {
                    OnPropertyChanged(nameof(IsFabricSelected));
                }
            }
        }

        public bool IsFabricSelected => string.Equals(NewModpackLoader, "Fabric", StringComparison.OrdinalIgnoreCase);

        public bool InstallSodium
        {
            get => _installSodium;
            set => SetProperty(ref _installSodium, value);
        }

        public bool InstallIris
        {
            get => _installIris;
            set => SetProperty(ref _installIris, value);
        }

        public ImageBrush? CustomBackgroundBrush
        {
            get => _customBackgroundBrush;
            set => SetProperty(ref _customBackgroundBrush, value);
        }

        public bool IsAddServerOverlayVisible
        {
            get => _isAddServerOverlayVisible;
            set => SetProperty(ref _isAddServerOverlayVisible, value);
        }

        public string NewServerName
        {
            get => _newServerName;
            set => SetProperty(ref _newServerName, value);
        }

        public string NewServerIp
        {
            get => _newServerIp;
            set => SetProperty(ref _newServerIp, value);
        }

        public AsyncRelayCommand PlayCommand { get; }
        public AsyncRelayCommand<string> ConnectServerCommand { get; }
        public RelayCommand ToggleThemeCommand { get; }
        public RelayCommand OpenModpackOverlayCommand { get; }
        public RelayCommand CloseModpackOverlayCommand { get; }
        public AsyncRelayCommand CreateModpackCommand { get; }
        public RelayCommand OpenShortcutOverlayCommand { get; }
        public RelayCommand CloseShortcutOverlayCommand { get; }
        public RelayCommand CreateShortcutCommand { get; }
        public RelayCommand ImportZipModpackCommand { get; }
        public RelayCommand DeleteModpackCommand { get; }
        public RelayCommand<string> OpenQuickFolderCommand { get; }
        public RelayCommand<string> CopyServerIpCommand { get; }
        public AsyncRelayCommand RefreshServersCommand { get; }
        public RelayCommand<ServerItem> DeleteServerCommand { get; }
        public RelayCommand OpenAddServerOverlayCommand { get; }
        public RelayCommand CloseAddServerOverlayCommand { get; }
        public AsyncRelayCommand AddServerCommand { get; }
        public RelayCommand ExitCommand { get; }

        public MainViewModel() : this(
            SettingsService.Instance,
            MinecraftLaunchService.Instance,
            ServerStatusService.Instance,
            NewsService.Instance,
            MinecraftLauncher.Services.DiscordService.Instance,
            MinecraftLauncher.Services.UpdateService.Instance,
            AudioService.Instance,
            ThemeService.Instance,
            ToastService.Instance)
        {
        }

        public MainViewModel(
            ISettingsService settingsService,
            IMinecraftLaunchService launchService,
            IServerStatusService serverStatusService,
            INewsService newsService,
            IDiscordService discordService,
            IUpdateService updateService,
            IAudioService audioService,
            IThemeService themeService,
            IToastService toastService)
        {
            _settingsService = settingsService;
            _launchService = launchService;
            _serverStatusService = serverStatusService;
            _newsService = newsService;
            _discordService = discordService;
            _updateService = updateService;
            _audioService = audioService;
            _themeService = themeService;
            _toastService = toastService;

            // Строка прогресса и пункт создания сборки хранятся в полях,
            // поэтому за сменой языка нужно следить вручную: DynamicResource
            // на них не действует.
            LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

            ExitCommand = new RelayCommand(() => RequestClose?.Invoke());

            PlayCommand = new AsyncRelayCommand(ExecutePlayAsync);
            ConnectServerCommand = new AsyncRelayCommand<string>(ExecuteConnectServerAsync);
            ToggleThemeCommand = new RelayCommand(ExecuteToggleTheme);
            OpenModpackOverlayCommand = new RelayCommand(() => IsModpackOverlayVisible = true);
            CloseModpackOverlayCommand = new RelayCommand(() => IsModpackOverlayVisible = false);
            CreateModpackCommand = new AsyncRelayCommand(ExecuteCreateModpackAsync);
            OpenShortcutOverlayCommand = new RelayCommand(() =>
            {
                ShortcutVersion = SelectedVersion;
                IsShortcutOverlayVisible = true;
            });
            CloseShortcutOverlayCommand = new RelayCommand(() => IsShortcutOverlayVisible = false);
            CreateShortcutCommand = new RelayCommand(ExecuteCreateShortcut);
            ImportZipModpackCommand = new RelayCommand(ExecuteImportZipModpack);
            DeleteModpackCommand = new RelayCommand(ExecuteDeleteModpack);
            OpenQuickFolderCommand = new RelayCommand<string>(ExecuteOpenQuickFolder);

            CopyServerIpCommand = new RelayCommand<string>(ip =>
            {
                if (!string.IsNullOrWhiteSpace(ip))
                {
                    Clipboard.SetText(ip);
                    _toastService.ShowSuccess(_loc.Format("Str_Server_IpCopied", ip), _loc.GetString("Str_T_Server"));
                }
            });

            RefreshServersCommand = new AsyncRelayCommand(async () =>
            {
                await RefreshServersAsync();
                _toastService.ShowSuccess(_loc.GetString("Str_Server_StatusUpdated"), _loc.GetString("Str_T_Monitoring"));
            });

            DeleteServerCommand = new RelayCommand<ServerItem>(server =>
            {
                if (server == null || string.IsNullOrWhiteSpace(server.Ip)) return;

                if (QMessageBoxWindow.Show(_loc.Format("Str_Server_DeleteQuestion", server.Name), _loc.GetString("Str_T_DeleteConfirm"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    _serverStatusService.RemoveServer(_settingsService.Settings.GamePath, server.Ip);
                    Servers.Remove(server);
                    _toastService.ShowInfo(_loc.Format("Str_Server_Removed", server.Name), _loc.GetString("Str_T_Server"));
                }
            });

            OpenAddServerOverlayCommand = new RelayCommand(() =>
            {
                NewServerName = "";
                NewServerIp = "";
                IsAddServerOverlayVisible = true;
            });

            CloseAddServerOverlayCommand = new RelayCommand(() => IsAddServerOverlayVisible = false);

            AddServerCommand = new AsyncRelayCommand(async () =>
            {
                if (string.IsNullOrWhiteSpace(NewServerIp))
                {
                    _toastService.ShowWarning(_loc.GetString("Str_Server_IpRequired"), _loc.GetString("Str_T_Server"));
                    return;
                }

                string name = string.IsNullOrWhiteSpace(NewServerName) ? NewServerIp.Trim() : NewServerName.Trim();
                string ip = NewServerIp.Trim();

                _serverStatusService.AddServer(_settingsService.Settings.GamePath, name, ip);
                IsAddServerOverlayVisible = false;
                await RefreshServersAsync();
                _toastService.ShowSuccess(_loc.Format("Str_Server_Added", name), _loc.GetString("Str_T_Server"));
            });

            _launchService.FileProgressChanged += OnFileProgressChanged;
            _launchService.ByteProgressChanged += OnByteProgressChanged;
        }

        public async Task InitializeAsync()
        {
            var settings = _settingsService.Load();
            _discordService.StartRpc(settings.EnableDiscordRpc);
            ApplyCustomWallpaper();

            await Task.Run(() => _serverStatusService.InjectServersIfEnabled(settings.GamePath, settings.AutoAddServers));

            // Обход всех версий Fabric ушёл в Task.Run (иначе старт лаунчера
            // подвисал на большой папке игры).
            await _launchService.InitializeAsync(settings.GamePath);

            await LoadAccountsAsync();
            await LoadVersionsAsync();
            await RefreshServersAsync();
            await RefreshNewsAsync();

            // Таймеры гасят сами себя на время работы, иначе тики накладывались:
// async-лямбда возвращает управление в message pump на первом await, поэтому
// следующий тик мог запуститься, пока предыдущий ещё шёл. В итоге дублировались
// пинги и последний результат выигрывал у первого.
_newsTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
_newsTimer.Tick += async (_, _) => await RunExclusiveAsync(RefreshNewsAsync, _newsRefreshRunning);
_newsTimer.Start();

_serverTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
_serverTimer.Tick += async (_, _) => await RunExclusiveAsync(RefreshServersAsync, _serverRefreshRunning);
_serverTimer.Start();

            _ = Task.Run(async () =>
            {
                try
                {
                    var settings = _settingsService.Settings;
                    if (!settings.CheckUpdatesOnStartup) return;

                    var release = await _updateService.CheckForUpdatesAsync(isManual: false);
                    if (release != null && release.HasUpdate)
                    {
                        if (!string.IsNullOrEmpty(settings.SkippedVersion) &&
                            settings.SkippedVersion.Equals(release.TagName, StringComparison.OrdinalIgnoreCase))
                        {
                            return;
                        }

                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            var updateWindow = new Views.Windows.UpdateWindow(release)
                            {
                                Owner = Application.Current.MainWindow
                            };
                            updateWindow.ShowDialog();
                        });
                    }
                }
                catch { }
            });

            CheckForQuickPlay();
        }

        public void ApplyCustomWallpaper()
        {
            var settings = _settingsService.Settings;
            if (!string.IsNullOrWhiteSpace(settings.CustomWallpaperPath) && File.Exists(settings.CustomWallpaperPath))
            {
                try
                {
                    var bitmap = new BitmapImage(new Uri(settings.CustomWallpaperPath, UriKind.Absolute));
                    CustomBackgroundBrush = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
                    return;
                }
                catch { }
            }

            try
            {
                CustomBackgroundBrush = new ImageBrush(new BitmapImage(new Uri("pack://application:,,,/bg.png", UriKind.Absolute)))
                {
                    Stretch = Stretch.UniformToFill
                };
            }
            catch { }
        }

        public async Task LoadAccountsAsync()
        {
            Accounts.Clear();
            var settings = _settingsService.Settings;

            foreach (var acc in settings.Accounts)
            {
                acc.AvatarImage = AvatarHelper.GetDefaultAvatar();
                Accounts.Add(acc);

                // Ошибка загрузки аватара наблюдается явно (иначе терялась в
                // UnobservedTaskException и молча оставляла дефолтную картинку).
                // Присваивание через "_ =" подавляет предупреждение CS4014.
                _ = LoadAvatarAsync(acc);
            }

            SelectedAccount = Accounts.FirstOrDefault(a => a.Nickname == settings.ActiveAccount)
                              ?? Accounts.FirstOrDefault();
        }

        private async Task LoadAvatarAsync(AccountProfile acc)
        {
            try
            {
                var img = await AvatarHelper.GetAvatarAsync(acc.Nickname, acc.Uuid);

                if (img == null || _disposed) return;

                _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    acc.AvatarImage = img;
                }));
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("AvatarLoad", $"Failed to load avatar for '{acc.Nickname}'", ex);
            }
        }

        public async Task LoadVersionsAsync()
        {
            ProgressText = _loc.GetString("Str_Progress_FetchingVersions");
            Versions.Clear();
            VanillaVersions.Clear();
            var settings = _settingsService.Settings;

            _newPackShortcut = _loc.GetString("Str_Progress_NewPackShortcut");
            RegisterCreateModpackEntryText(_newPackShortcut);
            Versions.Add(_newPackShortcut);

            foreach (var pack in settings.Modpacks)
            {
                Versions.Add($"⭐ {pack.Name} ({pack.Loader})");
            }

            try
            {
                var releases = await _launchService.GetAllReleaseVersionsAsync();
                foreach (var rel in releases)
                {
                    VanillaVersions.Add(rel);
                    Versions.Add(rel);
                }

                if (VanillaVersions.Count > 0 && (string.IsNullOrEmpty(NewModpackVersion) || NewModpackVersion == "1.20.1"))
                {
                    NewModpackVersion = VanillaVersions[0];
                }
            }
            catch { }

            if (Versions.Count > 1)
            {
                string last = settings.LastSelectedVersion;
                string? matchingVer = null;
                if (!string.IsNullOrEmpty(last))
                {
                    matchingVer = Versions.FirstOrDefault(v =>
                        v.Equals(last, StringComparison.OrdinalIgnoreCase) ||
                        v.Replace("⭐", "").Trim().Equals(last.Replace("⭐", "").Trim(), StringComparison.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrEmpty(matchingVer))
                {
                    SelectedVersion = matchingVer;
                }
                else
                {
                    SelectedVersion = Versions[1];
                }
                ProgressText = _loc.GetString("Str_Progress_Ready");
            }
        }

        public async Task RefreshNewsAsync()
        {
            var posts = await _newsService.FetchTelegramNewsAsync();
            Application.Current.Dispatcher.Invoke(() =>
            {
                NewsPosts.Clear();
                foreach (var p in posts) NewsPosts.Add(p);
                OnPropertyChanged(nameof(HasNews));
            });
        }

        public async Task RefreshServersAsync()
        {
            var settings = _settingsService.Settings;
            var list = await _serverStatusService.LoadAndPingServersAsync(settings.GamePath);
            Application.Current.Dispatcher.Invoke(() =>
            {
                Servers.Clear();
                foreach (var s in list) Servers.Add(s);
                OnPropertyChanged(nameof(HasServers));
            });
        }

        private void OnFileProgressChanged(int progressed, int total)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (total > 0)
                {
                    ProgressText = _loc.Format("Str_Progress_Preparing", progressed, total);
                    if (IsActionOverlayVisible)
                    {
                        ActionOverlayText = _loc.Format("Str_Progress_PreparingResources", progressed, total);
                    }
                }
            });
        }

        private void OnByteProgressChanged(long progressed, long total, double ratio, string speed)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                ProgressFillRatio = ratio;
                if (IsActionOverlayVisible && !string.IsNullOrEmpty(speed))
                {
                    ActionOverlayText = _loc.Format("Str_Progress_Components", speed);
                }

                if (total > 0)
                {
                    int percent = (int)(ratio * 100);
                    double progMb = progressed / 1048576.0;
                    double totMb = total / 1048576.0;
                    string progStr = progMb >= 1024
                        ? _loc.Format("Str_Size_Gb", $"{(progMb / 1024.0):F1}")
                        : _loc.Format("Str_Size_Mb", $"{progMb:F1}");
                    string totStr = totMb >= 1024
                        ? _loc.Format("Str_Size_Gb", $"{(totMb / 1024.0):F1}")
                        : _loc.Format("Str_Size_Mb", $"{totMb:F1}");
                    ProgressText = $"{progStr} / {totStr} ({percent}%) • {speed}";
                }
            });
        }

        private async Task ExecutePlayAsync()
        {
            if (string.IsNullOrWhiteSpace(SelectedVersion) || IsCreateModpackEntry(SelectedVersion))
            {
                IsModpackOverlayVisible = true;
                return;
            }

            await StartGameAsync(SelectedVersion, null);
        }

        private async Task ExecuteConnectServerAsync(string? serverIp)
        {
            if (string.IsNullOrWhiteSpace(serverIp)) return;
            string ver = string.IsNullOrWhiteSpace(SelectedVersion) || IsCreateModpackEntry(SelectedVersion)
                ? "1.20.1"
                : SelectedVersion;

            await StartGameAsync(ver, serverIp);
        }

        /// <summary>
        /// Читает stdout/stderr дочернего процесса.
        ///
        /// Это обязательно, а не украшение: MinecraftLaunchEngine включает
        /// RedirectStandardOutput/Error и вызывает BeginOutputReadLine, но ни один
        /// обработчик не был привязан, если Discord RPC выключен. Буфер пайпа (64 КБ)
        /// переполнялся, и процесс игры блокировался навсегда, при этом лаунчер
        /// сообщал об успешном запуске.
        /// </summary>
        private void AttachProcessOutputHandlers(Process process)
        {
            lock (_outputHandlersGate)
            {
                if (_attachedProcess == process) return;

                DetachProcessOutputHandlers(process);

                DataReceivedEventHandler onOut = (_, e) => ProcessOutputLine?.Invoke(e.Data);
                DataReceivedEventHandler onErr = (_, e) => ProcessOutputLine?.Invoke(e.Data);

                process.OutputDataReceived += onOut;
                process.ErrorDataReceived += onErr;

                _outputHandlers = (onOut, onErr);
                _attachedProcess = process;

                // MinecraftLaunchEngine уже вызвал Begin*ReadLine, но до привязки
                // хендлеров, поэтому ранний вывод (то есть первоначальный краш-лог)
                // терялся. Перезапускаем чтение явно.
                try
                {
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                }
                catch (InvalidOperationException)
                {
                    // Чтение уже было запущено движком — это не ошибка.
                }
            }
        }

        private void DetachProcessOutputHandlers(Process? process)
        {
            lock (_outputHandlersGate)
            {
                if (process == null)
                {
                    _outputHandlers = null;
                    _attachedProcess = null;
                    return;
                }
                if (_outputHandlers is { } handlers && ReferenceEquals(_attachedProcess, process))
                {
                    process.OutputDataReceived -= handlers.Item1;
                    process.ErrorDataReceived -= handlers.Item2;
                }

                _outputHandlers = null;
                _attachedProcess = null;
            }
        }

        private async Task StartGameAsync(string version, string? serverIp)
        {
            var settings = _settingsService.Settings;
            var account = SelectedAccount ?? Accounts.FirstOrDefault();

            if (account == null)
            {
                _toastService.ShowWarning(_loc.GetString("Str_Account_Required"), _loc.GetString("Str_T_Account"));
                return;
            }

            // Java Compatibility Guard.
            // Раньше проверка была синхронной: она спавнила процесс и ждала его
            // на UI-потоке (до 3 секунд на каждое нажатие Play), плюс читала
            // stderr до stdout и могла зависнуть намертво.
            var compat = await JavaCompatibilityService.Instance.CheckCompatibilityAsync(version, settings.JavaPath);
            if (!compat.IsCompatible)
            {
                var choice = QMessageBoxWindow.Show(
                    $"{compat.Message}\n\n{_loc.Format("Str_Launch_JavaMismatchPrompt", compat.RequiredVersion)}",
                    _loc.GetString("Str_T_JavaCheck"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (choice != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            IsActionOverlayVisible = true;
            ActionOverlayText = _loc.GetString("Str_Launch_Preparing");
            _discordService.SetLaunchingState(version, _loc.GetString("Str_Launch_Preparing"));

            try
            {
                var process = await _launchService.LaunchGameAsync(
                    version,
                    account,
                    settings,
                    serverIp,
                    status => Application.Current.Dispatcher.Invoke(() =>
                    {
                        ActionOverlayText = status;
                        _discordService.SetLaunchingState(version, status);
                    }));

                _audioService.PlayLaunchSound(settings.EnableUiSounds);

                string cleanVer = version.Replace("⭐", "").Trim();
                string packName = cleanVer;
                if (packName.Contains(" (")) packName = packName.Substring(0, packName.LastIndexOf(" (")).Trim();

                var pack = settings.Modpacks.Find(p => 
                    p.Name.Equals(packName, StringComparison.OrdinalIgnoreCase) ||
                    $"{p.Name} ({p.Loader})".Equals(cleanVer, StringComparison.OrdinalIgnoreCase));

                string effectiveGamePath = (!string.IsNullOrWhiteSpace(pack?.FolderPath) && Directory.Exists(pack.FolderPath))
                    ? pack.FolderPath
                    : Path.Combine(settings.GamePath, "instances", packName);

                if (!Directory.Exists(effectiveGamePath))
                {
                    effectiveGamePath = settings.GamePath;
                }

                _discordService.StartGameTracking(version, effectiveGamePath, settings.HideServerIp, process, serverIp);

                GameLaunched?.Invoke(process);

                DateTime startTime = DateTime.Now;

                // Наблюдение за процессом игры живёт отдельно от обработчиков
                // вывода: раньше DiscordService цеплял OutputDataReceived только
                // если RPC был включён, и при выключенном RPC пайпы никто не читал.
                AttachProcessOutputHandlers(process);

                _ = Task.Run(() =>
                {
                    try
                    {
                        process.WaitForExit();
                        int exitCode = process.ExitCode;
                        long minutes = (long)(DateTime.Now - startTime).TotalMinutes;

                        if (_settingsService.Settings.EnableDiscordRpc)
                        {
                            _discordService.StopGameTracking();
                        }

                        if (pack != null)
                        {
                            // PlaytimeMinutes и LaunchCount — наблюдаемое свойство,
                            // поэтому мутируем на UI-потоке: раньше это делалось
                            // из пула и PropertyChanged улетал не в тот поток.
                            long added = Math.Max(0, minutes);

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                pack.PlaytimeMinutes += added;
                                pack.LaunchCount += 1;
                            });

                            _settingsService.Save();
                        }

                        if (exitCode != 0)
                        {
                            var crash = CrashAnalyzer.AnalyzeLatestCrash(effectiveGamePath);
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                RequestShow?.Invoke();
                                _toastService.ShowError($"{crash.Summary}\n\n{crash.Recommendation}", crash.Title);
                            });
                        }

                        // Хендлеры вывода и сам Process больше не нужны: без Dispose
                        // на каждый запуск игры оставались живые хендлы.
                        DetachProcessOutputHandlers(process);
                        process.Dispose();
                    }
                    catch (Exception ex)
                    {
                        CrashLogWriter.Write("GameExitWatcher", "Failed while handling game process exit", ex);
                    }
                });

                if (settings.CloseOnLaunch)
                {
                    RequestHide?.Invoke();
                }
            }
            catch (Exception ex)
            {
                ProgressFillRatio = 0.0;
                ProgressText = _loc.GetString("Str_Progress_Ready");
                _toastService.ShowError(_loc.Format("Str_T_ErrorLaunch", ex.Message), _loc.GetString("Str_T_Error"));
                _discordService.SetMenuState(SelectedVersion);
            }
            finally
            {
                IsActionOverlayVisible = false;
                if (ProgressText.StartsWith(_progressPreparingPrefix, StringComparison.Ordinal))
                {
                    ProgressFillRatio = 0.0;
                    ProgressText = _loc.GetString("Str_Progress_Ready");
                }
            }
        }

        private void ExecuteToggleTheme()
        {
            _themeService.ToggleTheme();
        }

        private async Task ExecuteCreateModpackAsync()
        {
            try
            {
                string name = (NewModpackName ?? "").Trim();
                string gameVer = !string.IsNullOrWhiteSpace(NewModpackVersion) ? NewModpackVersion.Trim() : "1.20.1";
                string loader = string.IsNullOrWhiteSpace(NewModpackLoader) ? "Vanilla" : NewModpackLoader.Trim();

                if (string.IsNullOrWhiteSpace(name))
                {
                    _toastService.ShowWarning(_loc.GetString("Str_Pack_NameRequired"), _loc.GetString("Str_T_PackCreateTitle"));
                    return;
                }

                var settings = _settingsService.Settings;
                if (settings.Modpacks.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    _toastService.ShowError(_loc.GetString("Str_Pack_NameExists"), _loc.GetString("Str_T_Error"));
                    return;
                }

                string instancesPath = Path.Combine(settings.GamePath, "instances", name);
                Directory.CreateDirectory(instancesPath);

                if (loader == "Fabric" || loader == "Quilt")
                {
                    string modsPath = Path.Combine(instancesPath, "mods");
                    Directory.CreateDirectory(modsPath);

                    if (InstallSodium)
                    {
                        await _launchService.DownloadModrinthModAsync("sodium", gameVer, modsPath);
                        await _launchService.DownloadModrinthModAsync("lithium", gameVer, modsPath);
                        await _launchService.DownloadModrinthModAsync("ferrite-core", gameVer, modsPath);
                    }
                    if (InstallIris)
                    {
                        await _launchService.DownloadModrinthModAsync("iris", gameVer, modsPath);
                    }
                }

                var newPack = new ModpackProfile
                {
                    Name = name,
                    GameVersion = gameVer,
                    Version = gameVer,
                    Loader = loader,
                    FolderPath = instancesPath
                };

                settings.Modpacks.Add(newPack);
                _settingsService.Save(settings);

                IsModpackOverlayVisible = false;
                await LoadVersionsAsync();
                SelectedVersion = $"⭐ {newPack.Name} ({newPack.Loader})";
                _toastService.ShowSuccess(_loc.Format("Str_Pack_Created", name), _loc.GetString("Str_T_PacksTitle"));
            }
            catch (Exception ex)
            {
                _toastService.ShowError(_loc.Format("Str_Pack_CreateError", ex.Message), _loc.GetString("Str_T_Error"));
            }
        }

        private void ExecuteCreateShortcut()
        {
            if (string.IsNullOrWhiteSpace(ShortcutVersion) || string.IsNullOrWhiteSpace(ShortcutIp))
            {
                _toastService.ShowWarning(_loc.GetString("Str_Shortcut_FieldsRequired"), _loc.GetString("Str_T_Shortcut"));
                return;
            }

            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string cleanName = ShortcutVersion.Replace("⭐", "").Replace(":", "_").Trim();
                string shortcutFile = Path.Combine(desktop, $"Minecraft ({cleanName}).lnk");

                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    dynamic shell = Activator.CreateInstance(shellType)!;
                    dynamic shortcut = shell.CreateShortcut(shortcutFile);
                    shortcut.TargetPath = exePath;
                    shortcut.Arguments = $"-quickplay \"{ShortcutVersion}\" \"{ShortcutIp.Trim()}\"";
                    shortcut.IconLocation = $"{exePath},0";
                    shortcut.WorkingDirectory = Path.GetDirectoryName(exePath) ?? "";
                    shortcut.Save();
                }

                IsShortcutOverlayVisible = false;
                _toastService.ShowSuccess(_loc.GetString("Str_Shortcut_Created"), _loc.GetString("Str_T_Done"));
            }
            catch (Exception ex)
            {
                _toastService.ShowError(_loc.Format("Str_Shortcut_Error", ex.Message), _loc.GetString("Str_T_Error"));
            }
        }

        private async void ExecuteImportZipModpack()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Modpack files (*.mrpack;*.zip)|*.mrpack;*.zip|Modrinth Pack (*.mrpack)|*.mrpack|Zip Archive (*.zip)|*.zip",
                Title = _loc.GetString("Str_Dialog_ImportPack")
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    _toastService.ShowInfo(_loc.GetString("Str_Import_Progress"), _loc.GetString("Str_T_ImportTitle"));

                    // Общий сервис импорта вместо второй копии этой же логики.
                    var profile = await ModpackImportService.Instance.ImportAsync(
                        dlg.FileName,
                        _settingsService.Settings.GamePath);

                    _settingsService.Settings.Modpacks.Add(profile);
                    _settingsService.Save();

                    await LoadVersionsAsync();
                    IsModpackOverlayVisible = false;
                    SelectedVersion = $"⭐ {profile.Name} ({profile.Loader})";
                    _toastService.ShowSuccess(_loc.Format("Str_Import_Done", profile.Name), _loc.GetString("Str_T_ImportTitle"));
                }
                catch (Exception ex)
                {
                    CrashLogWriter.Write("ModpackImport", $"Failed to import '{dlg.FileName}'", ex);
                    _toastService.ShowError(_loc.Format("Str_Import_Error", ex.Message), _loc.GetString("Str_T_Error"));
                }
            }
        }

        private void ExecuteDeleteModpack()
        {
            if (string.IsNullOrWhiteSpace(SelectedVersion) || !SelectedVersion.StartsWith("⭐ "))
            {
                _toastService.ShowWarning(_loc.GetString("Str_Delete_PackRequired"), _loc.GetString("Str_T_Delete"));
                return;
            }

            // Раньше вычислялось LastIndexOf('(') - 3 без проверки: при записи вида
            // "⭐ Сборка" без скобок LastIndexOf возвращает -1, и Substring(2, -4)
            // бросал ArgumentOutOfRangeException прямо в обработчик кнопки.
            int openParen = SelectedVersion.IndexOf('(');
            string packName = openParen > 2
                ? SelectedVersion[2..openParen].Trim()
                : SelectedVersion[2..].Trim();

            if (QMessageBoxWindow.Show(_loc.Format("Str_Pack_DeleteQuestion", packName), _loc.GetString("Str_T_Confirmation"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                var settings = _settingsService.Settings;
                var pack = settings.Modpacks.Find(p => p.Name == packName);
                if (pack != null)
                {
                    try
                    {
                        if (Directory.Exists(pack.FolderPath))
                        {
                            Directory.Delete(pack.FolderPath, true);
                        }
                    }
                    catch { }

                    settings.Modpacks.Remove(pack);
                    _settingsService.Save(settings);
                    _ = LoadVersionsAsync();
                    _toastService.ShowSuccess(_loc.Format("Str_Delete_Done", packName), _loc.GetString("Str_T_Success"));
                }
            }
        }

        private void ExecuteOpenQuickFolder(string? subFolder)
        {
            var settings = _settingsService.Settings;
            string targetDir = settings.GamePath;

            if (!string.IsNullOrEmpty(SelectedVersion) && SelectedVersion.StartsWith("⭐"))
            {
                string packName = SelectedVersion.Replace("⭐", "").Trim();
                if (packName.Contains(" (")) packName = packName.Substring(0, packName.LastIndexOf(" (")).Trim();

                var modpack = settings.Modpacks.Find(p => p.Name == packName);
                if (modpack != null && Directory.Exists(modpack.FolderPath))
                {
                    targetDir = modpack.FolderPath;
                }
            }

            if (!string.IsNullOrEmpty(subFolder))
            {
                targetDir = Path.Combine(targetDir, subFolder);
            }

            Directory.CreateDirectory(targetDir);
            try
            {
                Process.Start("explorer.exe", targetDir);
            }
            catch { }
        }

        private void CheckForQuickPlay()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 1; i < args.Length; i++)
                {
                    if (args[i] == "-quickplay" && i + 2 < args.Length)
                    {
                        string targetVer = args[i + 1];
                        string serverIp = args[i + 2];

                        SelectedVersion = targetVer;
                        _ = StartGameAsync(targetVer, serverIp);
                        return;
                    }
                }
            }
            catch { }
        }
    }
}
