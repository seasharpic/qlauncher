using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Services;

namespace MinecraftLauncher.Views.Windows
{
    public partial class SkinPreviewWindow : Window
    {
        private static readonly ILocalizationService Loc = LocalizationService.Instance;

        private readonly bool _isElyBy;
        private readonly string _nickname;
        private readonly string _uuid;

        private SkinModel3DBuilder.SkinRaster? _raster;
        private string? _cachePath;
        private bool _slim;

        private double _yaw = 20;
        private double _pitch = -10;
        private double _zoom = 1.0;

        private bool _dragging;
        private Point _dragStart;
        private double _dragStartYaw;
        private double _dragStartPitch;

        private readonly DispatcherTimer _autoRotateTimer;

        public SkinPreviewWindow() : this(false, "Steve") { }

        public SkinPreviewWindow(bool isElyBy, string nickname, string uuid = "")
        {
            InitializeComponent();
            _isElyBy = isElyBy;
            _nickname = nickname;
            _uuid = uuid;

            _autoRotateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _autoRotateTimer.Tick += (_, _) =>
            {
                if (_dragging || _raster == null) return;
                _yaw = (_yaw + 2.0) % 360.0;
                RenderFrame();
            };

            _ = LoadSkinAsync();
        }

        private async Task LoadSkinAsync()
        {
            StatusText.Text = Loc.GetString("Str_Skin3D_Loading", "Loading skin…");

            var (image, path) = await SkinProvider.GetSkinAsync(_isElyBy, _nickname, _uuid);
            _cachePath = path;

            if (image == null)
            {
                SkinImage.IsVisible = false;
                StatusText.Text = Loc.GetString("Str_Skin3D_NotLoaded", "Failed to load the skin");
                return;
            }

            StatusText.Text = string.Empty;
            _raster = SkinModel3DBuilder.Rasterize(image);
            _slim = SkinModel3DBuilder.DetectSlim(_raster);
            SlimCheckBox.IsChecked = _slim;

            RenderFrame();

            if (AutoRotateCheckBox.IsChecked == true)
            {
                _autoRotateTimer.Start();
            }
        }

        private void RenderFrame()
        {
            if (_raster == null) return;
            var bmp = SkinModel3DBuilder.RenderToBitmap(_raster, _slim, _yaw, _pitch, 280, 360);
            SkinImage.Source = bmp;
            SkinImage.Width = 280 * _zoom;
            SkinImage.Height = 360 * _zoom;
        }

        private void Viewport_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(ViewportHost).Properties.IsLeftButtonPressed) return;
            _dragging = true;
            _dragStart = e.GetPosition(ViewportHost);
            _dragStartYaw = _yaw;
            _dragStartPitch = _pitch;
            e.Pointer.Capture(ViewportHost);
        }

        private void Viewport_PointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_dragging) return;
            var pos = e.GetPosition(ViewportHost);
            _yaw = _dragStartYaw + (pos.X - _dragStart.X) * 0.6;
            _pitch = Math.Clamp(_dragStartPitch - (pos.Y - _dragStart.Y) * 0.5, -80, 80);
            RenderFrame();
        }

        private void Viewport_PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            e.Pointer.Capture(null);
        }

        private void Viewport_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            _zoom = Math.Clamp(_zoom * (e.Delta.Y > 0 ? 1.08 : 0.92), 0.6, 1.8);
            RenderFrame();
            e.Handled = true;
        }

        private void SlimCheckBox_Changed(object? sender, RoutedEventArgs e)
        {
            if (_autoRotateTimer == null) return;
            AudioService.Instance.PlayClickSound(SettingsService.Instance.Settings.EnableUiSounds);
            _slim = SlimCheckBox.IsChecked == true;
            RenderFrame();
        }

        private void AutoRotateCheckBox_Changed(object? sender, RoutedEventArgs e)
        {
            if (_autoRotateTimer == null) return;
            AudioService.Instance.PlayClickSound(SettingsService.Instance.Settings.EnableUiSounds);
            bool on = AutoRotateCheckBox.IsChecked == true;
            if (on && _raster != null && !_dragging)
                _autoRotateTimer.Start();
            else
                _autoRotateTimer.Stop();
        }

        private async void SaveButton_Click(object? sender, RoutedEventArgs e)
        {
            AudioService.Instance.PlayClickSound(SettingsService.Instance.Settings.EnableUiSounds);

            if (_cachePath == null || !File.Exists(_cachePath))
            {
                ToastService.Instance.ShowWarning(
                    Loc.GetString("Str_Skin3D_NotLoaded", "Failed to load the skin"),
                    "Str_Skin3D_Title");
                return;
            }

            var path = await UiHelper.SaveFileAsync(
                Loc.GetString("Str_Skin3D_Save", "Save skin"),
                $"skin_{_nickname}.png",
                "png",
                new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } });

            if (string.IsNullOrEmpty(path)) return;

            try
            {
                File.Copy(_cachePath, path, overwrite: true);
                ToastService.Instance.ShowInfo(
                    Loc.Format("Str_Skin3D_Saved", path),
                    "Str_Skin3D_Title");
            }
            catch (Exception ex)
            {
                ToastService.Instance.ShowError(ex.Message, "Str_Skin3D_Title");
            }
        }

        private void OpenButton_Click(object? sender, RoutedEventArgs e)
        {
            AudioService.Instance.PlayClickSound(SettingsService.Instance.Settings.EnableUiSounds);

            if (_cachePath == null || !File.Exists(_cachePath)) return;

            try
            {
                string dir = Path.GetDirectoryName(_cachePath) ?? "";
                if (OperatingSystem.IsWindows())
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_cachePath}\""));
                else if (OperatingSystem.IsMacOS())
                    Process.Start("open", dir);
                else
                    Process.Start("xdg-open", dir);
            }
            catch { }
        }

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                try { BeginMoveDrag(e); } catch { }
            }
        }

        private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _autoRotateTimer.Stop();
        }
    }
}
