using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using MinecraftLauncher.Models;

namespace MinecraftLauncher.Services
{
    public interface IWorldBackupService
    {
        List<WorldItem> GetWorlds(string gamePath);
        List<WorldBackupItem> GetBackups(string gamePath);
        Task<string> CreateBackupAsync(string worldPath, string gamePath);
        Task RestoreBackupAsync(string backupZipPath, string gamePath);
        bool DeleteBackup(string backupZipPath);
    }

    public class WorldBackupService : IWorldBackupService
    {
        public static WorldBackupService Instance { get; } = new WorldBackupService();

        public List<WorldItem> GetWorlds(string gamePath)
        {
            var list = new List<WorldItem>();
            string savesDir = Path.Combine(gamePath, "saves");
            if (!Directory.Exists(savesDir)) return list;

            try
            {
                foreach (var dir in Directory.GetDirectories(savesDir))
                {
                    try
                    {
                        string folderName = Path.GetFileName(dir);
                        string iconPath = Path.Combine(dir, "icon.png");
                        Bitmap? icon = null;

                        if (File.Exists(iconPath))
                        {
                            try
                            {
                                icon = new Bitmap(iconPath);
                            }
                            catch { }
                        }

                        var di = new DirectoryInfo(dir);
                        long size = 0;
                        try
                        {
                            foreach (var f in di.EnumerateFiles("*", SearchOption.AllDirectories))
                            {
                                size += f.Length;
                            }
                        }
                        catch { }

                        list.Add(new WorldItem
                        {
                            FolderName = folderName,
                            WorldName = folderName,
                            FullPath = dir,
                            LastModified = di.LastWriteTime,
                            SizeBytes = size,
                            Icon = icon
                        });
                    }
                    catch { }
                }
            }
            catch { }

            return list;
        }

        public List<WorldBackupItem> GetBackups(string gamePath)
        {
            var list = new List<WorldBackupItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var candidateDirs = new[]
            {
                Path.Combine(gamePath, "backups", "saves"),
                Path.Combine(gamePath, "backups")
            };

            foreach (var backupDir in candidateDirs)
            {
                if (!Directory.Exists(backupDir)) continue;

                try
                {
                    foreach (var file in Directory.GetFiles(backupDir, "*.zip"))
                    {
                        if (seen.Contains(file)) continue;
                        seen.Add(file);

                        try
                        {
                            var fi = new FileInfo(file);
                            string baseName = Path.GetFileNameWithoutExtension(file);
                            string worldName = TryStripTimestamp(baseName) ?? baseName;

                            list.Add(new WorldBackupItem
                            {
                                FileName = fi.Name,
                                FullPath = file,
                                WorldName = worldName,
                                CreatedAt = fi.LastWriteTime,
                                SizeBytes = fi.Length
                            });
                        }
                        catch { }
                    }
                }
                catch { }
            }

            return list;
        }

        private static string? TryStripTimestamp(string fileNameWithoutExtension)
        {
            const int timestampWithSeparatorLength = 1 + 8 + 1 + 6;

            if (fileNameWithoutExtension.Length <= timestampWithSeparatorLength)
            {
                return null;
            }

            string tail = fileNameWithoutExtension[^timestampWithSeparatorLength..];
            if (tail[0] != '_' || tail[9] != '_')
            {
                return null;
            }

            for (int i = 1; i < timestampWithSeparatorLength; i++)
            {
                if (i == 9) continue;
                if (!char.IsDigit(tail[i])) return null;
            }

            return fileNameWithoutExtension[..^timestampWithSeparatorLength];
        }

        public async Task<string> CreateBackupAsync(string worldPath, string gamePath)
        {
            return await Task.Run(() =>
            {
                string worldName = Path.GetFileName(worldPath);
                string backupDir = Path.Combine(gamePath, "backups", "saves");
                Directory.CreateDirectory(backupDir);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string destZip = Path.Combine(backupDir, $"{worldName}_{timestamp}.zip");

                if (File.Exists(destZip)) File.Delete(destZip);

                ZipFile.CreateFromDirectory(worldPath, destZip, CompressionLevel.Optimal, false);
                return destZip;
            });
        }

        public async Task RestoreBackupAsync(string backupZipPath, string gamePath)
        {
            await Task.Run(() =>
            {
                string fileName = Path.GetFileNameWithoutExtension(backupZipPath);
                string worldName = TryStripTimestamp(fileName) ?? fileName;

                string savesDir = Path.Combine(gamePath, "saves");
                Directory.CreateDirectory(savesDir);

                string targetWorldDir = Path.Combine(savesDir, worldName);
                if (Directory.Exists(targetWorldDir))
                {
                    string backupsRoot = Path.Combine(gamePath, "backups");
                    Directory.CreateDirectory(backupsRoot);

                    string conflictBackup = Path.Combine(
                        backupsRoot,
                        $"{worldName}_pre_restore_{DateTime.Now:yyyyMMdd_HHmmss}");

                    Directory.Move(targetWorldDir, conflictBackup);
                }

                Directory.CreateDirectory(targetWorldDir);
                ZipFile.ExtractToDirectory(backupZipPath, targetWorldDir, true);
            });
        }

        public bool DeleteBackup(string backupZipPath)
        {
            try
            {
                if (File.Exists(backupZipPath))
                {
                    File.Delete(backupZipPath);
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}
