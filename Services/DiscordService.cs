using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DiscordRPC;
using MinecraftLauncher.ViewModels;

namespace MinecraftLauncher.Services
{
    public interface IDiscordService
    {
        void StartRpc(bool enabled);
        void StopRpc();
        void SetMenuState(string? selectedVersion = null);
        void SetPageState(string details, string state);
        void SetLaunchingState(string version, string? step = null);
        void UpdateSelectedVersion(string? version);
        void StartGameTracking(string version, string gamePath, bool hideIp, Process? process = null, string? serverIp = null);
        void StopGameTracking();
        bool IsGameRunning { get; }
        bool IsInMenu { get; }
    }

    public class DiscordService : IDiscordService
    {
        private enum GameActivity
        {
            Initializing,
            MainMenu,
            Singleplayer,
            SingleplayerPaused,
            Multiplayer
        }

        private const string ApplicationId = "1144124876001644564";
        private DiscordRpcClient? _client;
        private CancellationTokenSource? _cts;
        private DateTime? _launcherStartTime;
        private DateTime? _gameStartTime;
        private string _currentPageDetails = LocalizationService.Instance.GetString("Str_Discord_PageHomeDetails");
        private string _currentPageState = LocalizationService.Instance.GetString("Str_Discord_PageHomeState");
        private string _currentDetails = "";
        private string _currentState = "";
        private GameActivity _activity = GameActivity.MainMenu;

        public bool IsGameRunning => _gameStartTime != null;
        public bool IsInMenu => _currentPageDetails == LocalizationService.Instance.GetString("Str_Discord_PageHomeDetails");

        private static Button[] CreateDefaultButtons() => new[]
        {
            new Button { Label = LocalizationService.Instance.GetString("Str_Discord_ButtonDownload"), Url = "https://github.com/dyagyatis/QLauncher" },
            new Button { Label = LocalizationService.Instance.GetString("Str_Discord_ButtonTelegram"), Url = "https://t.me/QLauncher_MC" }
        };

        public static DiscordService Instance { get; } = new DiscordService();

        private Timestamps GetLauncherTimestamps()
        {
            if (_launcherStartTime == null)
            {
                _launcherStartTime = DateTime.UtcNow;
            }
            return new Timestamps { Start = _launcherStartTime.Value };
        }

        private Timestamps GetGameTimestamps()
        {
            if (_gameStartTime == null)
            {
                _gameStartTime = DateTime.UtcNow;
            }
            return new Timestamps { Start = _gameStartTime.Value };
        }

        private void EnsureClient()
        {
            try
            {
                var settings = SettingsService.Instance.Settings;
                if (!settings.EnableDiscordRpc) return;

                if (_client == null || _client.IsDisposed)
                {
                    _client = new DiscordRpcClient(ApplicationId);
                    _client.Initialize();
                }
            }
            catch { }
        }

        public void StartRpc(bool enabled)
        {
            if (!enabled)
            {
                StopRpc();
                return;
            }

            if (_launcherStartTime == null)
            {
                _launcherStartTime = DateTime.UtcNow;
            }

            EnsureClient();
            SetMenuState();
        }

        public void StopRpc()
        {
            CancelAndDisposeTracking();
            _gameStartTime = null;
            _activity = GameActivity.MainMenu;

            if (_client != null && !_client.IsDisposed)
            {
                try
                {
                    _client.ClearPresence();
                    _client.Dispose();
                }
                catch { }
                _client = null;
            }
        }

        public void SetPageState(string details, string state)
        {
            _currentPageDetails = details;
            _currentPageState = state;

            if (IsGameRunning) return;
            EnsureClient();
            if (_client == null || _client.IsDisposed) return;

            _currentDetails = details;
            _currentState = state;

            string largeText = details == LocalizationService.Instance.GetString("Str_Discord_PageHomeDetails") ? "QLauncher" : $"QLauncher • {details}";

            _client.SetPresence(new RichPresence
            {
                Details = details,
                State = state,
                Timestamps = GetLauncherTimestamps(),
                Assets = new Assets
                {
                    LargeImageKey = "logo",
                    LargeImageText = largeText
                },
                Buttons = CreateDefaultButtons()
            });
        }

        public void SetMenuState(string? selectedVersion = null)
        {
            string state = !string.IsNullOrWhiteSpace(selectedVersion) && !MainViewModel.IsCreateModpackEntry(selectedVersion)
                ? LocalizationService.Instance.Format("Str_Discord_SelectedVersion", selectedVersion.Replace("⭐", "").Trim())
                : LocalizationService.Instance.GetString("Str_Discord_PickingPack");

            if (IsGameRunning)
            {
                _currentPageDetails = LocalizationService.Instance.GetString("Str_Discord_PageHomeDetails");
                _currentPageState = state;
                return;
            }

            SetPageState(LocalizationService.Instance.GetString("Str_Discord_PageHomeDetails"), state);
        }

        public void SetLaunchingState(string version, string? step = null)
        {
            EnsureClient();
            if (_client == null || _client.IsDisposed) return;

            string cleanVer = version.Replace("⭐", "").Trim();
            var (loaderKey, loaderTitle) = ResolveLoaderInfo(version);

            _currentDetails = LocalizationService.Instance.Format("Str_Discord_Launching", cleanVer);
            // step необязателен: при null показываем стандартное «подготовка к запуску».
            _currentState = string.IsNullOrEmpty(step)
                ? LocalizationService.Instance.GetString("Str_Launch_Initializing")
                : step;

            _client.SetPresence(new RichPresence
            {
                Details = _currentDetails,
                State = _currentState,
                Timestamps = GetLauncherTimestamps(),
                Assets = new Assets
                {
                    LargeImageKey = "logo",
                    LargeImageText = "QLauncher v2.0",
                    SmallImageKey = loaderKey,
                    SmallImageText = loaderTitle
                },
                Buttons = CreateDefaultButtons()
            });
        }

        public void UpdateSelectedVersion(string? version)
        {
            if (IsInMenu && !IsGameRunning)
            {
                SetMenuState(version);
            }
        }

        public void StartGameTracking(string version, string gamePath, bool hideIp, Process? process = null, string? serverIp = null)
        {
            EnsureClient();
            if (_client == null || _client.IsDisposed) return;

            _gameStartTime = DateTime.UtcNow;

            string cleanVer = version.Replace("⭐", "").Trim();
            var (loaderKey, loaderTitle) = ResolveLoaderInfo(version);
            string modInfo = ResolveModInfo(gamePath, loaderTitle, cleanVer);

            if (!string.IsNullOrWhiteSpace(serverIp))
            {
                _activity = GameActivity.Multiplayer;
                string srv = hideIp ? LocalizationService.Instance.GetString("Str_Discord_Online") : FormatServerName(serverIp);
                UpdatePresence(srv, LocalizationService.Instance.GetString("Str_Discord_Joining"), loaderKey, modInfo);
            }
            else
            {
                _activity = GameActivity.Initializing;
                UpdatePresence(LocalizationService.Instance.Format("Str_Discord_Launching", cleanVer), LocalizationService.Instance.GetString("Str_Discord_LoadingAssets"), loaderKey, modInfo);
            }

            CancelAndDisposeTracking();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // 1. Process real-time stdout/stderr stream
            if (process != null)
            {
                try
                {
                    process.OutputDataReceived += (_, e) =>
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                        {
                            HandleLogLine(e.Data, cleanVer, loaderKey, modInfo, hideIp, serverIp);
                        }
                    };

                    process.ErrorDataReceived += (_, e) =>
                    {
                        if (!string.IsNullOrWhiteSpace(e.Data))
                        {
                            HandleLogLine(e.Data, cleanVer, loaderKey, modInfo, hideIp, serverIp);
                        }
                    };
                }
                catch { }

                // 2. Window detector (once window is drawn, game is running!)
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(1500, token);
                        while (!token.IsCancellationRequested && !process.HasExited)
                        {
                            process.Refresh();
                            if (process.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(process.MainWindowTitle))
                            {
                                if (_activity == GameActivity.Initializing)
                                {
                                    if (string.IsNullOrWhiteSpace(serverIp))
                                    {
                                        _activity = GameActivity.MainMenu;
                                        UpdatePresence(LocalizationService.Instance.Format("Str_Discord_Playing", cleanVer), LocalizationService.Instance.GetString("Str_Discord_PageHomeDetails"), loaderKey, modInfo);
                                    }
                                    else
                                    {
                                        _activity = GameActivity.Multiplayer;
                                        string srv = hideIp ? LocalizationService.Instance.GetString("Str_Discord_Online") : FormatServerName(serverIp);
                                        UpdatePresence(srv, LocalizationService.Instance.GetString("Str_Discord_OnServer"), loaderKey, modInfo);
                                    }
                                }
                                break;
                            }
                            await Task.Delay(1000, token);
                        }
                    }
                    catch { }
                }, token);
            }

            // 3. Fallback log file watcher (checks instances/<pack> and root)
            string logFile = Path.Combine(gamePath, "logs", "latest.log");
            if (!File.Exists(logFile))
            {
                string alt = Path.Combine(gamePath, "instances", cleanVer, "logs", "latest.log");
                if (File.Exists(alt)) logFile = alt;
            }
            Task.Run(() => WatchLogFile(logFile, cleanVer, loaderKey, modInfo, hideIp, serverIp, token), token);
        }

        public void StopGameTracking()
        {
            // Раньше токен только отменялся, но никогда не освобождался, и новый
            // CancellationTokenSource создавался на каждом запуске игры.
            CancelAndDisposeTracking();

            _gameStartTime = null;
            _activity = GameActivity.MainMenu;

            // Раньше здесь стоял EnsureClient(), который ради сброса статуса
            // создавал и инициализировал новый RPC-клиент, если его ещё не было.
            // Теперь только сбрасываем статус у уже существующего клиента.
            if (_client == null || _client.IsDisposed) return;

            SetPageState(_currentPageDetails, _currentPageState);
        }

        /// <summary>
        /// Отменяет отслеживание и освобождает CTS. Токен отменяется в трёх местах,
        /// и ни один из них не освобождал источник.
        /// </summary>
        private void CancelAndDisposeTracking()
        {
            var cts = Interlocked.Exchange(ref _cts, null);
            if (cts == null) return;

            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Уже освобождён — ничего делать не нужно.
            }

            cts.Dispose();
        }

        private void HandleLogLine(string line, string version, string loaderKey, string modInfo, bool hideIp, string? serverIp)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            // 1. Connecting to multiplayer server
            if (line.Contains("Connecting to ", StringComparison.OrdinalIgnoreCase))
            {
                string serverDetails = "";
                if (!hideIp)
                {
                    try
                    {
                        int idx = line.IndexOf("Connecting to ", StringComparison.OrdinalIgnoreCase) + 14;
                        string raw = line.Substring(idx).Split(',')[0].Trim();
                        serverDetails = FormatServerName(raw);
                    }
                    catch { }
                }

                string details = string.IsNullOrEmpty(serverDetails) ? LocalizationService.Instance.GetString("Str_Discord_Online") : serverDetails;
                string state = hideIp ? LocalizationService.Instance.GetString("Str_Discord_Multiplayer") : LocalizationService.Instance.GetString("Str_Discord_OnServer");
                _activity = GameActivity.Multiplayer;
                UpdatePresence(details, state, loaderKey, modInfo);
                return;
            }

            // 2. Singleplayer world started / loaded
            if (line.Contains("Starting integrated minecraft server", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Preparing start region", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Preparing level", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Preparing spawn area", StringComparison.OrdinalIgnoreCase))
            {
                _activity = GameActivity.Singleplayer;
                UpdatePresence(LocalizationService.Instance.GetString("Str_Discord_Singleplayer"), LocalizationService.Instance.GetString("Str_Discord_Surviving"), loaderKey, modInfo);
                return;
            }

            // 3. Paused inside singleplayer (Esc menu)
            if (line.Contains("Saving and pausing game", StringComparison.OrdinalIgnoreCase))
            {
                if (_activity == GameActivity.Singleplayer)
                {
                    _activity = GameActivity.SingleplayerPaused;
                    UpdatePresence(LocalizationService.Instance.GetString("Str_Discord_Singleplayer"), LocalizationService.Instance.GetString("Str_Discord_InGameMenu"), loaderKey, modInfo);
                }
                return;
            }

            // 4. Activity in singleplayer while was paused (player resumed playing)
            if (_activity == GameActivity.SingleplayerPaused)
            {
                if (line.Contains("[CHAT]", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("advancements", StringComparison.OrdinalIgnoreCase))
                {
                    _activity = GameActivity.Singleplayer;
                    UpdatePresence(LocalizationService.Instance.GetString("Str_Discord_Singleplayer"), LocalizationService.Instance.GetString("Str_Discord_Surviving"), loaderKey, modInfo);
                }
            }

            // 5. Exited world / disconnected from server back to Main Menu
            if (line.Contains("Stopping server", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Stopping singleplayer server", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Disconnecting from", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Disconnected", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("lost connection", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("left the game", StringComparison.OrdinalIgnoreCase))
            {
                _activity = GameActivity.MainMenu;
                UpdatePresence(LocalizationService.Instance.Format("Str_Discord_Playing", version), LocalizationService.Instance.GetString("Str_Discord_PageHomeDetails"), loaderKey, modInfo);
                return;
            }

            // 6. Game startup reached main menu
            if (line.Contains("Sound engine started", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("OpenAL initialized", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("textures/atlas/gui.png-atlas", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Backend library: LWJGL", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Reloading ResourceManager", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Setting user: ", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("TitleScreen", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Entering main menu", StringComparison.OrdinalIgnoreCase))
            {
                if (_activity == GameActivity.Initializing)
                {
                    if (string.IsNullOrWhiteSpace(serverIp))
                    {
                        _activity = GameActivity.MainMenu;
                        UpdatePresence(LocalizationService.Instance.Format("Str_Discord_Playing", version), LocalizationService.Instance.GetString("Str_Discord_PageHomeDetails"), loaderKey, modInfo);
                    }
                    else
                    {
                        _activity = GameActivity.Multiplayer;
                        string srv = hideIp ? LocalizationService.Instance.GetString("Str_Discord_Online") : FormatServerName(serverIp);
                        UpdatePresence(srv, LocalizationService.Instance.GetString("Str_Discord_OnServer"), loaderKey, modInfo);
                    }
                }
            }
        }

        private async Task WatchLogFile(string logFile, string version, string loaderKey, string modInfo, bool hideIp, string? serverIp, CancellationToken token)
        {
            await Task.Delay(2000, token);

            int retries = 0;
            while (!File.Exists(logFile) && retries < 20)
            {
                if (token.IsCancellationRequested) return;
                await Task.Delay(500, token);
                retries++;
            }

            if (!File.Exists(logFile)) return;

            try
            {
                using var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs, Encoding.UTF8);

                long lastPosition = 0;
                var lastWrite = File.GetLastWriteTimeUtc(logFile);
                if (_gameStartTime.HasValue && lastWrite < _gameStartTime.Value.AddSeconds(-3))
                {
                    lastPosition = fs.Length;
                    fs.Seek(lastPosition, SeekOrigin.Begin);
                }

                while (!token.IsCancellationRequested)
                {
                    if (fs.Length < lastPosition)
                    {
                        fs.Seek(0, SeekOrigin.Begin);
                        lastPosition = 0;
                        reader.DiscardBufferedData();
                    }

                    // ReadLineAsync не принимает токен и не имеет таймаута: если игра жива,
                    // но молчит (меню, пауза), watcher навсегда застревал на
                    // чтении, держал FileStream открытым и не доходил до проверки
                    // отмены. WaitAsync с ожиданием позволяет выйти по токену.
                    string? line = await ReadLineWithCancellationAsync(reader, token);
                    if (line == null)
                    {
                        if (token.IsCancellationRequested)
                        {
                            break;
                        }

                        lastPosition = fs.Position;

                        try
                        {
                            await Task.Delay(500, token);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }

                        continue;
                    }

                    lastPosition = fs.Position;
                    HandleLogLine(line, version, loaderKey, modInfo, hideIp, serverIp);
                }
            }
            catch { }
        }

        /// <summary>
        /// Читает строку, но позволяет выйти по токену отмены.
        /// Возвращает null при отмене или по достижении лимита ожидания,
        /// чтобы вызывающий код различил эти случаи по token.IsCancellationRequested.
        /// </summary>
        private static async Task<string?> ReadLineWithCancellationAsync(StreamReader reader, CancellationToken token)
        {
            Task<string?> readTask = reader.ReadLineAsync();

            // Если данные не идут, ждать бесконечно нельзя: опрашиваем
            // с небольшим интервалом и следим за токеном.
            while (true)
            {
                Task completed = await Task.WhenAny(readTask, Task.Delay(200, token));

                if (completed == readTask)
                {
                    try
                    {
                        return await readTask;
                    }
                    catch (OperationCanceledException)
                    {
                        return null;
                    }
                    catch (IOException)
                    {
                        return null;
                    }
                }

                if (token.IsCancellationRequested)
                {
                    return null;
                }
            }
        }

        private void UpdatePresence(string details, string state, string loaderKey, string modInfo)
        {
            EnsureClient();
            if (_client == null || _client.IsDisposed) return;

            _currentDetails = details;
            _currentState = state;

            _client.SetPresence(new RichPresence
            {
                Details = details,
                State = state,
                Timestamps = GetGameTimestamps(),
                Assets = new Assets
                {
                    LargeImageKey = "logo",
                    LargeImageText = "QLauncher",
                    SmallImageKey = loaderKey,
                    SmallImageText = modInfo
                },
                Buttons = CreateDefaultButtons()
            });
        }

        private static (string key, string title) ResolveLoaderInfo(string version)
        {
            string lower = version.ToLowerInvariant();
            if (lower.Contains("fabric")) return ("fabric", "Fabric");
            if (lower.Contains("neoforge")) return ("neoforge", "NeoForge");
            if (lower.Contains("forge")) return ("forge", "Forge");
            if (lower.Contains("quilt")) return ("quilt", "Quilt");
            return ("vanilla", "Vanilla");
        }

        private static string ResolveModInfo(string gamePath, string loaderTitle, string version)
        {
            try
            {
                string modsDir = Path.Combine(gamePath, "mods");
                if (Directory.Exists(modsDir))
                {
                    int count = Directory.GetFiles(modsDir, "*.jar", SearchOption.TopDirectoryOnly).Length;
                    if (count > 0)
                    {
                        return LocalizationService.Instance.Format("Str_Discord_ModsCount", $"{loaderTitle} • {count}");
                    }
                }
            }
            catch { }

            return $"{loaderTitle} {version}";
        }

        private static string FormatServerName(string rawIp)
        {
            if (string.IsNullOrWhiteSpace(rawIp)) return LocalizationService.Instance.GetString("Str_Discord_Online");

            string lower = rawIp.ToLowerInvariant();
            if (lower.Contains("hypixel")) return "Hypixel Network";
            if (lower.Contains("mineblaze")) return "MineBlaze";
            if (lower.Contains("scraft")) return "SCRAFT Server";
            if (lower.Contains("vimeworld")) return "VimeWorld";
            if (lower.Contains("holyworld")) return "HolyWorld";
            if (lower.Contains("funtime")) return "FunTime";
            if (lower.Contains("reallyworld")) return "ReallyWorld";
            if (lower.Contains("cubecraft")) return "CubeCraft";
            if (lower.Contains("minemen")) return "Minemen Club";
            if (lower.Contains("gommehd")) return "GommeHD";
            if (lower.Contains("2b2t")) return "2b2t";
            if (lower.Contains("spworlds")) return "SPWorlds";

            string clean = rawIp.Split(':')[0].Trim();
            return LocalizationService.Instance.Format("Str_Discord_Server", clean);
        }
    }
}
