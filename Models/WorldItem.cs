using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace MinecraftLauncher.Models
{
    public class WorldItem
    {
        public string FolderName { get; set; } = "";
        public string WorldName { get; set; } = "";
        public string FullPath { get; set; } = "";
        public DateTime LastModified { get; set; }
        public long SizeBytes { get; set; }
        public BitmapImage? Icon { get; set; }

        public string DisplaySize => SizeBytes >= 1048576 * 1024
            ? Services.LocalizationService.Instance.Format("Str_Size_Gb", $"{(SizeBytes / (1024.0 * 1048576.0)):F1}")
            : Services.LocalizationService.Instance.Format("Str_Size_Mb", $"{(SizeBytes / 1048576.0):F1}");

        public string DisplayDate => LastModified.ToString("dd.MM.yyyy HH:mm");
        public string LastPlayedText { get; set; } = "";
        public string IconPath { get; set; } = "/logo.png";
    }

    public class WorldBackupItem
    {
        public string FileName { get; set; } = "";
        public string FullPath { get; set; } = "";
        public string WorldName { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public long SizeBytes { get; set; }

        public string DisplaySize => Services.LocalizationService.Instance.Format("Str_Size_Mb", $"{(SizeBytes / 1048576.0):F1}");
        public string DisplayDate => CreatedAt.ToString("dd.MM.yyyy HH:mm");
    }
}
