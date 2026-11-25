using System;

namespace MinecraftLauncher.Helpers
{
    public enum ResolvedTitlebarKind
    {
        Windows11,
        MacOS,
        Gnome,
        Kde,
        Tiling
    }

    public static class TitlebarHelper
    {
        public static event Action? TitlebarStyleChanged;

        public static void NotifyStyleChanged() => TitlebarStyleChanged?.Invoke();

        public static ResolvedTitlebarKind Resolve(string? configuredStyle)
        {
            return (configuredStyle ?? "Auto").ToLowerInvariant() switch
            {
                "windows11" => ResolvedTitlebarKind.Windows11,
                "macos" => ResolvedTitlebarKind.MacOS,
                "gnome" => ResolvedTitlebarKind.Gnome,
                "kde" => ResolvedTitlebarKind.Kde,
                "tiling" => ResolvedTitlebarKind.Tiling,
                _ => DetectSystemTitlebar()
            };
        }

        public static ResolvedTitlebarKind DetectSystemTitlebar()
        {
            if (OperatingSystem.IsMacOS()) return ResolvedTitlebarKind.MacOS;
            if (OperatingSystem.IsWindows()) return ResolvedTitlebarKind.Windows11;

            if (IsTilingWm()) return ResolvedTitlebarKind.Tiling;

            string desktop = (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "") + " " +
                             (Environment.GetEnvironmentVariable("XDG_SESSION_DESKTOP") ?? "");

            if (desktop.Contains("gnome", StringComparison.OrdinalIgnoreCase) ||
                desktop.Contains("ubuntu", StringComparison.OrdinalIgnoreCase) ||
                desktop.Contains("cinnamon", StringComparison.OrdinalIgnoreCase) ||
                desktop.Contains("pantheon", StringComparison.OrdinalIgnoreCase))
            {
                return ResolvedTitlebarKind.Gnome;
            }

            if (desktop.Contains("kde", StringComparison.OrdinalIgnoreCase) ||
                desktop.Contains("plasma", StringComparison.OrdinalIgnoreCase))
            {
                return ResolvedTitlebarKind.Kde;
            }

            return ResolvedTitlebarKind.Kde;
        }

        public static bool IsTilingWm()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE")) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NIRI_SOCKET")) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SWAYSOCK")) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("I3SOCK")))
            {
                return true;
            }

            string desktop = (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "") + " " +
                             (Environment.GetEnvironmentVariable("XDG_SESSION_DESKTOP") ?? "") + " " +
                             (Environment.GetEnvironmentVariable("DESKTOP_SESSION") ?? "");

            return desktop.Contains("hyprland", StringComparison.OrdinalIgnoreCase) ||
                   desktop.Contains("niri", StringComparison.OrdinalIgnoreCase) ||
                   desktop.Contains("sway", StringComparison.OrdinalIgnoreCase) ||
                   desktop.Contains("i3", StringComparison.OrdinalIgnoreCase) ||
                   desktop.Contains("bspwm", StringComparison.OrdinalIgnoreCase) ||
                   desktop.Contains("awesome", StringComparison.OrdinalIgnoreCase) ||
                   desktop.Contains("dwm", StringComparison.OrdinalIgnoreCase);
        }
    }
}
