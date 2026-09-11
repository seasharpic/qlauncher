using System;
using System.Collections.Generic;
using System.IO;
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
        public string JvmPreset { get; set; } = "default";
        public string CustomJvmArgs { get; set; } = "";

        public int ScreenWidth { get; set; } = 1920;
        public int ScreenHeight { get; set; } = 1080;
        public bool IsFullScreen { get; set; } = false;
        public string GpuPreference { get; set; } = "HighPerformance";
        public string CustomWallpaperPath { get; set; } = "";
        public bool IsDarkTheme { get; set; } = true;
        public string Language { get; set; } = "ru";

        public bool CheckUpdatesOnStartup { get; set; } = true;
        public bool ShowSplashOnStartup { get; set; } = true;
        public string UpdateChannel { get; set; } = "stable";
        public string UpdateMirror { get; set; } = "auto";
        public int BandwidthLimitMbps { get; set; } = 0;
        public string SkippedVersion { get; set; } = "";

        public List<AccountProfile> Accounts { get; set; } = new List<AccountProfile>();
        public List<ModpackProfile> Modpacks { get; set; } = new List<ModpackProfile>();
    }
}
