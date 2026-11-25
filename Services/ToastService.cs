using Avalonia.Controls;
using Avalonia.Threading;
using MinecraftLauncher.Views.Controls;

namespace MinecraftLauncher.Services
{
    public interface IToastService
    {
        void RegisterContainer(Panel container);
        void ShowSuccess(string message, string titleKey = "Str_Toast_Success");
        void ShowError(string message, string titleKey = "Str_Toast_Error");
        void ShowWarning(string message, string titleKey = "Str_Toast_Warning");
        void ShowInfo(string message, string titleKey = "Str_Toast_Info");
    }

    public class ToastService : IToastService
    {
        private Panel? _toastContainer;

        public static ToastService Instance { get; } = new ToastService();

        public void RegisterContainer(Panel container)
        {
            _toastContainer = container;
        }

        public void ShowSuccess(string message, string titleKey = "Str_Toast_Success") =>
            ShowToast(titleKey, message, ToastType.Success);

        public void ShowError(string message, string titleKey = "Str_Toast_Error") =>
            ShowToast(titleKey, message, ToastType.Error);

        public void ShowWarning(string message, string titleKey = "Str_Toast_Warning") =>
            ShowToast(titleKey, message, ToastType.Warning);

        public void ShowInfo(string message, string titleKey = "Str_Toast_Info") =>
            ShowToast(titleKey, message, ToastType.Info);

        private void ShowToast(string titleKey, string message, ToastType type)
        {
            if (_toastContainer == null) return;

            string title = LocalizationService.Instance.GetString(titleKey);

            Dispatcher.UIThread.Post(() =>
            {
                if (_toastContainer == null) return;
                var toast = new ToastNotification();
                _toastContainer.Children.Add(toast);
                toast.Show(title, message, type);
            });
        }
    }
}
