using System;
using System.Threading.Tasks;
using MinecraftLauncher.Models;

namespace MinecraftLauncher.Views.Windows
{
    public enum QMessageBoxButton { OK, OKCancel, YesNo, YesNoCancel }
    public enum QMessageBoxImage { None, Information, Warning, Error, Question }
    public enum QMessageBoxResult { None, OK, Cancel, Yes, No }

    public static class QMessageBoxWindow
    {
        public static Func<string, string?, QMessageBoxButton, QMessageBoxImage, Task<QMessageBoxResult>>? Handler { get; set; }

        public static Task<QMessageBoxResult> ShowAsync(
            string message,
            string? title = null,
            QMessageBoxButton buttons = QMessageBoxButton.OK,
            QMessageBoxImage image = QMessageBoxImage.Information)
        {
            if (Handler != null)
            {
                return Handler(message, title, buttons, image);
            }
            return Task.FromResult(buttons == QMessageBoxButton.YesNo ? QMessageBoxResult.Yes : QMessageBoxResult.OK);
        }
    }

    public sealed class UpdateWindow
    {
        public UpdateWindow(ReleaseInfo release) { }
        public void Show() { }
        public Task ShowDialog(object owner) => Task.CompletedTask;
    }
}
