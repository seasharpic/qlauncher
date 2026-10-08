using System;
using System.Diagnostics;
using System.Windows;
using MinecraftLauncher.Common;
using MinecraftLauncher.Services;

namespace MinecraftLauncher.ViewModels
{
    public class GameConsoleViewModel : ViewModelBase
    {
        // Локализация для строк, которые собираются в коде.
        private readonly ILocalizationService _loc = LocalizationService.Instance;

        private string _processStatusText = LocalizationService.Instance.GetString("Str_Console_Starting");
        private bool _autoScroll = true;

        public string ProcessStatusText
        {
            get => _processStatusText;
            set => SetProperty(ref _processStatusText, value);
        }

        public bool AutoScroll
        {
            get => _autoScroll;
            set => SetProperty(ref _autoScroll, value);
        }

        public event Action<string, bool>? LogLineReceived;
        public event Action? RequestClear;
        public event Action? RequestCopy;

        public RelayCommand ClearLogCommand { get; }
        public RelayCommand CopyLogCommand { get; }

        public GameConsoleViewModel()
        {
            ClearLogCommand = new RelayCommand(() => RequestClear?.Invoke());
            CopyLogCommand = new RelayCommand(() => RequestCopy?.Invoke());
        }

        private Process? _attachedProcess;
        private readonly object _gate = new();

        /// <summary>
        /// Наблюдение за процессом игры для окна консоли.
        ///
        /// Чтение пайпов теперь всегда включено: если раньше консоль не открывали,
        /// никто не подписывался на вывод, буфер пайпа переполнялся, и процесс игры
        /// блокировался. Само чтение выполняет MainViewModel — здесь только
        /// подписка на событие ProcessOutputLine.
        /// </summary>
        public void AttachProcess(Process process)
        {
            DetachProcess();

            _attachedProcess = process;
            ProcessStatusText = _loc.Format("Str_Console_Running", process.Id);

            process.Exited += OnProcessExited;
        }

        private void OnProcessExited(object? sender, EventArgs e)
        {
            if (_attachedProcess is not { } process) return;

            int exitCode;
            try
            {
                exitCode = process.ExitCode;
            }
            catch (InvalidOperationException)
            {
                return;
            }

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                ProcessStatusText = _loc.Format("Str_Console_Exited", exitCode);
                LogLineReceived?.Invoke(_loc.Format("Str_Console_GameExited", exitCode), exitCode != 0);
            }));
        }

        public void DetachProcess()
        {
            if (_attachedProcess is { } process)
            {
                process.Exited -= OnProcessExited;
            }

            _attachedProcess = null;
        }

        /// <summary>
        /// Привязывает окно консоли к потоку вывода, который читает MainViewModel.
        /// Собственных подписок на OutputDataReceived больше нет, чтобы не было
        /// двойного чтения одних и тех же пайпов.
        /// </summary>
        public void SubscribeToOutput(Action<string, bool> handler)
        {
            LogLineReceived -= handler;
            LogLineReceived += handler;
        }

        public void UnsubscribeFromOutput(Action<string, bool> handler)
        {
            LogLineReceived -= handler;
        }
    }
}
