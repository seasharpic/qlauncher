using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using MinecraftLauncher.Models;

namespace MinecraftLauncher.Services
{
    public interface IGpuService
    {
        List<GpuInfo> GetAvailableGpus();
        void ApplyGpuPreference(string javaPath, string preference);
        void ConfigureProcessEnvironment(ProcessStartInfo startInfo, string preference);
    }

    public class GpuService : IGpuService
    {
        public static GpuService Instance { get; } = new();

        private const string VideoClassGuid = "{4d36e968-e325-11ce-bfc1-08002be10318}";
        private const string UserGpuPreferencesKey = @"Software\Microsoft\DirectX\UserGpuPreferences";

        public List<GpuInfo> GetAvailableGpus()
        {
            if (OperatingSystem.IsWindows())
            {
                return GetWindowsGpus();
            }

            if (OperatingSystem.IsLinux())
            {
                return GetLinuxGpus();
            }

            return new List<GpuInfo>();
        }

        private static List<GpuInfo> GetLinuxGpus()
        {
            var result = new List<GpuInfo>();
            try
            {
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = "lspci",
                    Arguments = "-nn",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (proc != null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(1500);
                    foreach (var line in output.Split('\n'))
                    {
                        if (line.Contains("VGA compatible controller", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("3D controller", StringComparison.OrdinalIgnoreCase))
                        {
                            int idx = line.IndexOf(": ", StringComparison.Ordinal);
                            string name = idx >= 0 ? line[(idx + 2)..].Trim() : line.Trim();
                            bool isDiscrete = DetermineIfDiscrete(name, "", name, 0);
                            result.Add(new GpuInfo { Name = name, IsDiscrete = isDiscrete });
                        }
                    }
                }
            }
            catch { }
            return result;
        }

        [SupportedOSPlatform("windows")]
        private static List<GpuInfo> GetWindowsGpus()
        {
            var result = new List<GpuInfo>();

            try
            {
                using var classKey = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Control\Class\{VideoClassGuid}");
                if (classKey != null)
                {
                    for (int i = 0; i < 32; i++)
                    {
                        string subName = i.ToString("D4");
                        using var devKey = classKey.OpenSubKey(subName);
                        if (devKey == null) continue;

                        string? name = devKey.GetValue("DriverDesc") as string;
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        if (name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("VMware", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("RDP", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("Remote Desktop", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string provider = devKey.GetValue("ProviderName") as string ?? "";
                        string driverVersion = devKey.GetValue("DriverVersion") as string ?? "";
                        string matchingId = devKey.GetValue("MatchingDeviceId") as string ?? "";

                        long vram = 0;
                        var qwMem = devKey.GetValue("HardwareInformation.qwMemorySize");
                        if (qwMem is long l) vram = l;
                        else if (qwMem is int iVal) vram = (uint)iVal;
                        else
                        {
                            var mem = devKey.GetValue("HardwareInformation.MemorySize");
                            if (mem is long ml) vram = ml;
                            else if (mem is int mi) vram = (uint)mi;
                            else if (mem is byte[] mb && mb.Length >= 4)
                            {
                                vram = BitConverter.ToUInt32(mb, 0);
                            }
                        }

                        bool isDiscrete = DetermineIfDiscrete(name, provider, matchingId, vram);

                        if (!result.Any(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                        {
                            result.Add(new GpuInfo
                            {
                                Name = name,
                                Vendor = provider,
                                DriverVersion = driverVersion,
                                DeviceId = matchingId,
                                VramBytes = vram,
                                IsDiscrete = isDiscrete
                            });
                        }
                    }
                }
            }
            catch { }

            if (result.Count == 0)
            {
                try
                {
                    result.AddRange(EnumerateViaDisplayDevices());
                }
                catch { }
            }

            return result;
        }

        private static bool DetermineIfDiscrete(string name, string provider, string deviceId, long vram)
        {
            string lowerName = name.ToLowerInvariant();
            string lowerId = deviceId.ToLowerInvariant();

            if (lowerName.Contains("nvidia") || lowerName.Contains("geforce") || lowerName.Contains("quadro") ||
                lowerName.Contains("rtx") || lowerName.Contains("gtx") || lowerId.Contains("ven_10de") || lowerId.Contains("10de:"))
            {
                return true;
            }

            if (lowerName.Contains("radeon") || lowerId.Contains("ven_1002") || lowerId.Contains("ven_1022") || lowerId.Contains("1002:"))
            {
                if (lowerName.Contains("rx ") || lowerName.Contains("pro ") || vram >= 1024L * 1024L * 1024L)
                {
                    return true;
                }
            }

            if (lowerName.Contains("intel") && lowerName.Contains("arc"))
            {
                return true;
            }

            return false;
        }

        public void ApplyGpuPreference(string javaPath, string preference)
        {
            if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(javaPath)) return;
            ApplyWindowsGpuPreference(javaPath, preference);
        }

        [SupportedOSPlatform("windows")]
        private static void ApplyWindowsGpuPreference(string javaPath, string preference)
        {
            try
            {
                var pathsToRegister = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(javaPath))
                {
                    pathsToRegister.Add(Path.GetFullPath(javaPath));

                    string dir = Path.GetDirectoryName(javaPath) ?? "";
                    string file = Path.GetFileName(javaPath);
                    if (file.Equals("java.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        string javaw = Path.Combine(dir, "javaw.exe");
                        if (File.Exists(javaw)) pathsToRegister.Add(Path.GetFullPath(javaw));
                    }
                    else if (file.Equals("javaw.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        string java = Path.Combine(dir, "java.exe");
                        if (File.Exists(java)) pathsToRegister.Add(Path.GetFullPath(java));
                    }
                }

                using var key = Registry.CurrentUser.CreateSubKey(UserGpuPreferencesKey);
                if (key == null) return;

                string? regValue;
                if (string.Equals(preference, "PowerSaving", StringComparison.OrdinalIgnoreCase))
                {
                    regValue = "GpuPreference=1;";
                }
                else if (string.Equals(preference, "Default", StringComparison.OrdinalIgnoreCase))
                {
                    regValue = null;
                }
                else
                {
                    regValue = "GpuPreference=2;";
                }

                foreach (var p in pathsToRegister)
                {
                    if (regValue == null)
                    {
                        if (key.GetValue(p) != null)
                        {
                            key.DeleteValue(p, throwOnMissingValue: false);
                        }
                    }
                    else
                    {
                        key.SetValue(p, regValue);
                    }
                }
            }
            catch { }
        }

        public void ConfigureProcessEnvironment(ProcessStartInfo startInfo, string preference)
        {
            if (string.Equals(preference, "PowerSaving", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.EnvironmentVariables.Remove("SHIM_MCCOMPAT");
                startInfo.EnvironmentVariables["DRI_PRIME"] = "0";
            }
            else if (string.Equals(preference, "Default", StringComparison.OrdinalIgnoreCase))
            {
            }
            else
            {
                startInfo.EnvironmentVariables["SHIM_MCCOMPAT"] = "0x800000001";
                startInfo.EnvironmentVariables["__NV_PRIME_RENDER_OFFLOAD"] = "1";
                startInfo.EnvironmentVariables["__GLX_VENDOR_LIBRARY_NAME"] = "nvidia";
                startInfo.EnvironmentVariables["DRI_PRIME"] = "1";
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

        private static List<GpuInfo> EnumerateViaDisplayDevices()
        {
            var list = new List<GpuInfo>();
            uint devNum = 0;
            var d = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };

            while (EnumDisplayDevices(null, devNum, ref d, 0))
            {
                if (!string.IsNullOrWhiteSpace(d.DeviceString) &&
                    !list.Any(g => g.Name.Equals(d.DeviceString, StringComparison.OrdinalIgnoreCase)))
                {
                    bool isDiscrete = DetermineIfDiscrete(d.DeviceString, "", d.DeviceID, 0);
                    list.Add(new GpuInfo
                    {
                        Name = d.DeviceString,
                        DeviceId = d.DeviceID,
                        IsDiscrete = isDiscrete
                    });
                }
                devNum++;
                d.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
            }

            return list;
        }
    }
}
