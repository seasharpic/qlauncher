using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Models;
using MinecraftLauncher.Services.LaunchEngine;

namespace MinecraftLauncher.Services
{
    public interface IModpackImportService
    {
        Task<ModpackProfile> ImportAsync(string sourceFile, string gamePath, IProgress<string>? status = null);
    }

    /// <summary>
    /// Импорт сборок: .mrpack (Modrinth), .zip от CurseForge (manifest.json),
    /// .zip от Prism Launcher / MultiMC (mmc-pack.json + instance.cfg) и обычные .zip.
    /// </summary>
    public class ModpackImportService : IModpackImportService
    {
        public static ModpackImportService Instance { get; } = new ModpackImportService();

        private static readonly HttpClient CfHttp = new() { Timeout = TimeSpan.FromSeconds(30) };

        public async Task<ModpackProfile> ImportAsync(string sourceFile, string gamePath, IProgress<string>? status = null)
        {
            if (string.IsNullOrWhiteSpace(sourceFile) || !File.Exists(sourceFile))
            {
                throw new FileNotFoundException(LocalizationService.Instance.GetString("Str_Import_FileNotFound"), sourceFile);
            }

            if (string.IsNullOrWhiteSpace(gamePath))
            {
                throw new ArgumentException(LocalizationService.Instance.GetString("Str_Import_EmptyGamePath"), nameof(gamePath));
            }

            string ext = Path.GetExtension(sourceFile).ToLowerInvariant();

            return ext switch
            {
                ".mrpack" => await ImportMrPackAsync(sourceFile, gamePath, status),
                ".zip" => await ImportZipAsync(sourceFile, gamePath, status),
                _ => throw new NotSupportedException(LocalizationService.Instance.Format("Str_Import_UnsupportedExt", ext))
            };
        }

        private static async Task<ModpackProfile> ImportMrPackAsync(string sourceFile, string gamePath, IProgress<string>? status)
        {
            status?.Report(LocalizationService.Instance.GetString("Str_Import_StatusMrPack"));

            return await Task.Run(() => MrPackInstaller.InstallMrPackAsync(sourceFile, gamePath))
                .ConfigureAwait(true);
        }

        private static async Task<ModpackProfile> ImportZipAsync(string sourceFile, string gamePath, IProgress<string>? status)
        {
            status?.Report(LocalizationService.Instance.GetString("Str_Import_StatusZip"));

            string fallbackName = Path.GetFileNameWithoutExtension(sourceFile);
            if (string.IsNullOrWhiteSpace(fallbackName))
            {
                throw new InvalidDataException(LocalizationService.Instance.GetString("Str_Import_NameMissing"));
            }

            string tempExtract = Path.Combine(Path.GetTempPath(), $"qlauncher_import_{Guid.NewGuid():N}");
            try
            {
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(tempExtract);
                    ZipFile.ExtractToDirectory(sourceFile, tempExtract, overwriteFiles: true);
                });

                string cfManifest = Path.Combine(tempExtract, "manifest.json");
                if (File.Exists(cfManifest))
                {
                    return await ImportCurseForgeExtractedAsync(tempExtract, cfManifest, fallbackName, gamePath, status);
                }

                string? prismRoot = FindPrismRoot(tempExtract);
                if (prismRoot != null)
                {
                    return await ImportPrismExtractedAsync(prismRoot, fallbackName, gamePath);
                }

                // Обычный ZIP-архив
                string instancesPath = AllocateInstanceFolder(gamePath, ref fallbackName);
                await Task.Run(() => CopyDirectory(tempExtract, instancesPath));

                return new ModpackProfile
                {
                    Name = fallbackName,
                    GameVersion = "1.20.1",
                    Loader = "Custom",
                    FolderPath = instancesPath
                };
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, true);
                }
                catch { }
            }
        }

        private static async Task<ModpackProfile> ImportCurseForgeExtractedAsync(
            string root, string manifestPath, string fallbackName, string gamePath, IProgress<string>? status)
        {
            string json = await File.ReadAllTextAsync(manifestPath);
            using var doc = JsonDocument.Parse(json);
            var rootEl = doc.RootElement;

            string packName = rootEl.TryGetProperty("name", out var n) && !string.IsNullOrWhiteSpace(n.GetString())
                ? n.GetString()!.Trim()
                : fallbackName;

            string gameVersion = "1.20.1";
            string loader = "Vanilla";

            if (rootEl.TryGetProperty("minecraft", out var mcEl))
            {
                if (mcEl.TryGetProperty("version", out var v) && !string.IsNullOrWhiteSpace(v.GetString()))
                {
                    gameVersion = v.GetString()!.Trim();
                }

                if (mcEl.TryGetProperty("modLoaders", out var loadersEl) && loadersEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var l in loadersEl.EnumerateArray())
                    {
                        string id = l.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                        bool primary = !l.TryGetProperty("primary", out var pEl) || pEl.GetBoolean();
                        if (!primary && loader != "Vanilla") continue;

                        if (id.StartsWith("fabric-", StringComparison.OrdinalIgnoreCase)) loader = "Fabric";
                        else if (id.StartsWith("neoforge-", StringComparison.OrdinalIgnoreCase)) loader = "NeoForge";
                        else if (id.StartsWith("forge-", StringComparison.OrdinalIgnoreCase)) loader = "Forge";
                        else if (id.StartsWith("quilt-", StringComparison.OrdinalIgnoreCase)) loader = "Quilt";
                    }
                }
            }

            string instancesPath = AllocateInstanceFolder(gamePath, ref packName);
            Directory.CreateDirectory(instancesPath);

            string overridesName = rootEl.TryGetProperty("overrides", out var ov) && !string.IsNullOrWhiteSpace(ov.GetString())
                ? ov.GetString()!
                : "overrides";

            string overridesDir = Path.Combine(root, overridesName);
            if (Directory.Exists(overridesDir))
            {
                await Task.Run(() => CopyDirectory(overridesDir, instancesPath));
            }

            // Если задан CurseForge API ключ — скачиваем моды из manifest.json -> files
            string cfKey = SettingsService.Instance.Settings.CurseForgeApiKey;
            if (!string.IsNullOrWhiteSpace(cfKey) &&
                rootEl.TryGetProperty("files", out var filesEl) &&
                filesEl.ValueKind == JsonValueKind.Array)
            {
                string modsDir = Path.Combine(instancesPath, "mods");
                Directory.CreateDirectory(modsDir);

                var fileList = filesEl.EnumerateArray().ToList();
                int done = 0;
                foreach (var f in fileList)
                {
                    done++;
                    if (!f.TryGetProperty("projectID", out var projEl) || !f.TryGetProperty("fileID", out var fidEl))
                        continue;

                    int projectId = projEl.GetInt32();
                    int fileId = fidEl.GetInt32();
                    status?.Report($"CurseForge: {done}/{fileList.Count}");

                    try
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Get,
                            $"https://api.curseforge.com/v1/mods/{projectId}/files/{fileId}");
                        req.Headers.Add("x-api-key", cfKey);

                        using var resp = await CfHttp.SendAsync(req);
                        if (!resp.IsSuccessStatusCode) continue;

                        using var fileDoc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                        var dataEl = fileDoc.RootElement.GetProperty("data");
                        string? dlUrl = dataEl.TryGetProperty("downloadUrl", out var dlEl) ? dlEl.GetString() : null;
                        string fileName = dataEl.TryGetProperty("fileName", out var fnEl) ? fnEl.GetString() ?? $"{fileId}.jar" : $"{fileId}.jar";
                        if (string.IsNullOrWhiteSpace(dlUrl)) continue;

                        byte[] bytes = await CfHttp.GetByteArrayAsync(dlUrl);
                        await File.WriteAllBytesAsync(Path.Combine(modsDir, Path.GetFileName(fileName)), bytes);
                    }
                    catch (Exception ex)
                    {
                        CrashLogWriter.Write("CurseForgeImport", $"Failed to download mod {projectId}/{fileId}", ex);
                    }
                }
            }

            return new ModpackProfile
            {
                Name = packName,
                GameVersion = gameVersion,
                Loader = loader,
                FolderPath = instancesPath
            };
        }

        private static async Task<ModpackProfile> ImportPrismExtractedAsync(
            string prismRoot, string fallbackName, string gamePath)
        {
            string packName = fallbackName;
            string cfgPath = Path.Combine(prismRoot, "instance.cfg");
            if (File.Exists(cfgPath))
            {
                foreach (var line in await File.ReadAllLinesAsync(cfgPath))
                {
                    if (line.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
                    {
                        string val = line[5..].Trim();
                        if (!string.IsNullOrWhiteSpace(val)) packName = val;
                        break;
                    }
                }
            }

            string gameVersion = "1.20.1";
            string loader = "Vanilla";
            string mmcPack = Path.Combine(prismRoot, "mmc-pack.json");
            if (File.Exists(mmcPack))
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(mmcPack));
                if (doc.RootElement.TryGetProperty("components", out var comps) && comps.ValueKind == JsonValueKind.Array)
                {
                    foreach (var comp in comps.EnumerateArray())
                    {
                        string uid = comp.TryGetProperty("uid", out var u) ? u.GetString() ?? "" : "";
                        string ver = comp.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";

                        if (uid == "net.minecraft" && !string.IsNullOrWhiteSpace(ver)) gameVersion = ver;
                        else if (uid == "net.fabricmc.fabric-loader") loader = "Fabric";
                        else if (uid == "net.minecraftforge") loader = "Forge";
                        else if (uid == "net.neoforged") loader = "NeoForge";
                        else if (uid == "org.quiltmc.quilt-loader") loader = "Quilt";
                    }
                }
            }

            string instancesPath = AllocateInstanceFolder(gamePath, ref packName);
            Directory.CreateDirectory(instancesPath);

            string mcDir = Path.Combine(prismRoot, ".minecraft");
            if (!Directory.Exists(mcDir)) mcDir = Path.Combine(prismRoot, "minecraft");
            if (!Directory.Exists(mcDir)) mcDir = prismRoot;

            await Task.Run(() => CopyDirectory(mcDir, instancesPath));

            return new ModpackProfile
            {
                Name = packName,
                GameVersion = gameVersion,
                Loader = loader,
                FolderPath = instancesPath
            };
        }

        private static string? FindPrismRoot(string root)
        {
            if (File.Exists(Path.Combine(root, "mmc-pack.json"))) return root;
            foreach (var dir in Directory.GetDirectories(root))
            {
                if (File.Exists(Path.Combine(dir, "mmc-pack.json"))) return dir;
            }
            return null;
        }

        private static string AllocateInstanceFolder(string gamePath, ref string packName)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                packName = packName.Replace(c, '_');
            }
            string instancesPath = Path.Combine(gamePath, "instances", packName);
            if (Directory.Exists(instancesPath))
            {
                packName += "_" + DateTime.Now.ToString("HHmmss");
                instancesPath = Path.Combine(gamePath, "instances", packName);
            }
            return instancesPath;
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: true);
            }
            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                CopyDirectory(subDir, Path.Combine(destinationDir, Path.GetFileName(subDir)));
            }
        }
    }
}
