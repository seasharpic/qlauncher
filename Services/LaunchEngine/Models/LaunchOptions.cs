using System;
using System.Collections.Generic;
using MinecraftLauncher.Helpers;

namespace MinecraftLauncher.Services.LaunchEngine.Models
{
    public class LaunchOptions
    {
        public string GameRootPath { get; set; } = "";
        public string InstancePath { get; set; } = "";
        public string VersionId { get; set; } = "";
        public string JavaPath { get; set; } = "";
        public string PlayerName { get; set; } = "Player";
        public string Uuid { get; set; } = "";
        public string AccessToken { get; set; } = OfflineAccessToken;

        /// <summary>
        /// Метка офлайн-аккаунта. Офлайн-аккаунты создаются с таким значением
        /// вместо настоящего токена, потому что токена у них нет.
        /// </summary>
        public const string OfflineAccessToken = "offline";

        /// <summary>
        /// Нужна ли игре авторизация Microsoft.
        ///
        /// Правило живёт здесь, а не в сборщике аргументов: раньше проверка
        /// выглядела как "!string.IsNullOrEmpty(AccessToken)", из-за чего
        /// офлайн-аккаунт с меткой "offline" считался онлайновым. В игру
        /// уходили user_type=mojang и clientid при заведомо нерабочем токене,
        /// и сессионные серверы отклоняли авторизацию.
        /// </summary>
        public bool IsOnlineAuth =>
            !string.IsNullOrWhiteSpace(AccessToken)
            && !string.Equals(AccessToken, OfflineAccessToken, StringComparison.OrdinalIgnoreCase);
        public int RamMb { get; set; } = 4096;

        /// <summary>
        /// Пресет сборщика мусора. Помимо значений из JvmOptimizationHelper
        /// принимает "Auto" — выбор по объёму памяти. По умолчанию Auto.
        /// </summary>
        public string JvmPreset { get; set; } = JvmOptimizationHelper.AutoPresetName;
        public string CustomJvmArgs { get; set; } = "";

        /// <summary>
        /// Базовые оптимизации JVM (StringDeduplication, ParallelRefProc,
        /// AlwaysPreTouch, PerfDisableSharedMem) поверх флагов сборщика мусора.
        /// При false остаются только флаги сборщика.
        /// </summary>
        public bool UseOptimizedJvmArgs { get; set; } = true;
        public int ScreenWidth { get; set; } = 1920;
        public int ScreenHeight { get; set; } = 1080;
        public bool IsFullScreen { get; set; }
        public string GpuPreference { get; set; } = "HighPerformance";
        public bool IsDemo { get; set; }
        public string? QuickPlaySingleplayer { get; set; }
        public string? QuickPlayMultiplayer { get; set; }
        public string? QuickPlayRealms { get; set; }
        public string? ServerIp { get; set; }
        public int? ServerPort { get; set; }
        public Dictionary<string, string> CustomVariables { get; set; } = new();
    }

    public enum LaunchPhase
    {
        Initializing,
        CheckingVersionMetadata,
        DownloadingLibraries,
        DownloadingAssets,
        BuildingArguments,
        StartingProcess,
        Completed,
        Failed
    }

    public class LaunchProgress
    {
        public LaunchPhase Phase { get; set; }
        public string StatusText { get; set; } = "";
        public int Percentage { get; set; }
        public long CurrentBytes { get; set; }
        public long TotalBytes { get; set; }
    }
}
