using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace MinecraftLauncher.Helpers
{
    public interface ILauncherNavigator
    {
        void AnimateNavigate(UserControl page);
        void AnimateGoBack();
        void CloseSettings();
        void OpenModpackOverlay();
        void OpenShortcutOverlay();
        void ReloadLauncher();
    }

    public static class UiHelper
    {
        public static ILauncherNavigator? Navigator { get; set; }

        public static Window? MainWindow =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        public static TopLevel? CurrentTopLevel
        {
            get
            {
                if (MainWindow is { } win) return TopLevel.GetTopLevel(win);
                if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime single && single.MainView != null)
                {
                    return TopLevel.GetTopLevel(single.MainView);
                }
                return null;
            }
        }

        public static void RunOnUi(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess())
                action();
            else
                Dispatcher.UIThread.Post(action);
        }

        public static Task InvokeOnUiAsync(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }
            return Dispatcher.UIThread.InvokeAsync(action).GetTask();
        }

        public static void Shutdown()
        {
            RunOnUi(() =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    desktop.Shutdown();
                }
            });
        }

        public static async Task<string?> PickFileAsync(string title, params FilePickerFileType[] filters)
        {
            var topLevel = CurrentTopLevel;
            if (topLevel?.StorageProvider == null) return null;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = filters.Length > 0 ? filters : null
            });

            return files.FirstOrDefault()?.TryGetLocalPath();
        }

        public static async Task<string?> SaveFileAsync(string title, string suggestedFileName, string? defaultExtension = null, params FilePickerFileType[] filters)
        {
            var topLevel = CurrentTopLevel;
            if (topLevel?.StorageProvider == null) return null;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedFileName,
                DefaultExtension = defaultExtension,
                FileTypeChoices = filters.Length > 0 ? filters : null
            });

            return file?.TryGetLocalPath();
        }

        public static async Task<string?> PickFolderAsync(string title)
        {
            var topLevel = CurrentTopLevel;
            if (topLevel?.StorageProvider == null) return null;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            });

            return folders.FirstOrDefault()?.TryGetLocalPath();
        }

        public static async Task SetClipboardTextAsync(string text)
        {
            var topLevel = CurrentTopLevel;
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(text);
            }
        }
    }
}
