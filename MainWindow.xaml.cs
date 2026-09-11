using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MinecraftLauncher.Common;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Services;
using MinecraftLauncher.ViewModels;
using MinecraftLauncher.Views.Pages;
using MinecraftLauncher.Views.Windows;

namespace MinecraftLauncher
{
    public partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; }

        private double _normalLeft, _normalTop, _normalWidth, _normalHeight;
        private bool _isCustomMaximized;
        private GameConsoleWindow? _consoleWindow;

        public MainWindow()
        {
            InitializeComponent();
            ViewModel = new MainViewModel();
            DataContext = ViewModel;

            ViewModel.GameLaunched += OnGameLaunched;
            ViewModel.RequestClose += () => Close();
            ViewModel.RequestHide += () =>
            {
                LauncherTrayIcon.Visibility = Visibility.Visible;
                Hide();
            };
            ViewModel.RequestShow += RestoreLauncher;

            MainFrame.Navigated += MainFrame_Navigated;

            ThemeService.Instance.ThemeChanged += () => Dispatcher.Invoke(UpdateThemeUi);
            UpdateThemeUi();

            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ToastService.Instance.RegisterContainer(ToastContainer);

            if (!string.IsNullOrEmpty(App.JustUpdatedVersion))
            {
                ToastService.Instance.ShowSuccess($"QLauncher успешно обновлен до {App.JustUpdatedVersion}!", "Обновление");
                App.JustUpdatedVersion = null;
            }

            if (!LauncherPathHelper.IsPortableMode && PortableBadge != null)
            {
                PortableBadge.Visibility = Visibility.Collapsed;
            }

            var loadFade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(350));
            ContentGrid.BeginAnimation(UIElement.OpacityProperty, loadFade);

            await ViewModel.InitializeAsync();

            ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.ProgressFillRatio))
                {
                    if (ProgressHostGrid.ActualWidth > 0)
                    {
                        double targetWidth = Math.Max(0, Math.Min(ProgressHostGrid.ActualWidth, ViewModel.ProgressFillRatio * ProgressHostGrid.ActualWidth));
                        ProgressFillBorder.Width = targetWidth;
                        ProgressFillBorder.Visibility = targetWidth > 0 ? Visibility.Visible : Visibility.Collapsed;
                    }
                }
                else if (args.PropertyName == nameof(MainViewModel.IsModpackOverlayVisible))
                {
                    if (ViewModel.IsModpackOverlayVisible)
                    {
                        AnimateOpenModpackOverlay();
                    }
                    else
                    {
                        AnimateCloseModpackOverlay();
                    }
                }
                else if (args.PropertyName == nameof(MainViewModel.IsShortcutOverlayVisible))
                {
                    if (ViewModel.IsShortcutOverlayVisible)
                    {
                        ViewModel.DiscordService.SetPageState("Создание ярлыка", "Быстрый запуск");
                    }
                    else
                    {
                        if (MainFrame.Visibility == Visibility.Visible && MainFrame.Content != null)
                        {
                            UpdateDiscordForPage(MainFrame.Content);
                        }
                        else
                        {
                            ViewModel.DiscordService.SetMenuState(ViewModel.SelectedVersion);
                        }
                    }
                }
            };
        }

        private void OnGameLaunched(System.Diagnostics.Process process)
        {
            Dispatcher.Invoke(() =>
            {
                if (_consoleWindow == null || !_consoleWindow.IsLoaded)
                {
                    _consoleWindow = new GameConsoleWindow();
                }

                _consoleWindow.AttachProcess(process);
                _consoleWindow.Show();
                _consoleWindow.Activate();
            });
        }

        public void AnimateNavigate(Page page)
        {
            HomeView.Visibility = Visibility.Collapsed;
            MainFrame.Visibility = Visibility.Visible;
            MainFrame.Navigate(page);

            var slideAnim = new DoubleAnimation
            {
                From = 90,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var fadeAnim = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(250)
            };

            MainFrameTransform.BeginAnimation(TranslateTransform.XProperty, slideAnim);
            MainFrame.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
        }

        public async void AnimateGoBack()
        {
            if (MainFrame.CanGoBack)
            {
                var slideOut = new DoubleAnimation
                {
                    From = 0,
                    To = 70,
                    Duration = TimeSpan.FromMilliseconds(160),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                var fadeOut = new DoubleAnimation
                {
                    From = 1,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(160)
                };

                MainFrameTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);
                MainFrame.BeginAnimation(UIElement.OpacityProperty, fadeOut);

                await System.Threading.Tasks.Task.Delay(160);

                MainFrame.GoBack();

                var slideIn = new DoubleAnimation
                {
                    From = -70,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var fadeIn = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(200)
                };

                MainFrameTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);
                MainFrame.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            }
            else
            {
                CloseSettings();
            }
        }

        private void MainFrame_Navigated(object sender, System.Windows.Navigation.NavigationEventArgs e)
        {
            UpdateDiscordForPage(e.Content);
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
                    ViewModel.DiscordService.SetPageState("В настройках", "Настройки лаунчера");
                    break;
                case ModpacksPage:
                    ViewModel.DiscordService.SetPageState("Каталог сборок", "Выбирает модпак");
                    break;
                case ModsPage:
                    ViewModel.DiscordService.SetPageState("Менеджер модов", "Поиск дополнений");
                    break;
                case ScreenshotsPage:
                    ViewModel.DiscordService.SetPageState("Галерея скриншотов", "Просматривает снимки");
                    break;
                case ProfileManagerPage profilePage:
                    string packName = profilePage.ViewModel?.Profile?.Name ?? "Модпак";
                    ViewModel.DiscordService.SetPageState("Управление сборкой", $"Редактирует «{packName}»");
                    break;
                default:
                    ViewModel.DiscordService.SetPageState("В лаунчере", "Просмотр раздела");
                    break;
            }
        }

        public void CloseSettings()
        {
            MainFrame.Visibility = Visibility.Collapsed;
            MainFrame.Content = null;

            ViewModel.ApplyCustomWallpaper();
            _ = ViewModel.LoadAccountsAsync();
            _ = ViewModel.LoadVersionsAsync();

            ViewModel.DiscordService.SetMenuState(ViewModel.SelectedVersion);

            HomeView.Opacity = 0;
            HomeView.Visibility = Visibility.Visible;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            HomeView.BeginAnimation(UIElement.OpacityProperty, fadeIn);
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

        private void AnimateOpenModpackOverlay()
        {
            ViewModel.DiscordService.SetPageState("Создание сборки", "Настраивает параметры");

            ModpackOverlay.Visibility = Visibility.Visible;
            ModpackOverlay.IsHitTestVisible = true;

            var fadeOverlay = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ModpackOverlay.BeginAnimation(UIElement.OpacityProperty, fadeOverlay);

            var scaleAnim = new DoubleAnimation
            {
                From = 0.90,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(260),
                EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut }
            };
            ModpackScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            ModpackScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);

            var slideAnim = new DoubleAnimation
            {
                From = 25,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(260),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ModpackTranslateTransform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
        }

        private void AnimateCloseModpackOverlay()
        {
            ModpackOverlay.IsHitTestVisible = false;

            var fadeOverlay = new DoubleAnimation
            {
                From = ModpackOverlay.Opacity,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOverlay.Completed += (s, e) =>
            {
                if (!ViewModel.IsModpackOverlayVisible)
                {
                    ModpackOverlay.Visibility = Visibility.Collapsed;
                    if (MainFrame.Visibility == Visibility.Visible && MainFrame.Content != null)
                    {
                        UpdateDiscordForPage(MainFrame.Content);
                    }
                    else
                    {
                        ViewModel.DiscordService.SetMenuState(ViewModel.SelectedVersion);
                    }
                }
            };
            ModpackOverlay.BeginAnimation(UIElement.OpacityProperty, fadeOverlay);

            var scaleAnim = new DoubleAnimation
            {
                From = ModpackScaleTransform.ScaleX,
                To = 0.94,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            ModpackScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            ModpackScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);

            var slideAnim = new DoubleAnimation
            {
                From = ModpackTranslateTransform.Y,
                To = 15,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            ModpackTranslateTransform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
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
                ThemeToggleBtn.Content = ThemeService.Instance.IsDarkTheme ? "\uE706" : "\uE708";
                ThemeToggleBtn.ToolTip = ThemeService.Instance.IsDarkTheme ? "Переключить на светлую тему" : "Переключить на тёмную тему";
            }
        }

        private void ThemeToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.ToggleThemeCommand.Execute(null);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e) =>
            AnimateNavigate(new SettingsPage());

        private void NavHome_Click(object sender, RoutedEventArgs e)
        {
            AudioService.Instance.PlayClickSound(SettingsService.Instance.Settings.EnableUiSounds);
            CloseSettings();
        }

        private void NavModpacks_Click(object sender, RoutedEventArgs e)
        {
            AudioService.Instance.PlayClickSound(SettingsService.Instance.Settings.EnableUiSounds);
            AnimateNavigate(new ModpacksPage());
        }

        private void NavMods_Click(object sender, RoutedEventArgs e)
        {
            AudioService.Instance.PlayClickSound(SettingsService.Instance.Settings.EnableUiSounds);
            AnimateNavigate(new ModsPage());
        }

        private void NavScreenshots_Click(object sender, RoutedEventArgs e)
        {
            AudioService.Instance.PlayClickSound(SettingsService.Instance.Settings.EnableUiSounds);
            AnimateNavigate(new ScreenshotsPage(SettingsService.Instance.Settings.GamePath));
        }

        private void NavConsole_Click(object sender, RoutedEventArgs e)
        {
            AudioService.Instance.PlayClickSound(SettingsService.Instance.Settings.EnableUiSounds);
            if (_consoleWindow == null || !_consoleWindow.IsLoaded)
            {
                _consoleWindow = new GameConsoleWindow();
            }
            _consoleWindow.Show();
            _consoleWindow.Activate();
        }

        private void VersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VersionComboBox.SelectedItem is string selectedStr && selectedStr.Contains("Создать новую сборку"))
            {
                OpenModpackOverlay();
                string lastVer = SettingsService.Instance.Settings.LastSelectedVersion;
                if (!string.IsNullOrEmpty(lastVer) && ViewModel.Versions.Contains(lastVer))
                {
                    ViewModel.SelectedVersion = lastVer;
                }
                else
                {
                    var fallback = ViewModel.Versions.FirstOrDefault(v => !v.Contains("Создать новую сборку"));
                    if (!string.IsNullOrEmpty(fallback))
                    {
                        ViewModel.SelectedVersion = fallback;
                    }
                }
            }
        }

        private void CloseModpackOverlay_Click(object sender, MouseButtonEventArgs e) =>
            ViewModel.IsModpackOverlayVisible = false;

        private void CloseShortcutOverlay_Click(object sender, MouseButtonEventArgs e) =>
            ViewModel.IsShortcutOverlayVisible = false;

        private void QuickFolderButton_Click(object sender, RoutedEventArgs e)
        {
            AudioService.Instance.PlayClickSound(SettingsService.Instance.Settings.EnableUiSounds);
            FoldersPopup.PlacementTarget = QuickFolderBtn;
            FoldersPopup.IsOpen = !FoldersPopup.IsOpen;
        }

        private void OpenFolder_Mods(object sender, RoutedEventArgs e) { FoldersPopup.IsOpen = false; ViewModel.OpenQuickFolderCommand.Execute("mods"); }
        private void OpenFolder_ResourcePacks(object sender, RoutedEventArgs e) { FoldersPopup.IsOpen = false; ViewModel.OpenQuickFolderCommand.Execute("resourcepacks"); }
        private void OpenFolder_ShaderPacks(object sender, RoutedEventArgs e) { FoldersPopup.IsOpen = false; ViewModel.OpenQuickFolderCommand.Execute("shaderpacks"); }
        private void OpenFolder_Saves(object sender, RoutedEventArgs e) { FoldersPopup.IsOpen = false; ViewModel.OpenQuickFolderCommand.Execute("saves"); }
        private void OpenFolder_Screenshots(object sender, RoutedEventArgs e) { FoldersPopup.IsOpen = false; ViewModel.OpenQuickFolderCommand.Execute("screenshots"); }
        private void OpenFolder_Logs(object sender, RoutedEventArgs e) { FoldersPopup.IsOpen = false; ViewModel.OpenQuickFolderCommand.Execute("logs"); }
        private void OpenFolder_Root(object sender, RoutedEventArgs e) { FoldersPopup.IsOpen = false; ViewModel.OpenQuickFolderCommand.Execute(""); }

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (DragDropOverlay != null) DragDropOverlay.Visibility = Visibility.Visible;
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void Window_DragLeave(object sender, DragEventArgs e)
        {
            if (DragDropOverlay != null) DragDropOverlay.Visibility = Visibility.Collapsed;
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (DragDropOverlay != null) DragDropOverlay.Visibility = Visibility.Collapsed;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            string[]? files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length == 0) return;

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
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            Dispatcher.Invoke(() => ToastService.Instance.ShowInfo("Импорт перетащенной сборки .mrpack...", "Импорт"));
                            var profile = await Services.LaunchEngine.MrPackInstaller.InstallMrPackAsync(file, settings.GamePath);
                            settings.Modpacks.Add(profile);
                            SettingsService.Instance.Save(settings);
                            await Dispatcher.InvokeAsync(async () =>
                            {
                                await ViewModel.LoadVersionsAsync();
                                ViewModel.SelectedVersion = $"⭐ {profile.Name} ({profile.Loader})";
                                ToastService.Instance.ShowSuccess($"Сборка '{profile.Name}' успешно установлена!", "Импорт");
                            });
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() => ToastService.Instance.ShowError($"Ошибка импорта: {ex.Message}", "Ошибка"));
                        }
                    });
                    return;
                }
                else if (ext == ".jar")
                {
                    string destFile = Path.Combine(targetModsDir, Path.GetFileName(file));
                    File.Copy(file, destFile, true);
                    modsCount++;
                }
                else if (ext == ".zip")
                {
                    bool isShader = false;
                    bool isResource = false;
                    try
                    {
                        using var archive = System.IO.Compression.ZipFile.OpenRead(file);
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
                        string destFile = Path.Combine(targetShaderpacksDir, Path.GetFileName(file));
                        File.Copy(file, destFile, true);
                        shadersCount++;
                    }
                    else if (isResource)
                    {
                        Directory.CreateDirectory(targetResourcepacksDir);
                        string destFile = Path.Combine(targetResourcepacksDir, Path.GetFileName(file));
                        File.Copy(file, destFile, true);
                        resourcesCount++;
                    }
                    else
                    {
                        string destFile = Path.Combine(targetModsDir, Path.GetFileName(file));
                        File.Copy(file, destFile, true);
                        modsCount++;
                    }
                }
            }

            int total = modsCount + shadersCount + resourcesCount;
            if (total > 0)
            {
                AudioService.Instance.PlayClickSound(SettingsService.Instance.Settings.EnableUiSounds);
                var parts = new System.Collections.Generic.List<string>();
                if (modsCount > 0) parts.Add($"{modsCount} модов");
                if (shadersCount > 0) parts.Add($"{shadersCount} шейдеров");
                if (resourcesCount > 0) parts.Add($"{resourcesCount} текстур-паков");
                ToastService.Instance.ShowSuccess($"Успешно импортировано: {string.Join(", ", parts)}", "Импорт");
            }
        }

        private void CloseAddServerOverlay_Click(object sender, MouseButtonEventArgs e)
        {
            ViewModel.CloseAddServerOverlayCommand.Execute(null);
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (e.ClickCount == 2)
                {
                    MaximizeButton_Click(sender, e);
                }
                else
                {
                    if (_isCustomMaximized)
                    {
                        ClearAnimations();
                        Point clickPos = e.GetPosition(this);
                        double ratio = clickPos.X / ActualWidth;
                        Width = _normalWidth;
                        Height = _normalHeight;
                        Left = Left + clickPos.X - (_normalWidth * ratio);
                        Top = Top + clickPos.Y - clickPos.Y;
                        if (OuterBorder != null) OuterBorder.CornerRadius = new CornerRadius(14);
                        if (TitleBorder != null) TitleBorder.CornerRadius = new CornerRadius(10, 10, 0, 0);
                        if (CloseBtn != null) CloseBtn.Tag = new CornerRadius(0, 10, 0, 0);
                        _isCustomMaximized = false;
                    }
                    DragMove();
                }
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DiscordService.Instance.StopRpc();
            Application.Current.Shutdown();
        }

        private void ClearAnimations()
        {
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            OuterBorder?.BeginAnimation(Border.CornerRadiusProperty, null);
            TitleBorder?.BeginAnimation(Border.CornerRadiusProperty, null);
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            var duration = TimeSpan.FromMilliseconds(220);
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

            if (!_isCustomMaximized)
            {
                _normalLeft = Left; _normalTop = Top; _normalWidth = Width; _normalHeight = Height;
                BeginAnimation(LeftProperty, new DoubleAnimation(SystemParameters.WorkArea.Left, duration) { EasingFunction = ease });
                BeginAnimation(TopProperty, new DoubleAnimation(SystemParameters.WorkArea.Top, duration) { EasingFunction = ease });
                BeginAnimation(WidthProperty, new DoubleAnimation(SystemParameters.WorkArea.Width, duration) { EasingFunction = ease });
                BeginAnimation(HeightProperty, new DoubleAnimation(SystemParameters.WorkArea.Height, duration) { EasingFunction = ease });
                if (OuterBorder != null) AnimateCorners(OuterBorder, new CornerRadius(0), duration, ease);
                if (TitleBorder != null) AnimateCorners(TitleBorder, new CornerRadius(0), duration, ease);
                if (CloseBtn != null) CloseBtn.Tag = new CornerRadius(0);
                _isCustomMaximized = true;
            }
            else
            {
                BeginAnimation(LeftProperty, new DoubleAnimation(_normalLeft, duration) { EasingFunction = ease });
                BeginAnimation(TopProperty, new DoubleAnimation(_normalTop, duration) { EasingFunction = ease });
                BeginAnimation(WidthProperty, new DoubleAnimation(_normalWidth, duration) { EasingFunction = ease });
                BeginAnimation(HeightProperty, new DoubleAnimation(_normalHeight, duration) { EasingFunction = ease });
                if (OuterBorder != null) AnimateCorners(OuterBorder, new CornerRadius(14), duration, ease);
                if (TitleBorder != null) AnimateCorners(TitleBorder, new CornerRadius(10, 10, 0, 0), duration, ease);
                if (CloseBtn != null) CloseBtn.Tag = new CornerRadius(0, 10, 0, 0);
                _isCustomMaximized = false;
            }
        }

        private void AnimateCorners(Border border, CornerRadius to, TimeSpan duration, IEasingFunction ease)
        {
            var anim = new CornerRadiusAnimation { From = border.CornerRadius, To = to, Duration = duration, EasingFunction = ease };
            border.BeginAnimation(Border.CornerRadiusProperty, anim);
        }

        private void LauncherTrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e) => RestoreLauncher();
        private void TrayRestore_Click(object sender, RoutedEventArgs e) => RestoreLauncher();
        private void TrayExit_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

        private void RestoreLauncher()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            LauncherTrayIcon.Visibility = Visibility.Collapsed;
        }
    }
}