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
        private readonly IMinecraftLaunchService _launchService;
        private readonly IServerStatusService _serverStatusService;
        private readonly INewsService _newsService;
        private readonly IDiscordService _discordService;
        private readonly IUpdateService _updateService;
        private readonly IAudioService _audioService;
        private readonly IThemeService _themeService;
        private readonly IToastService _toastService;

        private AccountProfile? _selectedAccount;
        private string _selectedVersion = "";
        private string _progressText = "Готов к запуску";
        private double _progressFillRatio = 0.0;
        private bool _isActionOverlayVisible;
        private string _actionOverlayText = "Обработка...";
        private bool _isShortcutOverlayVisible;
        private string _shortcutVersion = "";
        private string _shortcutIp = "mc.hypixel.net";
        private bool _isModpackOverlayVisible;
        private string _newModpackName = "Моя сборка";
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
                    if (!string.IsNullOrEmpty(value) && !value.Contains("Создать новую сборку"))
                    {
                        var settings = _settingsService.Settings;
                        settings.LastSelectedVersion = value;
                        _settingsService.Save(settings);

                        _discordService.UpdateSelectedVersion(value);
                    }
                }
            }
        }

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
            set => SetProperty(ref _newModpackName, value);
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
                    _toastService.ShowSuccess($"IP адрес '{ip}' скопирован в буфер", "Сервер");
                }
            });

            RefreshServersCommand = new AsyncRelayCommand(async () =>
            {
                await RefreshServersAsync();
                _toastService.ShowSuccess("Статус серверов обновлен", "Мониторинг");
            });

            DeleteServerCommand = new RelayCommand<ServerItem>(server =>
            {
                if (server == null || string.IsNullOrWhiteSpace(server.Ip)) return;

                if (QMessageBoxWindow.Show($"Удалить сервер '{server.Name}' из списка?", "Удаление сервера", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    _serverStatusService.RemoveServer(_settingsService.Settings.GamePath, server.Ip);
                    Servers.Remove(server);
                    _toastService.ShowInfo($"Сервер '{server.Name}' удален", "Сервер");
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
                    _toastService.ShowWarning("Введите IP-адрес или домен сервера.", "Сервер");
                    return;
                }

                string name = string.IsNullOrWhiteSpace(NewServerName) ? NewServerIp.Trim() : NewServerName.Trim();
                string ip = NewServerIp.Trim();

                _serverStatusService.AddServer(_settingsService.Settings.GamePath, name, ip);
                IsAddServerOverlayVisible = false;
                await RefreshServersAsync();
                _toastService.ShowSuccess($"Сервер '{name}' добавлен в список!", "Сервер");
            });

            _launchService.FileProgressChanged += OnFileProgressChanged;
            _launchService.ByteProgressChanged += OnByteProgressChanged;
        }

        public async Task InitializeAsync()
        {
            var settings = _settingsService.Load();
            _discordService.StartRpc(settings.EnableDiscordRpc);
            ApplyCustomWallpaper();

            _serverStatusService.InjectServersIfEnabled(settings.GamePath, settings.AutoAddServers);
            _launchService.Initialize(settings.GamePath);

            await LoadAccountsAsync();
            await LoadVersionsAsync();
            await RefreshServersAsync();
            await RefreshNewsAsync();

            _newsTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
            _newsTimer.Tick += async (_, _) => await RefreshNewsAsync();
            _newsTimer.Start();

            _serverTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
            _serverTimer.Tick += async (_, _) => await RefreshServersAsync();
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

                _ = Task.Run(async () =>
                {
                    var img = await AvatarHelper.GetAvatarAsync(acc.Nickname, acc.Uuid);
                    if (img != null)
                    {
                        Application.Current.Dispatcher.Invoke(() => acc.AvatarImage = img);
                    }
                });
            }

            SelectedAccount = Accounts.FirstOrDefault(a => a.Nickname == settings.ActiveAccount)
                              ?? Accounts.FirstOrDefault();
        }

        public async Task LoadVersionsAsync()
        {
            ProgressText = "Получение списка версий...";
            Versions.Clear();
            VanillaVersions.Clear();
            var settings = _settingsService.Settings;

            Versions.Add("+ Создать новую сборку...");

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
                ProgressText = "Готов к запуску";
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
                    ProgressText = $"Подготовка ({progressed}/{total})";
                    if (IsActionOverlayVisible)
                    {
                        ActionOverlayText = $"Подготовка ресурсов ({progressed}/{total})...";
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
                    ActionOverlayText = $"Загрузка компонентов • {speed}";
                }

                if (total > 0)
                {
                    int percent = (int)(ratio * 100);
                    double progMb = progressed / 1048576.0;
                    double totMb = total / 1048576.0;
                    string progStr = progMb >= 1024 ? $"{(progMb / 1024.0):F1} ГБ" : $"{progMb:F1} МБ";
                    string totStr = totMb >= 1024 ? $"{(totMb / 1024.0):F1} ГБ" : $"{totMb:F1} МБ";
                    ProgressText = $"{progStr} / {totStr} ({percent}%) • {speed}";
                }
            });
        }

        private async Task ExecutePlayAsync()
        {
            if (string.IsNullOrWhiteSpace(SelectedVersion) || SelectedVersion.Contains("Создать новую сборку"))
            {
                IsModpackOverlayVisible = true;
                return;
            }

            await StartGameAsync(SelectedVersion, null);
        }

        private async Task ExecuteConnectServerAsync(string? serverIp)
        {
            if (string.IsNullOrWhiteSpace(serverIp)) return;
            string ver = string.IsNullOrWhiteSpace(SelectedVersion) || SelectedVersion.Contains("Создать новую сборку")
                ? "1.20.1"
                : SelectedVersion;

            await StartGameAsync(ver, serverIp);
        }

        private async Task StartGameAsync(string version, string? serverIp)
        {
            var settings = _settingsService.Settings;
            var account = SelectedAccount ?? Accounts.FirstOrDefault();

            if (account == null)
            {
                _toastService.ShowWarning("Пожалуйста, добавьте или выберите аккаунт в настройках.", "Аккаунт");
                return;
            }

            // Java Compatibility Guard
            var compat = JavaCompatibilityService.Instance.CheckCompatibility(version, settings.JavaPath);
            if (!compat.IsCompatible)
            {
                var choice = QMessageBoxWindow.Show(
                    $"{compat.Message}\n\nРекомендуется использовать Java {compat.RequiredVersion}.\nВы хотите продолжить запуск?",
                    "Проверка совместимости Java",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (choice != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            IsActionOverlayVisible = true;
            ActionOverlayText = "Подготовка к запуску...";
            _discordService.SetLaunchingState(version, "Подготовка к запуску...");

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
                _ = Task.Run(() =>
                {
                    try
                    {
                        process.WaitForExit();
                        int exitCode = process.ExitCode;
                        long minutes = (long)(DateTime.Now - startTime).TotalMinutes;

                        var s = _settingsService.Load();
                        if (s.EnableDiscordRpc)
                        {
                            _discordService.StopGameTracking();
                        }

                        if (pack != null)
                        {
                            pack.PlaytimeMinutes += Math.Max(0, minutes);
                            pack.LaunchCount += 1;
                            _settingsService.Save(s);
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
                    }
                    catch { }
                });

                if (settings.CloseOnLaunch)
                {
                    RequestHide?.Invoke();
                }
            }
            catch (Exception ex)
            {
                ProgressFillRatio = 0.0;
                ProgressText = "Готов к запуску";
                _toastService.ShowError($"Ошибка запуска: {ex.Message}", "Ошибка");
                _discordService.SetMenuState(SelectedVersion);
            }
            finally
            {
                IsActionOverlayVisible = false;
                if (ProgressText.StartsWith("Подготовка"))
                {
                    ProgressFillRatio = 0.0;
                    ProgressText = "Готов к запуску";
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
                    _toastService.ShowWarning("Введите название сборки!", "Создание сборки");
                    return;
                }

                var settings = _settingsService.Settings;
                if (settings.Modpacks.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    _toastService.ShowError("Сборка с таким названием уже существует.", "Ошибка");
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
                _toastService.ShowSuccess($"Сборка '{name}' создана!", "Сборки");
            }
            catch (Exception ex)
            {
                _toastService.ShowError($"Ошибка при создании сборки: {ex.Message}", "Ошибка");
            }
        }

        private void ExecuteCreateShortcut()
        {
            if (string.IsNullOrWhiteSpace(ShortcutVersion) || string.IsNullOrWhiteSpace(ShortcutIp))
            {
                _toastService.ShowWarning("Заполните все поля для создания ярлыка.", "Ярлык");
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
                _toastService.ShowSuccess("Ярлык быстрого входа создан на рабочем столе!", "Готово");
            }
            catch (Exception ex)
            {
                _toastService.ShowError($"Ошибка создания ярлыка: {ex.Message}", "Ошибка");
            }
        }

        private async void ExecuteImportZipModpack()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Modpack files (*.mrpack;*.zip)|*.mrpack;*.zip|Modrinth Pack (*.mrpack)|*.mrpack|Zip Archive (*.zip)|*.zip",
                Title = "Импорт сборки"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var settings = _settingsService.Settings;
                    string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();

                    if (ext == ".mrpack")
                    {
                        _toastService.ShowInfo("Импорт Modrinth сборки...", "Импорт");
                        var profile = await MrPackInstaller.InstallMrPackAsync(dlg.FileName, settings.GamePath);
                        settings.Modpacks.Add(profile);
                        _settingsService.Save(settings);

                        await LoadVersionsAsync();
                        IsModpackOverlayVisible = false;
                        SelectedVersion = $"⭐ {profile.Name} ({profile.Loader})";
                        _toastService.ShowSuccess($"Сборка '{profile.Name}' успешно установлена!", "Импорт");
                    }
                    else
                    {
                        string packName = Path.GetFileNameWithoutExtension(dlg.FileName);
                        string instancesPath = Path.Combine(settings.GamePath, "instances", packName);

                        if (Directory.Exists(instancesPath))
                        {
                            packName += "_" + DateTime.Now.ToString("HHmmss");
                            instancesPath = Path.Combine(settings.GamePath, "instances", packName);
                        }

                        Directory.CreateDirectory(instancesPath);
                        ZipFile.ExtractToDirectory(dlg.FileName, instancesPath, true);

                        var newPack = new ModpackProfile
                        {
                            Name = packName,
                            GameVersion = "1.20.1",
                            Loader = "Custom",
                            FolderPath = instancesPath
                        };

                        settings.Modpacks.Add(newPack);
                        _settingsService.Save(settings);

                        await LoadVersionsAsync();
                        IsModpackOverlayVisible = false;
                        SelectedVersion = $"⭐ {newPack.Name} ({newPack.Loader})";
                        _toastService.ShowSuccess($"Сборка '{packName}' успешно импортирована!", "Импорт");
                    }
                }
                catch (Exception ex)
                {
                    _toastService.ShowError($"Ошибка распаковки сборки: {ex.Message}", "Ошибка импорта");
                }
            }
        }

        private void ExecuteDeleteModpack()
        {
            if (string.IsNullOrWhiteSpace(SelectedVersion) || !SelectedVersion.StartsWith("⭐ "))
            {
                _toastService.ShowWarning("Выберите кастомную сборку для удаления.", "Удаление");
                return;
            }

            string packName = SelectedVersion.Substring(2, SelectedVersion.LastIndexOf('(') - 3).Trim();

            if (QMessageBoxWindow.Show($"Удалить сборку '{packName}' и все ее файлы?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
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
                    _toastService.ShowSuccess($"Сборка '{packName}' удалена.", "Успешно");
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
