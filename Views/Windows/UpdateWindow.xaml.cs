using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MinecraftLauncher.Models;
using MinecraftLauncher.Services;

namespace MinecraftLauncher.Views.Windows
{
    public partial class UpdateWindow : Window
    {
        public ReleaseInfo Release { get; }
        private CancellationTokenSource? _downloadCts;
        private string _downloadedFilePath = "";

        public UpdateWindow(ReleaseInfo release)
        {
            InitializeComponent();
            Release = release;
            Loaded += UpdateWindow_Loaded;
        }

        private void UpdateWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Строки собираются в коде, поэтому подстановка идёт через
            // LocalizationService: DynamicResource здесь не работает.
            var loc = LocalizationService.Instance;

            VersionBadgeText.Text = $"{UpdateService.CurrentVersion} -> {Release.TagName}";
            ReleaseTypeBadgeText.Text = Release.ReleaseTypeBadge;
            ReleaseDateText.Text = loc.Format("Str_Update_ReleaseDate", Release.ReleaseDateFormatted);
            AuthorText.Text = loc.Format("Str_Update_Author", Release.Author);
            MirrorBadgeText.Text = loc.Format("Str_Update_Mirror", Release.ActiveMirror);
            SizeText.Text = string.IsNullOrWhiteSpace(Release.FileSizeFormatted)
                ? ""
                : loc.Format("Str_Update_Size", Release.FileSizeFormatted);
            ChangelogTextBlock.Text = Release.FormattedChangelog;
            Sha256Text.Text = string.IsNullOrEmpty(Release.Sha256)
                ? loc.GetString("Str_Update_Sha256PendingCode")
                : $"SHA-256: {Release.Sha256}";
        }

        private void Header_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void OpenBrowserBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = !string.IsNullOrEmpty(Release.HtmlUrl) ? Release.HtmlUrl : "https://github.com/dyagyatis/QLauncher/releases";
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch { }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            CancelDownload();
            Close();
        }

        private void Later_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SkipVersion_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = SettingsService.Instance.Settings;
                settings.SkippedVersion = Release.TagName;
                SettingsService.Instance.Save(settings);
                ToastService.Instance.ShowInfo(
                    LocalizationService.Instance.Format("Str_Update_SkippedToast", Release.TagName),
                    LocalizationService.Instance.GetString("Str_Update_Title"));
            }
            catch { }
            Close();
        }

        private void CopyChangelog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string text = $"QLauncher {Release.TagName} Changelog\n{Release.FormattedChangelog}";
                Clipboard.SetText(text);
                ToastService.Instance.ShowSuccess(
                    LocalizationService.Instance.GetString("Str_Update_CopiedToast"),
                    LocalizationService.Instance.GetString("Str_Update_TitleSingle"));
            }
            catch { }
        }

        private async void StartUpdate_Click(object sender, RoutedEventArgs e)
        {
            InitialActionsPanel.Visibility = Visibility.Collapsed;
            DownloadProgressPanel.Visibility = Visibility.Visible;
            ReadyToRestartPanel.Visibility = Visibility.Collapsed;

            _downloadCts = new CancellationTokenSource();
            var loc = LocalizationService.Instance;
            string tempDir = Path.Combine(Path.GetTempPath(), "QLauncher_Update");
            Directory.CreateDirectory(tempDir);
            _downloadedFilePath = Path.Combine(tempDir, Release.FileName);

            var progress = new Progress<DownloadProgressReport>(report =>
            {
                double maxWidth = 420;
                DownloadProgressBarFill.Width = Math.Max(4, report.Percentage * maxWidth);

                double mbRecv = report.BytesReceived / (1024.0 * 1024.0);
                double mbTotal = report.TotalBytes > 0 ? report.TotalBytes / (1024.0 * 1024.0) : 0;
                double speedMb = report.SpeedBytesPerSec / (1024.0 * 1024.0);

                int pct = (int)(report.Percentage * 100);
                DownloadStatusText.Text = loc.Format(
                    "Str_Update_DownloadProgress",
                    pct,
                    $"{mbRecv:F1}",
                    mbTotal > 0 ? $"{mbTotal:F1}" : "???",
                    report.ActiveMirror);

                string etaText = report.EstimatedTimeRemaining > TimeSpan.Zero
                    ? loc.Format("Str_Update_EtaSeconds", (int)Math.Ceiling(report.EstimatedTimeRemaining.TotalSeconds))
                    : loc.GetString("Str_Update_EtaPending");

                DownloadSpeedEtaText.Text = loc.Format("Str_Update_SpeedEta", $"{speedMb:F1}", etaText);
            });

            DownloadStatusText.Text = loc.Format("Str_Update_Connecting", Release.ActiveMirror);

            bool success = await UpdateService.Instance.DownloadUpdateWithFallbackAsync(Release, _downloadedFilePath, progress, _downloadCts.Token);

            if (_downloadCts.IsCancellationRequested) return;

            if (!success || !File.Exists(_downloadedFilePath))
            {
                DownloadStatusText.Text = loc.GetString("Str_Update_DownloadError");
                DownloadSpeedEtaText.Text = loc.GetString("Str_Update_DownloadErrorHint");
                await Task.Delay(2500);
                DownloadProgressPanel.Visibility = Visibility.Collapsed;
                InitialActionsPanel.Visibility = Visibility.Visible;
                return;
            }

            // Проверка целостности SHA-256
            DownloadStatusText.Text = loc.GetString("Str_Update_VerifyingHash");
            bool hashValid = await Task.Run(() => UpdateService.Instance.VerifySha256(_downloadedFilePath, Release.Sha256));

            if (!hashValid)
            {
                DownloadStatusText.Text = loc.GetString("Str_Update_HashMismatch");
                DownloadSpeedEtaText.Text = loc.GetString("Str_Update_Corrupted");
                try { File.Delete(_downloadedFilePath); } catch { }
                await Task.Delay(3000);
                DownloadProgressPanel.Visibility = Visibility.Collapsed;
                InitialActionsPanel.Visibility = Visibility.Visible;
                return;
            }

            DownloadProgressPanel.Visibility = Visibility.Collapsed;
            ReadyToRestartPanel.Visibility = Visibility.Visible;
        }

        private void CancelDownload_Click(object sender, RoutedEventArgs e)
        {
            CancelDownload();
            DownloadProgressPanel.Visibility = Visibility.Collapsed;
            InitialActionsPanel.Visibility = Visibility.Visible;
        }

        private void CancelDownload()
        {
            if (_downloadCts != null && !_downloadCts.IsCancellationRequested)
            {
                _downloadCts.Cancel();
                _downloadCts.Dispose();
                _downloadCts = null;
            }
        }

        private void Restart_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_downloadedFilePath) && File.Exists(_downloadedFilePath))
            {
                UpdateService.Instance.ApplyUpdateAndRestart(_downloadedFilePath, Release.TagName);
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (DownloadProgressPanel.Visibility == Visibility.Visible)
                {
                    CancelDownload_Click(this, new RoutedEventArgs());
                }
                else
                {
                    Close();
                }
            }
            else if (e.Key == Key.Enter)
            {
                if (InitialActionsPanel.Visibility == Visibility.Visible)
                {
                    StartUpdate_Click(this, new RoutedEventArgs());
                }
                else if (ReadyToRestartPanel.Visibility == Visibility.Visible)
                {
                    Restart_Click(this, new RoutedEventArgs());
                }
            }
        }
    }
}
