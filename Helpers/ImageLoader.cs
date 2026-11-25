using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace MinecraftLauncher.Helpers
{
    public static class ImageLoader
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
        private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new();

        public static readonly AttachedProperty<string?> SourceProperty =
            AvaloniaProperty.RegisterAttached<Image, string?>("Source", typeof(ImageLoader));

        static ImageLoader()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("QLauncher/2.0");
            SourceProperty.Changed.AddClassHandler<Image>(OnSourceChanged);
        }

        public static string? GetSource(Image obj) => obj.GetValue(SourceProperty);
        public static void SetSource(Image obj, string? value) => obj.SetValue(SourceProperty, value);

        private static async void OnSourceChanged(Image image, AvaloniaPropertyChangedEventArgs e)
        {
            string? url = e.NewValue as string;
            if (string.IsNullOrWhiteSpace(url))
            {
                image.Source = null;
                return;
            }

            if (Cache.TryGetValue(url, out var cached))
            {
                image.Source = cached;
                return;
            }

            Bitmap? bmp = null;
            try
            {
                if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
                    using var ms = new MemoryStream(bytes);
                    bmp = Bitmap.DecodeToWidth(ms, 256);
                }
                else if (File.Exists(url))
                {
                    await using var fs = File.OpenRead(url);
                    bmp = Bitmap.DecodeToWidth(fs, 480);
                }
            }
            catch
            {
                bmp = null;
            }

            if (bmp != null) Cache[url] = bmp;

            Dispatcher.UIThread.Post(() =>
            {
                if (GetSource(image) == url)
                {
                    image.Source = bmp;
                }
            });
        }
    }
}
