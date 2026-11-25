using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Services;
using MinecraftLauncher.ViewModels;

namespace MinecraftLauncher.Views.Windows
{
    public partial class GameConsoleWindow : Window
    {
        private static readonly ILocalizationService _loc = LocalizationService.Instance;
        private readonly List<string> _rawLines = new();
        private MainViewModel? _outputSource;

        private static readonly IBrush ErrorBrush = new ImmutableSolidColorBrush(Color.FromRgb(255, 99, 99));
        private static readonly IBrush WarnBrush = new ImmutableSolidColorBrush(Color.FromRgb(255, 200, 80));
        private static readonly IBrush InfoBrush = new ImmutableSolidColorBrush(Color.FromRgb(160, 220, 255));
        private static readonly IBrush DefaultBrush = new ImmutableSolidColorBrush(Color.FromRgb(210, 210, 220));
        private static readonly FontFamily MonoFont = new("Consolas, Monospace, Courier New");

        private const int MaxLogLines = 3000;

        public GameConsoleViewModel ViewModel { get; }

        public GameConsoleWindow()
        {
            InitializeComponent();
            ViewModel = new GameConsoleViewModel();
            DataContext = ViewModel;

            ViewModel.LogLineReceived += OnLogLineReceived;
            ViewModel.RequestClear += OnRequestClear;
            ViewModel.RequestCopy += OnRequestCopy;
            ViewModel.RequestShare += OnRequestShare;
        }

        public void AttachProcess(Process process) => ViewModel.AttachProcess(process);

        public void BindOutput(MainViewModel viewModel)
        {
            UnbindOutput();
            if (viewModel == null) return;
            _outputSource = viewModel;
            _outputSource.ProcessOutputLine += OnProcessOutputLine;
        }

        public void UnbindOutput()
        {
            if (_outputSource != null)
            {
                _outputSource.ProcessOutputLine -= OnProcessOutputLine;
                _outputSource = null;
            }
        }

        private void OnProcessOutputLine(string? line)
        {
            if (string.IsNullOrEmpty(line)) return;
            OnLogLineReceived(line, false);
        }

        private void OnLogLineReceived(string line, bool isError)
        {
            Dispatcher.UIThread.Post(() =>
            {
                IBrush brush;
                if (isError || line.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase) || line.Contains("Exception", StringComparison.OrdinalIgnoreCase))
                    brush = ErrorBrush;
                else if (line.Contains("[WARN]", StringComparison.OrdinalIgnoreCase))
                    brush = WarnBrush;
                else if (line.Contains("[INFO]", StringComparison.OrdinalIgnoreCase))
                    brush = InfoBrush;
                else
                    brush = DefaultBrush;

                _rawLines.Add(line);
                LogLinesPanel.Children.Add(new SelectableTextBlock
                {
                    Text = line,
                    Foreground = brush,
                    FontFamily = MonoFont,
                    FontSize = 12,
                    TextWrapping = TextWrapping.NoWrap
                });

                while (LogLinesPanel.Children.Count > MaxLogLines)
                {
                    LogLinesPanel.Children.RemoveAt(0);
                    if (_rawLines.Count > 0) _rawLines.RemoveAt(0);
                }

                if (ViewModel.AutoScroll)
                {
                    LogScrollViewer.ScrollToEnd();
                }
            });
        }

        private void OnRequestClear()
        {
            LogLinesPanel.Children.Clear();
            _rawLines.Clear();
        }

        private async void OnRequestCopy()
        {
            await UiHelper.SetClipboardTextAsync(string.Join(Environment.NewLine, _rawLines));
            ToastService.Instance.ShowSuccess(_loc.GetString("Str_Console_LogCopied"), _loc.GetString("Str_T_Console"));
        }

        private async void OnRequestShare()
        {
            string text = string.Join(Environment.NewLine, _rawLines);
            if (string.IsNullOrWhiteSpace(text))
            {
                ToastService.Instance.ShowInfo(_loc.GetString("Str_Console_LogEmpty"), _loc.GetString("Str_T_Console"));
                return;
            }

            try
            {
                string url = await LogShareService.UploadLogAsync(text).ConfigureAwait(true);
                await UiHelper.SetClipboardTextAsync(url);
                ToastService.Instance.ShowSuccess(
                    _loc.Format("Str_Console_LogShared", url),
                    _loc.GetString("Str_T_Console"));
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("GameConsoleWindow", "Log share failed", ex);
                ToastService.Instance.ShowError(
                    _loc.Format("Str_Console_LogShareError", ex.Message),
                    _loc.GetString("Str_T_Error"));
            }
        }

        private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        }

        private void MinimizeButton_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void MaximizeButton_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void CloseButton_Click(object? sender, RoutedEventArgs e) => Hide();
    }
}
