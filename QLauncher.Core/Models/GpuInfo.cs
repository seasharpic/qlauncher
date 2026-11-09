using System;

namespace MinecraftLauncher.Models
{
    public class GpuInfo
    {
        public string Name { get; set; } = "";
        public string Vendor { get; set; } = "";
        public long VramBytes { get; set; }
        public string DriverVersion { get; set; } = "";
        public string DeviceId { get; set; } = "";
        public bool IsDiscrete { get; set; }

        public string VramFormatted
        {
            get
            {
                if (VramBytes <= 0) return "";
                double gb = VramBytes / (1024.0 * 1024.0 * 1024.0);
                if (gb >= 1.0)
                {
                    return Services.LocalizationService.Instance.Format("Str_Size_Gb", $"{gb:F0}");
                }
                double mb = VramBytes / (1024.0 * 1024.0);
                return Services.LocalizationService.Instance.Format("Str_Size_Mb", $"{mb:F0}");
            }
        }

        public string DisplayName =>
            string.IsNullOrEmpty(VramFormatted) ? Name : $"{Name} ({VramFormatted})";
    }

    public class GpuOptionItem
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";

        public override string ToString() => Title;
    }
}
