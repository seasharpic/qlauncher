using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Services;
using MinecraftLauncher.ViewModels;
using MinecraftLauncher.Views.Pages;
using MinecraftLauncher.Views.Windows;

namespace MinecraftLauncher
{
    public partial class MainWindow : Window
    {
        private static readonly ILocalizationService _loc = LocalizationService.Instance;

        public MainViewModel ViewModel { get; }

        private GameConsoleWindow? _consoleWindow;
        private Action? _themeChangedHandler;
        private readonly Stack<UserControl> _navHistory = new();
        private TrayIcon? _trayIcon;

        public MainWindow()
        {
            InitializeComponent();
            ViewModel = new MainViewModel();
            DataContext = ViewModel;

            InitTrayIcon();

            ViewModel.GameLaunched += OnGameLaunched;
            ViewModel.RequestClose += () => Close();
            ViewModel.RequestHide += () =>
            {
                if (_trayIcon != null) _trayIcon.IsVisible = true;
                Hide();
            };
            ViewModel.RequestShow += RestoreLauncher;

            _themeChangedHandler = () => Dispatcher.UIThread.Post(UpdateThemeUi);
            ThemeService.Instance.ThemeChanged += _themeChangedHandler;

            UpdateThemeUi();

            AddHandler(DragDrop.DragEnterEvent, Window_DragEnter);
            AddHandler(DragDrop.DragLeaveEvent, Window_DragLeave);
            AddHandler(DragDrop.DragOverEvent, Window_DragOver);
            AddHandler(DragDrop.DropEvent, Window_Drop);

            Opened += MainWindow_Opened;
            Closing += MainWindow_Closing;
        }

        private void InitTrayIcon()
        {
            try
            {
                using var iconStream = AssetLoader.Open(new Uri("avares://QLauncher/logo.png"));
                var restoreItem = new NativeMenuItem(_loc.GetString("Str_Tray_Restore"));
                restoreItem.Click += (_, _) => RestoreLauncher();

                var exitItem = new NativeMenuItem(_loc.GetString("Str_Tray_Exit"));
                exitItem.Click += (_, _) => UiHelper.Shutdown();

                var menu = new NativeMenu();
                menu.Items.Add(restoreItem);
                menu.Items.Add(new NativeMenuItemSeparator());
                menu.Items.Add(exitItem);

                _trayIcon = new TrayIcon
                {
                    Icon = new WindowIcon(iconStream),
                    ToolTipText = "QLauncher",
                    Menu = menu,
                    IsVisible = false
                };
                _trayIcon.Clicked += (_, _) => RestoreLauncher();
            }
            catch { }
        }

        private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
        {
            if (_themeChangedHandler != null)
            {
                ThemeService.Instance.ThemeChanged -= _themeChangedHandler;
                _themeChangedHandler = null;
            }

            if (_trayIcon != null)
            {
                _trayIcon.IsVisible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            ViewModel?.Dispose();

            _consoleWindow?.UnbindOutput();
            _consoleWindow?.ViewModel?.DetachProcess();
            _consoleWindow?.Close();

            DiscordService.Instance.StopRpc();
        }

        private async void MainWindow_Opened(object? sender, EventArgs e)
        {
            ToastService.Instance.RegisterContainer(ToastContainer);

            if (!string.IsNullOrEmpty(App.JustUpdatedVersion))
            {
                ToastService.Instance.ShowSuccess(_loc.Format("Str_Update_AppliedToast", App.JustUpdatedVersion), _loc.GetString("Str_T_Updates"));
                App.JustUpdatedVersion = null;
            }

            if (!LauncherPathHelper.IsPortableMode && PortableBadge != null)
            {
                PortableBadge.IsVisible = false;
            }

            await ViewModel.InitializeAsync();

            ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.ProgressFillRatio))
                {
                    double hostW = ProgressHostGrid.Bounds.Width;
                    if (hostW > 0)
                    {
                        double targetWidth = Math.Max(0, Math.Min(hostW, ViewModel.ProgressFillRatio * hostW));
                        ProgressFillBorder.Width = targetWidth;
                        ProgressFillBorder.IsVisible = targetWidth > 0;
                    }
                }
                else if (args.PropertyName == nameof(MainViewModel.IsModpackOverlayVisible))
                {
                    if (ViewModel.IsModpackOverlayVisible)
                        ViewModel.DiscordService.SetPageState(_loc.GetString("Str_Discord_PageModpacksDetails"), _loc.GetString("Str_Discord_PageModpacksState"));
                    else
                        UpdateDiscordForCurrentState();
                }
                else if (args.PropertyName == nameof(MainViewModel.IsShortcutOverlayVisible))
                {
                    if (ViewModel.IsShortcutOverlayVisible)
                        ViewModel.DiscordService.SetPageState(_loc.GetString("Str_Discord_ShortcutDetails"), _loc.GetString("Str_Discord_ShortcutState"));
                    else
                        UpdateDiscordForCurrentState();
                }
            };
        }

        private void UpdateDiscordForCurrentState()
        {
            if (MainFrame.IsVisible && MainFrame.Content != null)
                UpdateDiscordForPage(MainFrame.Content);
            else
                ViewModel.DiscordService.SetMenuState(ViewModel.SelectedVersion);
        }

        private void OnGameLaunched(System.Diagnostics.Process process)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_consoleWindow == null)
                {
                    _consoleWindow = new GameConsoleWindow();
                    _consoleWindow.Closed += (_, _) => _consoleWindow = null;
                    _consoleWindow.BindOutput(ViewModel);
                }

                _consoleWindow.AttachProcess(process);
                _consoleWindow.Show();
                _consoleWindow.Activate();
            });
        }

        public void AnimateNavigate(UserControl page)
        {
            if (MainFrame.IsVisible && MainFrame.Content is UserControl current)
            {
                _navHistory.Push(current);
            }

            HomeView.IsVisible = false;
            MainFrame.Content = page;
            MainFrame.IsVisible = true;
            UpdateDiscordForPage(page);
        }

        public void AnimateGoBack()
        {
            if (_navHistory.Count > 0)
            {
                var prev = _navHistory.Pop();
                MainFrame.Content = prev;
                UpdateDiscordForPage(prev);
            }
            else
            {
                CloseSettings();
            }
        }

        public void UpdateDiscordForPage(object? content)
        {
            if (content == null)
            {
                ViewModel.DiscordService.SetMenuState(ViewModel.SelectedVersion);
                return;
            }

            switch (content)
            {
                case SettingsPage:
                    ViewModel.DiscordService.SetPageState(_loc.GetString("Str_Discord_PageSettingsDetails"), _loc.GetString("Str_Discord_PageSettingsState"));
                    break;
                case ModpacksPage:
                    ViewModel.DiscordService.SetPageState(_loc.GetString("Str_Discord_PageModpacksDetails"), _loc.GetString("Str_Discord_PageModpacksState"));
                    break;
                case ModsPage:
                    ViewModel.DiscordService.SetPageState(_loc.GetString("Str_Discord_PageModsDetails"), _loc.GetString("Str_Discord_PageModsState"));
                    break;
                case ScreenshotsPage:
                    ViewModel.DiscordService.SetPageState(_loc.GetString("Str_Discord_PageScreenshotsDetails"), _loc.GetString("Str_Discord_PageScreenshotsState"));
                    break;
                case ProfileManagerPage profilePage:
                    string packName = profilePage.ViewModel?.Profile?.Name ?? _loc.GetString("Str_Discord_DefaultPack");
                    ViewModel.DiscordService.SetPageState(_loc.GetString("Str_Discord_PageProfileDetails"), _loc.Format("Str_Discord_PageProfileState", packName));
                    break;
                default:
                    ViewModel.DiscordService.SetPageState(_loc.GetString("Str_Discord_PageHomeDetails"), _loc.GetString("Str_Discord_PageHomeState"));
                    break;
            }
        }

        public void CloseSettings()
        {
            _navHistory.Clear();
            MainFrame.IsVisible = false;
            MainFrame.Content = null;

            ViewModel.ApplyCustomWallpaper();
            _ = ViewModel.LoadAccountsAsync();
            _ = ViewModel.LoadVersionsAsync();

            ViewModel.DiscordService.SetMenuState(ViewModel.SelectedVersion);

            HomeView.IsVisible = true;
        }

        public void ReloadLauncher()
        {
            ViewModel.ApplyCustomWallpaper();
            _ = ViewModel.LoadAccountsAsync();
            _ = ViewModel.LoadVersionsAsync();
        }

        public void OpenModpackOverlay()
        {
            if (ViewModel.VanillaVersions.Count > 0 && string.IsNullOrEmpty(ViewModel.NewModpackVersion))
            {
                ViewModel.NewModpackVersion = ViewModel.VanillaVersions[0];
            }
            ViewModel.IsModpackOverlayVisible = true;
        }

        public void OpenShortcutOverlay()
        {
            ViewModel.ShortcutVersion = ViewModel.SelectedVersion;
            ViewModel.IsShortcutOverlayVisible = true;
        }

        public void SelectVersionByTag(string packTag)
        {
            if (ViewModel.Versions.Contains(packTag))
            {
                ViewModel.SelectedVersion = packTag;
            }
        }

        private void UpdateThemeUi()
        {
            if (ThemeToggleBtn != null)
            {
                ThemeToggleBtn.Content = ThemeService.Instance.IsDarkTheme ? "☀" : "🌙";
                ToolTip.SetTip(ThemeToggleBtn, ThemeService.Instance.IsDarkTheme
                    ? _loc.GetString("Str_Theme_ToggleToLight")
                    : _loc.GetString("Str_Theme_ToggleToDark"));
            }
        }

        private void ThemeToggleBtn_Click(object? sender, RoutedEventArgs e) =>
            ViewModel.ToggleThemeCommand.Execute(null);

        private void SettingsButton_Click(object? sender, RoutedEventArgs e) =>
            AnimateNavigate(new SettingsPage());

        private void AccountManagerButton_Click(object? sender, RoutedEventArgs e)
        {
            AudioService.Instance.PlayClickSound(SettingsService.Instance.Settings.EnableUiSounds);
            var page = new SettingsPage();
            AnimateNavigate(page);
            Dispatcher.UIThread.Post(() => page.ScrollToAccounts(), DispatcherPriority.Loaded);
        }

        private void Skin3DButton_Click(object? sender, RoutedEventArgs e)
        {
            AudioService.Instance.PlayClickSound(SettingsService.Instance.Settings.EnableUiSounds);
            var account = ViewModel.SelectedAccount;
            if (account == null) return;

            var window = new SkinPreviewWindow(account.IsElyBy, account.Nickname, account.Uuid);
            window.Show(this);
        }

        private void NavHome_Click(object? sender, RoutedEventArgs e)
        {
            AudioService.Instance.PlayClickSound(SettingsService.Instance.Settings.EnableUiSounds);
            CloseSettings();
        }

        private void NavModpacks_Click(object? sender, RoutedEventArgs e)
        {
            AudioService.Instance.PlayClickSound(SettingsService.Instance.Settings.EnableUiSounds);
            AnimateNavigate(new ModpacksPage());
        }

        private void NavMods_Click(object? sender, RoutedEventArgs e)
        {
            AudioService.Instance.PlayClickSound(SettingsService.Instance.Settings.EnableUiSounds);
            AnimateNavigate(new ModsPage());
        }

        private void NavScreenshots_Click(object? sender, RoutedEventArgs e)
        {
            AudioService.Instance.PlayClickSound(SettingsService.Instance.Settings.EnableUiSounds);
            AnimateNavigate(new ScreenshotsPage(SettingsService.Instance.Settings.GamePath));
        }

        private void NavConsole_Click(object? sender, RoutedEventArgs e)
        {
            AudioService.Instance.PlayClickSound(SettingsService.Instance.Settings.EnableUiSounds);
            if (_consoleWindow == null)
            {
                _consoleWindow = new GameConsoleWindow();
                _consoleWindow.Closed += (_, _) => _consoleWindow = null;
                _consoleWindow.BindOutput(ViewModel);
            }
            _consoleWindow.Show();
            _consoleWindow.Activate();
        }

        private void VersionComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (VersionComboBox.SelectedItem is string selectedStr && MainViewModel.IsCreateModpackEntry(selectedStr))
            {
                OpenModpackOverlay();
                string lastVer = SettingsService.Instance.Settings.LastSelectedVersion;
                if (!string.IsNullOrEmpty(lastVer) && ViewModel.Versions.Contains(lastVer))
                {
                    ViewModel.SelectedVersion = lastVer;
                }
                else
                {
                    var fallback = ViewModel.Versions.FirstOrDefault(v => !MainViewModel.IsCreateModpackEntry(v));
                    if (!string.IsNullOrEmpty(fallback))
                    {
                        ViewModel.SelectedVersion = fallback;
                    }
                }
            }
        }

        private void CloseModpackOverlay_PointerPressed(object? sender, PointerPressedEventArgs e) =>
            ViewModel.IsModpackOverlayVisible = false;

        private void CloseShortcutOverlay_PointerPressed(object? sender, PointerPressedEventArgs e) =>
            ViewModel.IsShortcutOverlayVisible = false;

        private void CloseAddServerOverlay_PointerPressed(object? sender, PointerPressedEventArgs e) =>
            ViewModel.CloseAddServerOverlayCommand.Execute(null);

        private void QuickFolderButton_Click(object? sender, RoutedEventArgs e)
        {
            AudioService.Instance.PlayClickSound(SettingsService.Instance.Settings.EnableUiSounds);
            FoldersPopup.IsOpen = !FoldersPopup.IsOpen;
        }

        private void OpenFolder_Mods(object? sender, RoutedEventArgs e) { FoldersPopup.IsOpen = false; ViewModel.OpenQuickFolderCommand.Execute("mods"); }
        private void OpenFolder_ResourcePacks(object? sender, RoutedEventArgs e) { FoldersPopup.IsOpen = false; ViewModel.OpenQuickFolderCommand.Execute("resourcepacks"); }
        private void OpenFolder_ShaderPacks(object? sender, RoutedEventArgs e) { FoldersPopup.IsOpen = false; ViewModel.OpenQuickFolderCommand.Execute("shaderpacks"); }
        private void OpenFolder_Saves(object? sender, RoutedEventArgs e) { FoldersPopup.IsOpen = false; ViewModel.OpenQuickFolderCommand.Execute("saves"); }
        private void OpenFolder_Screenshots(object? sender, RoutedEventArgs e) { FoldersPopup.IsOpen = false; ViewModel.OpenQuickFolderCommand.Execute("screenshots"); }
        private void OpenFolder_Logs(object? sender, RoutedEventArgs e) { FoldersPopup.IsOpen = false; ViewModel.OpenQuickFolderCommand.Execute("logs"); }
        private void OpenFolder_Root(object? sender, RoutedEventArgs e) { FoldersPopup.IsOpen = false; ViewModel.OpenQuickFolderCommand.Execute(""); }

        private void Window_DragEnter(object? sender, DragEventArgs e)
        {
            if (e.Data.Contains(DataFormats.Files))
            {
                if (DragDropOverlay != null) DragDropOverlay.IsVisible = true;
                e.DragEffects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void Window_DragLeave(object? sender, DragEventArgs e)
        {
            if (DragDropOverlay != null) DragDropOverlay.IsVisible = false;
        }

        private void Window_DragOver(object? sender, DragEventArgs e)
        {
            if (e.Data.Contains(DataFormats.Files))
            {
                e.DragEffects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private async Task ImportMrPackAsync(string file)
        {
            try
            {
                ToastService.Instance.ShowInfo(_loc.GetString("Str_DragDrop_Importing"), _loc.GetString("Str_T_ImportTitle"));
                var profile = await ModpackImportService.Instance.ImportAsync(
                    file,
                    SettingsService.Instance.Settings.GamePath);

                var settings = SettingsService.Instance.Settings;
                settings.Modpacks.Add(profile);
                SettingsService.Instance.Save();

                await ViewModel.LoadVersionsAsync();
                ViewModel.SelectedVersion = $"⭐ {profile.Name} ({profile.Loader})";
                ToastService.Instance.ShowSuccess(_loc.Format("Str_Import_Done", profile.Name), _loc.GetString("Str_T_ImportTitle"));
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("DragDropImport", $"Failed to import mrpack '{file}'", ex);
                ToastService.Instance.ShowError(_loc.Format("Str_Import_Error", ex.Message), _loc.GetString("Str_T_Error"));
            }
        }

        private void Window_Drop(object? sender, DragEventArgs e)
        {
            if (DragDropOverlay != null) DragDropOverlay.IsVisible = false;
            var storageItems = e.Data.GetFiles();
            if (storageItems == null) return;

            var files = storageItems
                .Select(i => i.TryGetLocalPath())
                .Where(p => !string.IsNullOrEmpty(p))
                .Cast<string>()
                .ToArray();

            if (files.Length == 0) return;

            var settings = SettingsService.Instance.Settings;
            string effectiveGameDir = settings.GamePath;

            string selectedPack = ViewModel.SelectedVersion;
            if (!string.IsNullOrEmpty(selectedPack) && selectedPack.StartsWith("⭐"))
            {
                string packName = selectedPack.Replace("⭐", "").Trim();
                if (packName.Contains(" (")) packName = packName.Substring(0, packName.LastIndexOf(" (")).Trim();

                var modpack = settings.Modpacks.Find(p => p.Name == packName);
                if (modpack != null && Directory.Exists(modpack.FolderPath))
                {
                    effectiveGameDir = modpack.FolderPath;
                }
            }

            string targetModsDir = Path.Combine(effectiveGameDir, "mods");
            string targetShaderpacksDir = Path.Combine(effectiveGameDir, "shaderpacks");
            string targetResourcepacksDir = Path.Combine(effectiveGameDir, "resourcepacks");

            Directory.CreateDirectory(targetModsDir);

            int modsCount = 0;
            int shadersCount = 0;
            int resourcesCount = 0;

            foreach (string file in files)
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".mrpack")
                {
                    _ = ImportMrPackAsync(file);
                    continue;
                }
                else if (ext == ".jar")
                {
                    File.Copy(file, Path.Combine(targetModsDir, Path.GetFileName(file)), true);
                    modsCount++;
                }
                else if (ext == ".zip")
                {
                    bool isShader = false;
                    bool isResource = false;
                    try
                    {
                        using var archive = ZipFile.OpenRead(file);
                        foreach (var entry in archive.Entries)
                        {
                            if (entry.FullName.StartsWith("shaders/", StringComparison.OrdinalIgnoreCase))
                            {
                                isShader = true;
                                break;
                            }
                            if (string.Equals(entry.FullName, "pack.mcmeta", StringComparison.OrdinalIgnoreCase))
                            {
                                isResource = true;
                            }
                        }
                    }
                    catch { }

                    if (isShader)
                    {
                        Directory.CreateDirectory(targetShaderpacksDir);
                        File.Copy(file, Path.Combine(targetShaderpacksDir, Path.GetFileName(file)), true);
                        shadersCount++;
                    }
                    else if (isResource)
                    {
                        Directory.CreateDirectory(targetResourcepacksDir);
                        File.Copy(file, Path.Combine(targetResourcepacksDir, Path.GetFileName(file)), true);
                        resourcesCount++;
                    }
                    else
                    {
                        File.Copy(file, Path.Combine(targetModsDir, Path.GetFileName(file)), true);
                        modsCount++;
                    }
                }
            }

            int total = modsCount + shadersCount + resourcesCount;
            if (total > 0)
            {
                AudioService.Instance.PlayClickSound(SettingsService.Instance.Settings.EnableUiSounds);
                var parts = new List<string>();
                if (modsCount > 0) parts.Add(_loc.Format("Str_Mods_Count", modsCount));
                if (shadersCount > 0) parts.Add(_loc.Format("Str_Shaders_Count", shadersCount));
                if (resourcesCount > 0) parts.Add(_loc.Format("Str_ResourcePacks_Count", resourcesCount));
                ToastService.Instance.ShowSuccess(_loc.Format("Str_DragDrop_ImportedSummary", string.Join(", ", parts)), _loc.GetString("Str_T_ImportTitle"));
            }
        }

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                if (e.ClickCount == 2)
                {
                    MaximizeButton_Click(sender, e);
                }
                else
                {
                    BeginMoveDrag(e);
                }
            }
        }

        private void MinimizeButton_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void MaximizeButton_Click(object? sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void CloseButton_Click(object? sender, RoutedEventArgs e) => UiHelper.Shutdown();

        private void RestoreLauncher()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            if (_trayIcon != null) _trayIcon.IsVisible = false;
        }
    }
}
