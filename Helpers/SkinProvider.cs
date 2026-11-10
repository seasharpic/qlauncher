using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace MinecraftLauncher.Helpers
{
    /// <summary>
    /// Полная текстура скина игрока (не лицо-аватарка) для 3D-превью.
    /// Источники: skinsystem.ely.by — для Ely.by-аккаунтов, mc-heads.net
    /// (по UUID, если он есть) — для Microsoft; minotar как запасной.
    /// PNG кэшируется в Cache/Skins и остаётся доступен для «Сохранить скин».
    /// </summary>
    public static class SkinProvider
    {
        private static readonly HttpClient Http = new();
        private static readonly ConcurrentDictionary<string, BitmapImage> Cache = new();
        private static readonly string CacheDir =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache", "Skins");

        static SkinProvider()
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                Http.DefaultRequestHeaders.UserAgent.ParseAdd("QLauncher/2.0");
            }
            catch { }
        }

        public static bool IsPng(byte[]? bytes) =>
            bytes != null && bytes.Length > 8 &&
            bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G';

        public static string CachePath(bool isElyBy, string nickname) =>
            Path.Combine(CacheDir, $"{(isElyBy ? "ely" : "ms")}_{nickname.ToLowerInvariant()}.png");

        /// <summary>
        /// Скачивает скин (или берёт из кэша). Возвращает пару «картинка, путь
        /// локального файла» — путь нужен кнопке сохранения скина в окне.
        /// </summary>
        public static async Task<(BitmapImage? Image, string? CachedPath)> GetSkinAsync(
            bool isElyBy, string nickname, string uuid = "")
        {
            if (string.IsNullOrWhiteSpace(nickname)) return (null, null);

            string key = nickname.ToLowerInvariant();
            string localPath = CachePath(isElyBy, nickname);

            if (Cache.TryGetValue(key, out var cached)) return (cached, localPath);

            if (File.Exists(localPath))
            {
                var bitmap = LoadFromFile(localPath);
                if (bitmap != null)
                {
                    Cache[key] = bitmap;
                    return (bitmap, localPath);
                }
            }

            // Порядок источников: свой каталог — главный, чужой — запасной.
            string? nick = Uri.EscapeDataString(nickname);
            string? uid = string.IsNullOrWhiteSpace(uuid) || uuid == "offline" ? null : uuid;
            string[] urls = isElyBy
                ? new[] { $"https://skinsystem.ely.by/skins/{nick}.png",
                          $"https://mc-heads.net/skin/{nick}" }
                : new[] { uid != null ? $"https://mc-heads.net/skin/{uuid}" : $"https://mc-heads.net/skin/{nick}",
                          $"https://minotar.net/skin/{nick}" };

            foreach (string url in urls)
            {
                try
                {
                    byte[] bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(true);
                    if (!IsPng(bytes)) continue;

                    await File.WriteAllBytesAsync(localPath, bytes).ConfigureAwait(true);
                    var bitmap = LoadFromFile(localPath);
                    if (bitmap != null)
                    {
                        Cache[key] = bitmap;
                        return (bitmap, localPath);
                    }
                }
                catch { }
            }

            return (null, localPath);
        }

        private static BitmapImage? LoadFromFile(string path)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }
    }
}
