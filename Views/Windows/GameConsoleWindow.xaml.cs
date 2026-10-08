using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MinecraftLauncher.Services;
using MinecraftLauncher.ViewModels;

namespace MinecraftLauncher.Views.Windows
{
    public partial class GameConsoleWindow : Window
    {
        // Локализация для строк, которые собираются в коде.
        private static readonly ILocalizationService _loc = LocalizationService.Instance;

        public GameConsoleViewModel ViewModel { get; }

        public GameConsoleWindow()
        {
            InitializeComponent();
            ViewModel = new GameConsoleViewModel();
            DataContext = ViewModel;

            ViewModel.LogLineReceived += OnLogLineReceived;
            ViewModel.RequestClear += OnRequestClear;
            ViewModel.RequestCopy += OnRequestCopy;
        }

        public void AttachProcess(Process process)
        {
            ViewModel.AttachProcess(process);
        }

        /// <summary>
        /// Подписывает окно на поток вывода, который читает MainViewModel.
        /// Раньше окно само подписывалось на OutputDataReceived процесса, но
        /// только если его открывали — то есть чтение пайпов зависело от наличия окна.
        /// </summary>
        public void BindOutput(MainViewModel viewModel)
        {
            UnbindOutput();

            if (viewModel == null) return;

            // Подписка именно на событие экземпляра: передавать событие как
            // Action<> нельзя, это даёт ошибку CS0070.
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

        private MainViewModel? _outputSource;

        private void OnProcessOutputLine(string? line)
        {
            if (string.IsNullOrEmpty(line)) return;

            // isError всегда false: раздельные потоки уже сведены в один поток
            // событий MainViewModel, а уровень подсвечивается по префиксу строки.
            OnLogLineReceived(line, false);
        }

        // Кисти создаются один раз: раньше на каждую строку выделялся новый
        // SolidColorBrush, и при тысячах строк в минуту это заметная нагрузка на GC.
        private static readonly SolidColorBrush ErrorBrush = Frozen(Color.FromRgb(255, 99, 99));
        private static readonly SolidColorBrush WarnBrush = Frozen(Color.FromRgb(255, 200, 80));
        private static readonly SolidColorBrush InfoBrush = Frozen(Color.FromRgb(160, 220, 255));
        private static readonly SolidColorBrush DefaultBrush = Frozen(Color.FromRgb(210, 210, 220));

        /// <summary>
        /// Верхняя граница числа строк в консоли. Minecraft и FML во время старта
        /// выдают тысячи строк в секунду; раньше коллекция Inlines росла без
        /// ограничения всю сессию и подвешивала диспетчер синхронным Invoke на строку.
        /// </summary>
        private const int MaxLogLines = 5000;

        private static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private void OnLogLineReceived(string line, bool isError)
        {
            // Dispatcher.Invoke блокирует вызывающий поток (это поток чтения вывода
            // процесса). BeginInvoke не блокирует и копит строки в очередь диспетчера.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SolidColorBrush brush;
                if (isError || line.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase) || line.Contains("Exception", StringComparison.OrdinalIgnoreCase))
                {
                    brush = ErrorBrush;
                }
                else if (line.Contains("[WARN]", StringComparison.OrdinalIgnoreCase))
                {
                    brush = WarnBrush;
                }
                else if (line.Contains("[INFO]", StringComparison.OrdinalIgnoreCase))
                {
                    brush = InfoBrush;
                }
                else
                {
                    brush = DefaultBrush;
                }

                var run = new Run(line + "\n") { Foreground = brush };
                LogParagraph.Inlines.Add(run);

                // Обрезаем коллекцию: иначе она растёт без границ всю сессию.
                // InlineCollection не поддерживает RemoveAt, поэтому пересобираем
                // хвост через First()/Last() на устойчивых границах.
                while (LogParagraph.Inlines.Count > MaxLogLines)
                {
                    LogParagraph.Inlines.Remove(LogParagraph.Inlines.First());
                }

                if (ViewModel.AutoScroll)
                {
                    LogRichTextBox.ScrollToEnd();
                }
            }));
        }

        private void OnRequestClear()
        {
            LogParagraph.Inlines.Clear();
        }

        private void OnRequestCopy()
        {
            var textRange = new TextRange(LogRichTextBox.Document.ContentStart, LogRichTextBox.Document.ContentEnd);
            Clipboard.SetText(textRange.Text);
            ToastService.Instance.ShowSuccess(_loc.GetString("Str_Console_LogCopied"), _loc.GetString("Str_T_Console"));
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed) DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void MaximizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();
    }
}
