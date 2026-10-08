using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using MinecraftLauncher.Common;
using MinecraftLauncher.Models;
using MinecraftLauncher.Services;
using MinecraftLauncher.Views.Windows;

namespace MinecraftLauncher.ViewModels
{
    public class ScreenshotsViewModel : ViewModelBase
    {
        private readonly string _screenshotsFolder;
        private readonly IToastService _toastService;

        // Локализация для строк, которые собираются в коде.
        private readonly ILocalizationService _loc = LocalizationService.Instance;

        private BitmapImage? _previewImage;
        private bool _isPreviewVisible;
        private bool _hasScreenshots;

        public ObservableCollection<ScreenshotItem> Screenshots { get; } = new();

        private string? _previewPath;

        public BitmapImage? PreviewImage
        {
            get => _previewImage;
            set => SetProperty(ref _previewImage, value);
        }

        public string? PreviewPath
        {
            get => _previewPath;
            set => SetProperty(ref _previewPath, value);
        }

        public bool IsPreviewVisible
        {
            get => _isPreviewVisible;
            set => SetProperty(ref _isPreviewVisible, value);
        }

        public bool HasScreenshots
        {
            get => _hasScreenshots;
            set => SetProperty(ref _hasScreenshots, value);
        }

        public RelayCommand OpenFolderCommand { get; }
        public RelayCommand<string> OpenPreviewCommand { get; }
        public RelayCommand ClosePreviewCommand { get; }
        public RelayCommand<string> CopyImageCommand { get; }
        public RelayCommand<string> DeleteImageCommand { get; }

        public ScreenshotsViewModel(string gamePath) : this(gamePath, ToastService.Instance)
        {
        }

        public ScreenshotsViewModel(string gamePath, IToastService toastService)
        {
            _screenshotsFolder = Path.Combine(gamePath, "screenshots");
            _toastService = toastService;

            OpenFolderCommand = new RelayCommand(ExecuteOpenFolder);
            OpenPreviewCommand = new RelayCommand<string>(ExecuteOpenPreview);
            ClosePreviewCommand = new RelayCommand(ExecuteClosePreview);
            CopyImageCommand = new RelayCommand<string>(ExecuteCopyImage);
            DeleteImageCommand = new RelayCommand<string>(ExecuteDeleteImage);

            LoadScreenshots();
        }

        public void LoadScreenshots()
        {
            Screenshots.Clear();

            if (Directory.Exists(_screenshotsFolder))
            {
                var files = Directory.GetFiles(_screenshotsFolder, "*.png", SearchOption.TopDirectoryOnly);
                Array.Sort(files, (a, b) => File.GetCreationTime(b).CompareTo(File.GetCreationTime(a)));

                foreach (var file in files)
                {
                    Screenshots.Add(new ScreenshotItem
                    {
                        FileName = Path.GetFileName(file),
                        FilePath = file
                    });
                }
            }

            HasScreenshots = Screenshots.Count > 0;
        }

        private void ExecuteOpenFolder()
        {
            Directory.CreateDirectory(_screenshotsFolder);
            try
            {
                Process.Start("explorer.exe", _screenshotsFolder);
            }
            catch { }
        }

        private void ExecuteOpenPreview(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();

                PreviewPath = path;
                PreviewImage = bitmap;
                IsPreviewVisible = true;
            }
            catch { }
        }

        private void ExecuteClosePreview()
        {
            IsPreviewVisible = false;
            PreviewImage = null;
            PreviewPath = null;
        }

        private void ExecuteCopyImage(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            try
            {
                var bitmap = new BitmapImage(new Uri(path, UriKind.Absolute));
                Clipboard.SetImage(bitmap);
                _toastService.ShowSuccess(_loc.GetString("Str_Screenshot_Copied"), _loc.GetString("Str_T_Screenshot"));
            }
            catch (Exception ex)
            {
                _toastService.ShowError(_loc.Format("Str_Screenshot_CopyError", ex.Message), _loc.GetString("Str_T_Error"));
            }
        }

        private void ExecuteDeleteImage(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            if (QMessageBoxWindow.Show(_loc.GetString("Str_Screenshot_DeleteQuestion"), _loc.GetString("Str_T_Confirmation"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                try
                {
                    File.Delete(path);
                    LoadScreenshots();
                    _toastService.ShowInfo(_loc.GetString("Str_Screenshot_Deleted"), _loc.GetString("Str_T_Screenshot"));
                }
                catch (Exception ex)
                {
                    _toastService.ShowError(_loc.Format("Str_Screenshot_DeleteError", ex.Message), _loc.GetString("Str_T_Error"));
                }
            }
        }
    }
}
