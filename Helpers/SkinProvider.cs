using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace MinecraftLauncher.Helpers
{
    public static class SkinProvider
    {
        private static readonly HttpClient Http = new();
        private static readonly ConcurrentDictionary<string, Bitmap> Cache = new();
        private static readonly string CacheDir =
            Path.Combine(LauncherPathHelper.GetDefaultDataDirectory(), "Cache", "Skins");

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

        public static async Task<(Bitmap? Image, string? CachedPath)> GetSkinAsync(
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

            string nick = Uri.EscapeDataString(nickname);
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

        private static Bitmap? LoadFromFile(string path)
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
    }
}
