using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using MinecraftLauncher.Helpers;

namespace MinecraftLauncher.Common
{
    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<object?, Task> _execute;
        private readonly Predicate<object?>? _canExecute;
        private readonly string _name;
        private bool _isExecuting;

        public event Action<Exception>? CommandFailed;

        public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
            : this(_ => execute(), canExecute != null ? _ => canExecute() : null)
        {
            ArgumentNullException.ThrowIfNull(execute);
        }

        public AsyncRelayCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null, string? name = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
            _name = name ?? execute.Method.Name ?? "AsyncCommand";
        }

        public bool IsExecuting
        {
            get => _isExecuting;
            private set
            {
                if (_isExecuting != value)
                {
                    _isExecuting = value;
                    RaiseCanExecuteChanged();
                }
            }
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            return !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);
        }

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter)) return;

            try
            {
                IsExecuting = true;
                await _execute(parameter);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("AsyncRelayCommand", $"Command '{_name}' failed", ex);
                CommandFailed?.Invoke(ex);
            }
            finally
            {
                IsExecuting = false;
            }
        }

        public async Task ExecuteAsync(object? parameter)
        {
            if (!CanExecute(parameter)) return;

            try
            {
                IsExecuting = true;
                await _execute(parameter);
            }
            finally
            {
                IsExecuting = false;
            }
        }

        public void RaiseCanExecuteChanged()
        {
            if (Dispatcher.UIThread.CheckAccess())
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            else
                Dispatcher.UIThread.Post(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
        }
    }

    public class AsyncRelayCommand<T> : ICommand
    {
        private readonly Func<T?, Task> _execute;
        private readonly Predicate<T?>? _canExecute;
        private readonly string _name;
        private bool _isExecuting;

        public event Action<Exception>? CommandFailed;

        public AsyncRelayCommand(Func<T?, Task> execute, Predicate<T?>? canExecute = null, string? name = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
            _name = name ?? execute.Method.Name ?? "AsyncCommand<T>";
        }

        public bool IsExecuting
        {
            get => _isExecuting;
            private set
            {
                if (_isExecuting != value)
                {
                    _isExecuting = value;
                    RaiseCanExecuteChanged();
                }
            }
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter)
        {
            if (_isExecuting) return false;

            if (parameter == null && typeof(T).IsValueType)
                return _canExecute?.Invoke(default) ?? true;

            return _canExecute?.Invoke((T?)parameter) ?? true;
        }

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter)) return;

            try
            {
                IsExecuting = true;
                if (parameter == null && typeof(T).IsValueType)
                {
                    await _execute(default);
                    return;
                }
                await _execute((T?)parameter);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("AsyncRelayCommand<T>", $"Command '{_name}' failed", ex);
                CommandFailed?.Invoke(ex);
            }
            finally
            {
                IsExecuting = false;
            }
        }

        public async Task ExecuteAsync(T? parameter)
        {
            if (!CanExecute(parameter)) return;

            try
            {
                IsExecuting = true;
                await _execute(parameter);
            }
            finally
            {
                IsExecuting = false;
            }
        }

        public void RaiseCanExecuteChanged()
        {
            if (Dispatcher.UIThread.CheckAccess())
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            else
                Dispatcher.UIThread.Post(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
        }
    }
}
