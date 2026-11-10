using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Microsoft.Win32;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Services;

namespace MinecraftLauncher.Views.Windows
{
    /// <summary>
    /// 3D-превью скина выбранного аккаунта: Viewport3D с боксовой моделью
    /// игрока, вращение мышью, зум колесом, автоповорот, переключатель
    /// slim/classic и кнопки сохранения/открытия PNG-скина.
    /// </summary>
    public partial class SkinPreviewWindow : Window
    {
        private static readonly ILocalizationService Loc = LocalizationService.Instance;

        private readonly bool _isElyBy;
        private readonly string _nickname;
        private readonly string _uuid;

        private BitmapImage? _skin;
        private string? _cachePath;
        private bool _slim;

        private double _dist = 68;
        private double _yaw;
        private double _pitch;

        private bool _dragging;
        private Point _dragStart;
        private double _dragStartYaw;
        private double _dragStartPitch;

        private RotateTransform3D _yawTransform = null!;
        private RotateTransform3D _pitchTransform = null!;

        private readonly DispatcherTimer _autoRotateTimer;

        public SkinPreviewWindow(bool isElyBy, string nickname, string uuid = "")
        {
            InitializeComponent();
            _isElyBy = isElyBy;
            _nickname = nickname;
            _uuid = uuid;

            _autoRotateTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _autoRotateTimer.Tick += (s, e) =>
            {
                if (_dragging) return;
                _yaw += 1.4;
                ApplyTransforms();
            };

            _ = LoadSkinAsync();
        }

        private async System.Threading.Tasks.Task LoadSkinAsync()
        {
            StatusText.Text = Loc.GetString("Str_Skin3D_Loading", "Loading skin…");

            var (image, path) = await SkinProvider.GetSkinAsync(_isElyBy, _nickname, _uuid);
            _cachePath = path;

            if (image == null)
            {
                Viewport.Visibility = Visibility.Collapsed;
                StatusText.Text = Loc.GetString("Str_Skin3D_NotLoaded", "Failed to load the skin");
                return;
            }

            _skin = image;
            StatusText.Text = string.Empty;

            // Сначала автоопределение ширины рук, далее пользователь может
            // переключить вручную: у HD-скинов детект надёжен не всегда.
            var raster = SkinModel3DBuilder.Rasterize(image);
            _slim = SkinModel3DBuilder.DetectSlim(raster);
            SlimCheckBox.IsChecked = _slim;

            BuildModel();

            if (AutoRotateCheckBox.IsChecked == true)
            {
                _autoRotateTimer.Start();
            }
        }

        private void BuildModel()
        {
            if (_skin == null) return;

            var raster = SkinModel3DBuilder.Rasterize(_skin);
            var model = SkinModel3DBuilder.Build(raster, _slim);

            // Вращение как трекбол: pitch — внутренняя (относительная) ось X,
            // yaw — внешняя мировая ось Y. Порядок в Transform3DGroup: yaw, pitch.
            _pitchTransform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), _pitch));
            _yawTransform = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), _yaw));

            var transform = new Transform3DGroup();
            transform.Children.Add(_yawTransform);
            transform.Children.Add(_pitchTransform);

            ModelContent.Content = model;
            ModelContent.Transform = transform;
        }

        private void ApplyTransforms()
        {
            if (_yawTransform == null || _pitchTransform == null) return;

            ((AxisAngleRotation3D)_yawTransform.Rotation).Angle = _yaw;
            ((AxisAngleRotation3D)_pitchTransform.Rotation).Angle = _pitch;
        }

        // --- Трекбол ---

        private void Viewport_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragging = true;
            _dragStart = e.GetPosition(Viewport);
            _dragStartYaw = _yaw;
            _dragStartPitch = _pitch;
            Viewport.CaptureMouse();
        }

        private void Viewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var pos = e.GetPosition(Viewport);
            _yaw = _dragStartYaw + (pos.X - _dragStart.X) * 0.5;
            _pitch = Clamp(_dragStartPitch - (pos.Y - _dragStart.Y) * 0.5, -90, 90);
            ApplyTransforms();
        }

        private void Viewport_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            Viewport.ReleaseMouseCapture();
        }

        private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            _dist = Clamp(_dist * (e.Delta > 0 ? 0.92 : 1.08), 28, 140);
            Camera.Position = new Point3D(0, 0, -_dist);
            e.Handled = true;
        }

        // --- Футер ---

        private void SlimCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            // Checked/Unchecked срабатывают и во время InitializeComponent
            // (положение галочки из XAML) — до присвоения полей конструктором.
            if (_autoRotateTimer == null) return;

            AudioService.Instance.PlayClickSound(SettingsService.Instance.Settings.EnableUiSounds);
            _slim = SlimCheckBox.IsChecked == true;
            BuildModel();
        }

        private void AutoRotateCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_autoRotateTimer == null) return;

            AudioService.Instance.PlayClickSound(SettingsService.Instance.Settings.EnableUiSounds);
            bool on = AutoRotateCheckBox.IsChecked == true;
            if (on && _skin != null && !_dragging)
            {
                _autoRotateTimer.Start();
            }
            else
            {
                _autoRotateTimer.Stop();
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            AudioService.Instance.PlayClickSound(SettingsService.Instance.Settings.EnableUiSounds);

            if (_cachePath == null || !File.Exists(_cachePath))
            {
                ToastService.Instance.ShowWarning(
                    Loc.GetString("Str_Skin3D_NotLoaded", "Failed to load the skin"),
                    "Str_Skin3D_Title");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "PNG|*.png",
                FileName = $"skin_{_nickname}.png"
            };

            if (dialog.ShowDialog(this) != true) return;

            try
            {
                File.Copy(_cachePath, dialog.FileName, overwrite: true);
                ToastService.Instance.ShowInfo(
                    Loc.Format("Str_Skin3D_Saved", dialog.FileName),
                    "Str_Skin3D_Title");
            }
            catch (Exception ex)
            {
                ToastService.Instance.ShowError(ex.Message, "Str_Skin3D_Title");
            }
        }

        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            AudioService.Instance.PlayClickSound(SettingsService.Instance.Settings.EnableUiSounds);

            if (_cachePath == null || !File.Exists(_cachePath)) return;

            try
            {
                using var _ = Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_cachePath}\""));
            }
            catch { }
        }

        // --- Оболочка окна ---

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _autoRotateTimer.Stop();
        }

        private static double Clamp(double value, double min, double max) =>
            value < min ? min : (value > max ? max : value);
    }
}
