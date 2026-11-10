using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using MinecraftLauncher.Helpers;

namespace MinecraftLauncher.Models
{
    public class LauncherSettings
    {
        public string GamePath { get; set; } = LauncherPathHelper.GetDefaultDataDirectory();
        public int RamMb { get; set; } = 4096;
        public string JavaPath { get; set; } = "";
        public bool CloseOnLaunch { get; set; } = false;
        public bool EnableDiscordRpc { get; set; } = true;
        public bool EnableUiSounds { get; set; } = true;
        public bool HideServerIp { get; set; } = true;
        public bool AutoAddServers { get; set; } = false;
        public string ActiveAccount { get; set; } = "";
        public string LastSelectedVersion { get; set; } = "";

        /// <summary>
        /// Пресет сборщика мусора: Auto, None, G1GC, Shenandoah, ZGC.
        /// Старые значения ("default", "aikar", "zgc_shenandoah") разбираются
        /// в JvmOptimizationHelper.PresetFromString, поэтому переезд не ломает
        /// уже существующие settings.json.
        /// </summary>
        public string JvmPreset { get; set; } = JvmOptimizationHelper.AutoPresetName;
        public string CustomJvmArgs { get; set; } = "";
        public bool UseOptimizedJvmArgs { get; set; } = true;

        /// <summary>
        /// Порядок использования официальных источников и зеркал:
        /// Auto, OfficialFirst, MirrorFirst, OfficialOnly.
        /// При Auto лаунчер проверяет доступность хостов при старте и ставит
        /// вперёд отвечающий, чтобы не платить таймаут на каждом запуске.
        /// </summary>
        public string MirrorPreference { get; set; } = "Auto";

        public int ScreenWidth { get; set; } = 1920;
        public int ScreenHeight { get; set; } = 1080;
        public bool IsFullScreen { get; set; } = false;
        public string GpuPreference { get; set; } = "HighPerformance";
        public string CustomWallpaperPath { get; set; } = "";
        public bool IsDarkTheme { get; set; } = true;

        /// <summary>
        /// Пресет акцентного цвета. Раньше выбор акцента жил только в памяти
        /// ThemeService, поэтому после перезапуска всегда возвращался синий.
        /// </summary>
        public string AccentPreset { get; set; } = "sapphire";

        public string Language { get; set; } = "ru";

        public bool CheckUpdatesOnStartup { get; set; } = true;
        public bool ShowSplashOnStartup { get; set; } = true;
        public string UpdateChannel { get; set; } = "stable";
        public string UpdateMirror { get; set; } = "auto";
        public int BandwidthLimitMbps { get; set; } = 0;
        public string SkippedVersion { get; set; } = "";

        /// <summary>
        /// Адрес сервера, который добавляется в servers.dat при включённом
        /// автодобавлении. Раньше был зашит прямо в коде, и у всех
        /// пользователей лаунчер стучался на play.scraft.ru независимо от
        /// настроек. Пустая строка отключает автодобавление.
        /// </summary>
        public string AutoInjectServerIp { get; set; } = "play.scraft.ru";

        /// <summary>Отображаемое имя для <see cref="AutoInjectServerIp"/>.</summary>
        public string AutoInjectServerName { get; set; } = "SCRAFT Server";

        /// <summary>
        /// Персональный токен CurseForge (формат "$2a$10$..."). В памяти — обычная
        /// строка, на диск сериализуется зашифрованной через DPAPI
        /// (см. <see cref="ProtectedStringConverter"/>). Пустое значение убирает
        /// CurseForge из мод-браузера: Modrinth продолжает работать без ключа.
        /// </summary>
        [JsonConverter(typeof(ProtectedStringConverter))]
        public string CurseForgeApiKey { get; set; } = "";

        /// <summary>
        /// clientToken для авторизации Ely.by (Yggdrasil). Это НЕ секрет —
        /// идентификатор конкретной установки лаунчера, сервер Ely.by
        /// привязывает access-токены к паре с ним. Генерируется при первом
        /// входе в Ely.by и живёт в settings.json обычной строкой.
        /// </summary>
        public string ElyByClientToken { get; set; } = "";

        public List<AccountProfile> Accounts { get; set; } = new List<AccountProfile>();
        public List<ModpackProfile> Modpacks { get; set; } = new List<ModpackProfile>();

        /// <summary>
        /// Избранные проекты каталога модов. Живут в settings.json без сети:
        /// вкладка «Избранное» браузера показывает их мгновенно.
        /// </summary>
        public List<FavoriteMod> FavoriteMods { get; set; } = new List<FavoriteMod>();
    }
}
