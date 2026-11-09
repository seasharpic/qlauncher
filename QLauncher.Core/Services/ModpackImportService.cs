using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using MinecraftLauncher.Models;
using MinecraftLauncher.Services.LaunchEngine;

namespace MinecraftLauncher.Services
{
    public interface IModpackImportService
    {
        Task<ModpackProfile> ImportAsync(string sourceFile, string gamePath, IProgress<string>? status = null);
    }

    /// <summary>
    /// Импорт сборок: .mrpack (Modrinth) и обычные .zip.
    ///
    /// Раньше одна и та же логика была написана трижды: в MainViewModel,
    /// в ModpacksViewModel и в обработчике drag-and-drop. Копии уже разошлись
    /// (в одной перезагружался список версий, в другой нет; в третьей был сломан
    /// await над Dispatcher.InvokeAsync, который возвращает внутренний Task и его
    /// отбрасывал, поэтому фактическое ожидание не происходило).
    ///
    /// Здесь реализация одна. Тяжёлые операции (распаковка, скачивание модов)
    /// выполняются в Task.Run, чтобы не блокировать UI-поток.
    /// </summary>
    public class ModpackImportService : IModpackImportService
    {
        public static ModpackImportService Instance { get; } = new ModpackImportService();

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

            string packName = Path.GetFileNameWithoutExtension(sourceFile);
            if (string.IsNullOrWhiteSpace(packName))
            {
                throw new InvalidDataException(LocalizationService.Instance.GetString("Str_Import_NameMissing"));
            }

            string instancesPath = Path.Combine(gamePath, "instances", packName);
            if (Directory.Exists(instancesPath))
            {
                packName += "_" + DateTime.Now.ToString("HHmmss");
                instancesPath = Path.Combine(gamePath, "instances", packName);
            }

            // Статический ExtractToDirectory проверяет пути на выход за пределы
            // каталога (в отличие от ручного entry.ExtractToFile).
            await Task.Run(() =>
            {
                Directory.CreateDirectory(instancesPath);
                ZipFile.ExtractToDirectory(sourceFile, instancesPath, overwriteFiles: true);
            });

            return new ModpackProfile
            {
                Name = packName,
                GameVersion = "1.20.1",
                Loader = "Custom",
                FolderPath = instancesPath
            };
        }
    }
}