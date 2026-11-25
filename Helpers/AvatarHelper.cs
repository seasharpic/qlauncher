using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace MinecraftLauncher.Helpers
{
    public static class AvatarHelper
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private static readonly ConcurrentDictionary<string, Bitmap> Cache = new ConcurrentDictionary<string, Bitmap>();
        private static readonly string CacheDir = Path.Combine(LauncherPathHelper.GetDefaultDataDirectory(), "Cache", "Avatars");

        static AvatarHelper()
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("QLauncher/2.0");
            }
            catch { }
        }

        public static async Task<Bitmap?> GetAvatarAsync(string nickname, string uuid = "")
        {
            if (string.IsNullOrWhiteSpace(nickname)) return GetDefaultAvatar();

            string key = nickname.ToLowerInvariant();
            if (Cache.TryGetValue(key, out var cached)) return cached;

            string localPath = Path.Combine(CacheDir, $"{key}.png");
            if (File.Exists(localPath))
            {
                try
                {
                    var bitmap = LoadBitmapFromFile(localPath);
                    if (bitmap != null)
                    {
                        Cache[key] = bitmap;
                        return bitmap;
                    }
                }
                catch { }
            }

            try
            {
                string target = string.IsNullOrWhiteSpace(uuid) || uuid == "offline" ? nickname : uuid;
                string url = $"https://minotar.net/helm/{Uri.EscapeDataString(target)}/32.png";

                byte[] bytes = await HttpClient.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(localPath, bytes);

                var bitmap = LoadBitmapFromFile(localPath);
                if (bitmap != null)
                {
                    Cache[key] = bitmap;
                    return bitmap;
                }
            }
            catch { }

            try
            {
                string elyUrl = $"https://ely.by/services/skins-system/skins/{Uri.EscapeDataString(nickname)}.png";
                byte[] bytes = await HttpClient.GetByteArrayAsync(elyUrl);
                await File.WriteAllBytesAsync(localPath, bytes);

                var bitmap = LoadBitmapFromFile(localPath);
                if (bitmap != null)
                {
                    Cache[key] = bitmap;
                    return bitmap;
                }
            }
            catch { }

            return GetDefaultAvatar();
        }

        public static async Task<Bitmap?> GetBodyPreviewAsync(string nickname, string uuid = "")
        {
            if (string.IsNullOrWhiteSpace(nickname)) return GetDefaultAvatar();

            string key = $"body_{nickname.ToLowerInvariant()}";
            if (Cache.TryGetValue(key, out var cached)) return cached;

            string localPath = Path.Combine(CacheDir, $"{key}.png");
            if (File.Exists(localPath))
            {
                try
                {
                    var bitmap = LoadBitmapFromFile(localPath);
                    if (bitmap != null)
                    {
                        Cache[key] = bitmap;
                        return bitmap;
                    }
                }
                catch { }
            }

            try
            {
                string target = string.IsNullOrWhiteSpace(uuid) || uuid == "offline" ? nickname : uuid;
                string url = $"https://minotar.net/armor/body/{Uri.EscapeDataString(target)}/120.png";

                byte[] bytes = await HttpClient.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(localPath, bytes);

                var bitmap = LoadBitmapFromFile(localPath);
                if (bitmap != null)
                {
                    Cache[key] = bitmap;
                    return bitmap;
                }
            }
            catch { }

            return GetDefaultAvatar();
        }

        private static Bitmap? LoadBitmapFromFile(string path)
        {
            try
            {
                return new Bitmap(path);
            }
            catch
            {
                return null;
            }
        }

        public static Bitmap? GetDefaultAvatar()
        {
            string key = "__default_steve__";
            if (Cache.TryGetValue(key, out var cached)) return cached;

            try
            {
                using var stream = AssetLoader.Open(new Uri("avares://QLauncher/logo.png"));
                var bitmap = new Bitmap(stream);
                Cache[key] = bitmap;
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }
}
