using System.Windows;
using System.Windows.Controls;
using MinecraftLauncher.Views.Controls;

namespace MinecraftLauncher.Services
{
    public interface IToastService
    {
        void RegisterContainer(Panel container);

        /// <summary>title передаётся ключом ресурса, а не готовой строкой.</summary>
        void ShowSuccess(string message, string titleKey = "Str_Toast_Success");

        /// <summary>title передаётся ключом ресурса, а не готовой строкой.</summary>
        void ShowError(string message, string titleKey = "Str_Toast_Error");

        /// <summary>title передаётся ключом ресурса, а не готовой строкой.</summary>
        void ShowWarning(string message, string titleKey = "Str_Toast_Warning");

        /// <summary>title передаётся ключом ресурса, а не готовой строкой.</summary>
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

        /// <summary>
        /// titleKey — ключ ресурса. Раньше здесь стояли русские строки по умолчанию
        /// ("Успех", "Ошибка", ...), то есть при английском интерфейсе заголовок
        /// тоста оставался русским, даже если сообщение было переведено.
        /// </summary>
        private void ShowToast(string titleKey, string message, ToastType type)
        {
            if (_toastContainer == null) return;

            string title = LocalizationService.Instance.GetString(titleKey);

            Application.Current.Dispatcher.Invoke(() =>
            {
                var toast = new ToastNotification();
                _toastContainer.Children.Add(toast);
                toast.Show(title, message, type);
            });
        }
    }
}
