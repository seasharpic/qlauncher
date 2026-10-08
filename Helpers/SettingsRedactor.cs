using System.Collections.Generic;
using System.Linq;
using MinecraftLauncher.Models;

namespace MinecraftLauncher.Helpers
{
    /// <summary>
    /// Готовит копию настроек, безопасную для экспорта и диагностики:
    /// токены удаляются, а ник и uuid сохраняются.
    ///
    /// Экспорт настроек раньше сериализовал объект целиком вместе с
    /// AccountProfile.AccessToken. DiagnosticReportService свою redact-функцию имел,
    /// но экспорт её обходил — теперь redact применяется в обоих путях.
    /// </summary>
    public static class SettingsRedactor
    {
        /// <summary>
        /// Создаёт глубокую копию настроек без секретов.
        /// Оригинал не изменяется — он продолжает работать в приложении.
        /// </summary>
        public static LauncherSettings CreateSanitizedCopy(LauncherSettings source)
        {
            return new LauncherSettings
            {
                GamePath = source.GamePath,
                RamMb = source.RamMb,
                JavaPath = source.JavaPath,
                CloseOnLaunch = source.CloseOnLaunch,
                EnableDiscordRpc = source.EnableDiscordRpc,
                EnableUiSounds = source.EnableUiSounds,
                HideServerIp = source.HideServerIp,
                AutoAddServers = source.AutoAddServers,
                ActiveAccount = source.ActiveAccount,
                LastSelectedVersion = source.LastSelectedVersion,
                JvmPreset = source.JvmPreset,
                CustomJvmArgs = source.CustomJvmArgs,
                UseOptimizedJvmArgs = source.UseOptimizedJvmArgs,
                MirrorPreference = source.MirrorPreference,
                ScreenWidth = source.ScreenWidth,
                ScreenHeight = source.ScreenHeight,
                IsFullScreen = source.IsFullScreen,
                GpuPreference = source.GpuPreference,
                CustomWallpaperPath = source.CustomWallpaperPath,
                IsDarkTheme = source.IsDarkTheme,
                AccentPreset = source.AccentPreset,
                Language = source.Language,
                CheckUpdatesOnStartup = source.CheckUpdatesOnStartup,
                ShowSplashOnStartup = source.ShowSplashOnStartup,
                UpdateChannel = source.UpdateChannel,
                UpdateMirror = source.UpdateMirror,
                BandwidthLimitMbps = source.BandwidthLimitMbps,
                SkippedVersion = source.SkippedVersion,
                AutoInjectServerIp = source.AutoInjectServerIp,
                AutoInjectServerName = source.AutoInjectServerName,

                // Ник остаётся — он нужен для диагностики, но токен убираем.
                // Пустой AccessToken при импорте обратно не даст войти без повторного логина.
                Accounts = source.Accounts
                    .Select(a => new AccountProfile
                    {
                        Nickname = a.Nickname,
                        Uuid = a.Uuid,
                        AccessToken = Services.LaunchEngine.Models.LaunchOptions.OfflineAccessToken
                    })
                    .ToList(),

                Modpacks = new List<ModpackProfile>(source.Modpacks)
            };
        }
    }
}