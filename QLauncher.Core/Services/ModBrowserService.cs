using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Models;
using System.Net.Http;

namespace MinecraftLauncher.Services
{
    /// <summary>
    /// Бросается, когда пользователь просит поиск в CurseForge без API-ключа.
    /// ViewModel показывает по нему отдельный тост с подсказкой, а не общий
    /// «ошибка сети»: у пользователя простое решение — вставить ключ.
    /// </summary>
    public class CurseForgeApiKeyException : Exception
    {
        public CurseForgeApiKeyException()
            : base("CurseForge API key is not set")
        {
        }
    }

    public interface IModBrowserService
    {
        Task<IReadOnlyList<OnlineModEntry>> SearchAsync(
            ModSource source, ModCategoryKind kind, ModSearchRequest request,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ModCategory>> GetCategoriesAsync(
            ModSource source, ModCategoryKind kind,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Подбирает файл мода по версии Minecraft и загрузчику.
        /// Возвращает null, если совместимого файла нет.
        /// </summary>
        Task<OnlineModFile?> ResolveFileAsync(
            ModSource source, OnlineModEntry mod,
            string? gameVersion, string? loader,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Подбирает файл зависимости по её id (CurseForge modId или Modrinth project_id).
        /// </summary>
        Task<OnlineModFile?> ResolveDependencyFileAsync(
            ModSource source, string dependencyId,
            string? gameVersion, string? loader,
            CancellationToken cancellationToken = default);

        /// <summary>Все версии Minecraft, известные каталогам (для фильтра в UI).</summary>
        Task<IReadOnlyList<string>> GetGameVersionsAsync(
            CancellationToken cancellationToken = default);

        /// <summary>Проверка ключа без сетевых вызовов.</summary>
        bool HasCurseForgeKey();

        /// <summary>
        /// Подробности проекта для страницы мода: описание, ссылка на каталог
        /// и последние файлы. null — каталог недоступен или проект не найден.
        /// </summary>
        Task<OnlineModDetail?> GetProjectDetailsAsync(
            ModSource source, ModCategoryKind kind, OnlineModEntry mod,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Локализация категорий обоих каталогов.
    /// Modrinth присылает name в слтрафиле слага ("game-mechanics"), CurseForge —
    /// обычные английские названия ("Game Mechanics"); оба вида сведены к общему
    /// ключу: нижний регистр, дефисы в пробелы. Незнакомые категории (8x, 16x,
    /// high...) остаются как пришли — их переводить не нужно.
    /// </summary>
    internal static class ModCategoryLocalizer
    {
        private static readonly Dictionary<string, string> Keys = new(StringComparer.Ordinal)
        {
            // --- mods ---
            ["adventure"] = "Str_Mods_Cat_Adventure",
            ["cursed"] = "Str_Mods_Cat_Cursed",
            ["decoration"] = "Str_Mods_Cat_Decoration",
            ["economy"] = "Str_Mods_Cat_Economy",
            ["equipment"] = "Str_Mods_Cat_Equipment",
            ["food"] = "Str_Mods_Cat_Food",
            ["game mechanics"] = "Str_Mods_Cat_GameMechanics",
            ["library"] = "Str_Mods_Cat_Library",
            ["magic"] = "Str_Mods_Cat_Magic",
            ["management"] = "Str_Mods_Cat_Management",
            ["minigame"] = "Str_Mods_Cat_MiniGame",
            ["mobs"] = "Str_Mods_Cat_Mobs",
            ["optimization"] = "Str_Mods_Cat_Optimization",
            ["social"] = "Str_Mods_Cat_Social",
            ["storage"] = "Str_Mods_Cat_Storage",
            ["technology"] = "Str_Mods_Cat_Technology",
            ["transportation"] = "Str_Mods_Cat_Transportation",
            ["utility"] = "Str_Mods_Cat_Utility",
            ["worldgen"] = "Str_Mods_Cat_Worldgen",

            // --- resourcepacks ---
            ["audio"] = "Str_Mods_Cat_Audio",
            ["blocks"] = "Str_Mods_Cat_Blocks",
            ["combat"] = "Str_Mods_Cat_Combat",
            ["entities"] = "Str_Mods_Cat_Entities",
            ["environment"] = "Str_Mods_Cat_Environment",
            ["fonts"] = "Str_Mods_Cat_Fonts",
            ["gui"] = "Str_Mods_Cat_Gui",
            ["items"] = "Str_Mods_Cat_Items",
            ["locale"] = "Str_Mods_Cat_Locale",
            ["modded"] = "Str_Mods_Cat_Modded",
            ["models"] = "Str_Mods_Cat_Models",
            ["core shaders"] = "Str_Mods_Cat_CoreShaders",
            ["simplistic"] = "Str_Mods_Cat_Simplified",
            ["realistic"] = "Str_Mods_Cat_Realistic",
            ["themed"] = "Str_Mods_Cat_Themed",
            ["tweaks"] = "Str_Mods_Cat_Tweaks",
            ["vanilla like"] = "Str_Mods_Cat_VanillaLike",

            // --- shaders ---
            ["atmosphere"] = "Str_Mods_Cat_Atmosphere",
            ["bloom"] = "Str_Mods_Cat_Bloom",
            ["cartoon"] = "Str_Mods_Cat_Cartoony",
            ["colored lighting"] = "Str_Mods_Cat_ColoredLighting",
            ["fantasy"] = "Str_Mods_Cat_Fantasy",
            ["foliage"] = "Str_Mods_Cat_Foliage",
            ["path tracing"] = "Str_Mods_Cat_PathTracing",
            ["pbr"] = "Str_Mods_Cat_Pbr",
            ["reflections"] = "Str_Mods_Cat_Reflections",
            ["semi realistic"] = "Str_Mods_Cat_SemiRealistic",
            ["shadows"] = "Str_Mods_Cat_Shadows",

            // --- плагины и прочее CurseForge ---
            ["chat"] = "Str_Mods_Cat_Chat",
            ["protection"] = "Str_Mods_Cat_Protection",
            ["games"] = "Str_Mods_Cat_Games",
            ["education"] = "Str_Mods_Cat_Education",
            ["miscellaneous"] = "Str_Mods_Cat_Miscellaneous",
            ["vanilla tweaks"] = "Str_Mods_Cat_VanillaTweaks"
        };

        internal static ILocalizationService? Localization { get; set; }

        public static string Localize(string category)
        {
            string key = category.ToLowerInvariant().Replace('-', ' ').Trim();
            if (Keys.TryGetValue(key, out string? stringKey))
            {
                return Localization?.GetString(stringKey, category) ?? category;
            }

            return category;
        }
    }

    /// <summary>
    /// Браузер онлайн-каталогов (Modrinth + CurseForge).
    ///
    /// Ключевые решения:
    ///  - сетевые вызовы идут через SecureHttp.Shared: один клиент с включённой
    ///    TLS-валидацией, без new HttpClient() на каждый чих;
    ///  - хеши файлов, если каталог их отдал, проверяются HashHelper — моды
    ///    это исполняемый код, и тихая подмена недопустима;
    ///  - разбор ответов через JsonDocument без System.Text.Json-моделей:
    ///    каталогам свойственно менять схему ответа, а нам нужны лишь
    ///    десяток полей — привязка к каждой мелочи только хрупче.
    /// </summary>
    public sealed class ModBrowserService : IModBrowserService
    {
        public static ModBrowserService Instance { get; } = new ModBrowserService(() =>
            SettingsService.Instance.Settings.CurseForgeApiKey);

        private readonly Func<string> _curseForgeKeyProvider;

        public ModBrowserService(Func<string> curseForgeKeyProvider)
        {
            _curseForgeKeyProvider = curseForgeKeyProvider ?? throw new ArgumentNullException(nameof(curseForgeKeyProvider));
            // Локализация категорий живёт в общем ModCategoryLocalizer:
            // им же переводятся и названия CurseForge.
            ModCategoryLocalizer.Localization = LocalizationService.Instance;
        }

        public bool HasCurseForgeKey()
        {
            return !string.IsNullOrWhiteSpace(_curseForgeKeyProvider());
        }

        public async Task<IReadOnlyList<OnlineModEntry>> SearchAsync(
            ModSource source, ModCategoryKind kind, ModSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            request.PageSize = Math.Clamp(request.PageSize, 1, 50);

            try
            {
                return source switch
                {
                    ModSource.CurseForge => await CurseForge.SearchAsync(kind, request, ResolveKey(), cancellationToken).ConfigureAwait(false),
                    _ => await Modrinth.SearchAsync(kind, request, cancellationToken).ConfigureAwait(false)
                };
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException)
            {
                // Структура ответа каталога изменилась — отдаём «пусто» вместо падения,
                // подробности в crash-лог.
                CrashLogWriter.Write("ModBrowserService", "Search parse failed", ex);
                return Array.Empty<OnlineModEntry>();
            }
        }

        public async Task<IReadOnlyList<ModCategory>> GetCategoriesAsync(
            ModSource source, ModCategoryKind kind,
            CancellationToken cancellationToken = default)
        {
            try
            {
                return source switch
                {
                    ModSource.CurseForge => await CurseForge.GetCategoriesAsync(kind, ResolveKey(), cancellationToken).ConfigureAwait(false),
                    _ => await Modrinth.GetCategoriesAsync(kind, cancellationToken).ConfigureAwait(false)
                };
            }
            catch (Exception ex) when (ex is JsonException or HttpRequestException)
            {
                CrashLogWriter.Write("ModBrowserService", "Categories load failed", ex);
                return Array.Empty<ModCategory>();
            }
        }

        public Task<OnlineModFile?> ResolveFileAsync(
            ModSource source, OnlineModEntry mod,
            string? gameVersion, string? loader,
            CancellationToken cancellationToken = default)
        {
            if (mod == null) throw new ArgumentNullException(nameof(mod));
            return ResolveDependencyFileAsync(source, mod.Id, gameVersion, loader, mod.Slug, cancellationToken);
        }

        public Task<OnlineModFile?> ResolveDependencyFileAsync(
            ModSource source, string dependencyId,
            string? gameVersion, string? loader,
            CancellationToken cancellationToken = default)
        {
            return ResolveDependencyFileAsync(source, dependencyId, gameVersion, loader, idOrSlug: dependencyId, cancellationToken: cancellationToken);
        }

        private async Task<OnlineModFile?> ResolveDependencyFileAsync(
            ModSource source, string projectIdOrSlug,
            string? gameVersion, string? loader, string idOrSlug,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(projectIdOrSlug)) return null;

            try
            {
                return source switch
                {
                    ModSource.CurseForge => await CurseForge.ResolveFileAsync(projectIdOrSlug, gameVersion, loader, ResolveKey(), cancellationToken).ConfigureAwait(false),
                    _ => await Modrinth.ResolveFileAsync(projectIdOrSlug, gameVersion, loader, cancellationToken).ConfigureAwait(false)
                };
            }
            catch (Exception ex) when (ex is JsonException or HttpRequestException or KeyNotFoundException or FormatException)
            {
                CrashLogWriter.Write("ModBrowserService", $"File resolve failed for '{idOrSlug}'", ex);
                return null;
            }
        }

        public async Task<IReadOnlyList<string>> GetGameVersionsAsync(CancellationToken cancellationToken = default)
        {
            // Modrinth отдаёт публичный список версий без ключа; CurseForge держит
            // тот же формат токенов версий, поэтому списка достаточно одного.
            try
            {
                using var response = await SecureHttp.Shared.GetAsync(
                    "https://api.modrinth.com/v2/tag/game_versions", cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

                var versions = new List<string>();
                foreach (var v in doc.RootElement.EnumerateArray())
                {
                    if (v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s)
                    {
                        versions.Add(s);
                    }
                }

                // Обратный порядок: свежие версии первыми в комбобоксе.
                versions.Reverse();
                return versions;
            }
            catch (Exception ex) when (ex is JsonException or HttpRequestException)
            {
                CrashLogWriter.Write("ModBrowserService", "Game versions load failed", ex);
                return Array.Empty<string>();
            }
        }

        private string ResolveKey()
        {
            string key = _curseForgeKeyProvider() ?? "";
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new CurseForgeApiKeyException();
            }
            return key.Trim();
        }

        public Task<OnlineModDetail?> GetProjectDetailsAsync(
            ModSource source, ModCategoryKind kind, OnlineModEntry mod,
            CancellationToken cancellationToken = default)
        {
            if (mod == null) throw new ArgumentNullException(nameof(mod));
            return source switch
            {
                ModSource.CurseForge => CurseForge.GetProjectDetailsAsync(kind, mod, ResolveKey(), cancellationToken),
                _ => Modrinth.GetProjectDetailsAsync(kind, mod, cancellationToken)
            };
        }

        // ------------------------------------------------------------------
        //  CurseForge
        // ------------------------------------------------------------------

        private static class CurseForge
        {
            private const string Base = "https://api.curseforge.com/v1";
            private const int MinecraftGameId = 432;

            private static readonly Dictionary<ModCategoryKind, int> RootClassIds = new()
            {
                { ModCategoryKind.Mod, 6 },
                { ModCategoryKind.Plugin, 5 },
                { ModCategoryKind.ResourcePack, 12 },
                { ModCategoryKind.World, 17 },
                { ModCategoryKind.Modpack, 4471 },
                { ModCategoryKind.Customization, 4546 },
                { ModCategoryKind.Shader, 6552 },
                { ModCategoryKind.DataPack, 6945 }
            };
            // Подтверждённые root-классы — Addons к ним не приписан, потому что его
            // classId нигде публично не задокументирован. Ищем один раз динамически.
            private static int? _addonsClassId;

            private static async Task<int?> GetAddonsClassIdAsync(string apiKey, CancellationToken ct)
            {
                // 0 в кэше означает «не нашли» — отдаём null, а не фиктивный classId.
                if (_addonsClassId.HasValue)
                {
                    return _addonsClassId == 0 ? null : _addonsClassId;
                }

                using var document = await GetJsonAsync(
                    $"{Base}/categories?gameId={MinecraftGameId}", apiKey, ct).ConfigureAwait(false);
                if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                foreach (var item in data.EnumerateArray())
                {
                    string name = ReadString(item, "name");
                    bool isAddons = name.Equals("Addons", StringComparison.OrdinalIgnoreCase) ||
                                    ReadString(item, "slug").Contains("addon", StringComparison.OrdinalIgnoreCase);
                    if (!isAddons) continue;
                    if (item.TryGetProperty("classId", out var cid) && cid.ValueKind == JsonValueKind.Number)
                    {
                        _addonsClassId = cid.GetInt32();
                        return _addonsClassId;
                    }
                }

                // Запомнили даже «не нашли» (0), чтобы не долбить сеть на каждый поиск.
                _addonsClassId ??= 0;
                return _addonsClassId == 0 ? (int?)null : _addonsClassId;
            }

            private static async Task<int?> GetRootClassIdAsync(ModCategoryKind kind, string apiKey, CancellationToken ct)
            {
                if (RootClassIds.TryGetValue(kind, out int id)) return id;
                if (kind == ModCategoryKind.Addons) return await GetAddonsClassIdAsync(apiKey, ct).ConfigureAwait(false);
                return null;
            }

            // CurseForge перечисляет загрузчики строкой внутри gameVersions файла.
            private static readonly Dictionary<string, string> LoaderNames = new(StringComparer.OrdinalIgnoreCase)
            {
                { "fabric", "Fabric" },
                { "forge", "Forge" },
                { "neoforge", "NeoForge" },
                { "quilt", "Quilt" }
            };

            // Подтверждено: sortField 2 = по популярности (скачиваниям), 6 = по дате обновления.
            private static int SortField(ModSortMode mode) => mode switch
            {
                ModSortMode.Downloads => 2,
                ModSortMode.RecentlyUpdated => 6,
                _ => 0
            };

            private static string LoaderParam(string? loader) => loader != null && LoaderNames.TryGetValue(loader, out var cfName)
                ? cfName
                : "";

            public static async Task<IReadOnlyList<OnlineModEntry>> SearchAsync(
                ModCategoryKind kind, ModSearchRequest request, string apiKey, CancellationToken ct)
            {
                int? classId = await GetRootClassIdAsync(kind, apiKey, ct).ConfigureAwait(false);
                if (classId == null)
                {
                    return Array.Empty<OnlineModEntry>();
                }

                var url = new StringBuilder($"{Base}/mods/search?gameId={MinecraftGameId}&classId={classId}");
                if (!string.IsNullOrWhiteSpace(request.Query))
                {
                    url.Append("&searchFilter=").Append(Uri.EscapeDataString(request.Query));
                }
                if (!string.IsNullOrWhiteSpace(request.GameVersion))
                {
                    url.Append("&gameVersion=").Append(Uri.EscapeDataString(request.GameVersion));
                }
                string loaderName = LoaderParam(request.Loader);
                if (loaderName.Length > 0)
                {
                    url.Append("&modLoaderType=").Append(ToLoaderType(loaderName));
                }
                if (!string.IsNullOrWhiteSpace(request.CategoryId))
                {
                    url.Append("&categoryId=").Append(Uri.EscapeDataString(request.CategoryId));
                }
                int sortField = SortField(request.Sort);
                if (sortField != 0)
                {
                    url.Append("&sortField=").Append(sortField).Append("&sortOrder=desc");
                }
                url.Append("&pageSize=").Append(request.PageSize);
                if (request.Page > 0)
                {
                    url.Append("&index=").Append(request.Page * request.PageSize);
                }

                using var document = await GetJsonAsync(url.ToString(), apiKey, ct).ConfigureAwait(false);
                return ParseSearch(document.RootElement, ModSource.CurseForge);
            }

            public static async Task<IReadOnlyList<ModCategory>> GetCategoriesAsync(
                ModCategoryKind kind, string apiKey, CancellationToken ct)
            {
                int? classId = await GetRootClassIdAsync(kind, apiKey, ct).ConfigureAwait(false);
                if (classId == null)
                {
                    return Array.Empty<ModCategory>();
                }

                using var document = await GetJsonAsync(
                    $"{Base}/categories?gameId={MinecraftGameId}&classId={classId}", apiKey, ct).ConfigureAwait(false);

                var result = new List<ModCategory>();
                if (document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in data.EnumerateArray())
                    {
                        string id = ReadString(item, "id");
                        string name = ReadString(item, "name");
                        if (id.Length > 0 && name.Length > 0)
                        {
                            // Названия CF ("Game Mechanics", "Cursed") — те же английские
                            // слова, что и слаги Modrinth: локализуем общим словарём.
                            result.Add(new ModCategory { Id = id, Name = ModCategoryLocalizer.Localize(name) });
                        }
                    }
                }

                result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                return result;
            }

            public static async Task<OnlineModFile?> ResolveFileAsync(
                string modId, string? gameVersion, string? loader, string apiKey, CancellationToken ct)
            {
                var url = new StringBuilder($"{Base}/mods/{Uri.EscapeDataString(modId)}/files?pageSize=50");
                if (!string.IsNullOrWhiteSpace(gameVersion))
                {
                    url.Append("&gameVersion=").Append(Uri.EscapeDataString(gameVersion));
                }

                string loaderName = LoaderParam(loader);

                using var document = await GetJsonAsync(url.ToString(), apiKey, ct).ConfigureAwait(false);
                if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                // API фильтрует по версии, но загрузчик внутри gameVersions нужно отсекать самим.
                // Файлы приходят новыми первыми — берём первый подходящий, второй запасной.
                OnlineModFile? primary = null;
                foreach (var file in data.EnumerateArray())
                {
                    if (!StringArrayMatches(file, "gameVersions", loaderName, when: loaderName.Length > 0)) continue;

                    var parsed = ParseFile(file, ModSource.CurseForge, loaderName, gameVersion);
                    if (parsed == null) continue;
                    if (primary == null)
                    {
                        primary = parsed;
                    }
                    else if (primary.Alternative == null)
                    {
                        primary.Alternative = parsed;
                        break;
                    }
                }

                return primary;
            }

            private static OnlineModFile? ParseFile(JsonElement file, ModSource source, string loaderName, string? gameVersion)
            {
                string fileName = ReadString(file, "fileName");
                if (fileName.Length == 0) return null;

                string id = ReadString(file, "id");
                string downloadUrl = ReadString(file, "downloadUrl");
                if (downloadUrl.Length == 0)
                {
                    downloadUrl = BuildForgeCdnUrl(id, fileName);
                }

                var result = new OnlineModFile
                {
                    Url = downloadUrl,
                    FallbackUrl = BuildForgeCdnUrl(id, fileName),
                    FileName = fileName,
                    Sha1 = ReadHash(file, 1),
                    Size = file.TryGetProperty("fileLength", out var len) && len.TryGetInt64(out var size) ? size : 0
                };

                string display = ReadString(file, "displayName");
                result.DisplayName = display.Length > 0 ? display : fileName;

                if (file.TryGetProperty("gameVersions", out var gv) && gv.ValueKind == JsonValueKind.Array)
                {
                    foreach (var v in gv.EnumerateArray())
                    {
                        if (v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s)
                        {
                            result.GameVersions.Add(s);
                        }
                    }
                }

                if (file.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Array)
                {
                    foreach (var dep in deps.EnumerateArray())
                    {
                        int relationType = dep.TryGetProperty("relationType", out var rt) && rt.ValueKind == JsonValueKind.Number ? rt.GetInt32() : 0;
                        if (relationType != 3) continue; // 3 = required
                        string depId = ReadString(dep, "modId");
                        if (depId.Length > 0 && depId != id)
                        {
                            result.RequiredDependencies.Add(new OnlineModDependency
                            {
                                Source = source,
                                Id = depId,
                                Title = depId,
                                Required = true
                            });
                        }
                    }
                }

                return result;
            }

            /// <summary>
            /// CurseForge иногда не отдаёт downloadUrl — восстанавливаем ссылку по схеме CDN:
            /// id делится на первые 4 цифры и остаток без ведущих нулей.
            /// </summary>
            private static string BuildForgeCdnUrl(string fileId, string fileName)
            {
                if (!long.TryParse(fileId, out long numericId) || numericId <= 0)
                {
                    return "";
                }

                string digits = numericId.ToString();
                if (digits.Length <= 4)
                {
                    return "";
                }

                string part1 = digits.Substring(0, 4);
                long part2 = long.Parse(digits.Substring(4));
                return $"https://mediafilez.forgecdn.net/files/{part1}/{part2}/{Uri.EscapeDataString(fileName)}";
            }

            /// <summary>Хеши приходят массивом {id, value}: 1 = SHA-1.</summary>
            private static string ReadHash(JsonElement file, int algorithmId)
            {
                if (file.TryGetProperty("hashes", out var hashes) && hashes.ValueKind == JsonValueKind.Array)
                {
                    foreach (var hash in hashes.EnumerateArray())
                    {
                        if (hash.TryGetProperty("id", out var algId) && algId.ValueKind == JsonValueKind.Number && algId.GetInt32() == algorithmId)
                        {
                            return ReadString(hash, "value");
                        }
                    }
                }
                return "";
            }

            private static string ToLoaderType(string loaderName)
            {
                // из Enum ModLoaderType API: Forge=1, Cauldron=2, LiteLoader=3, Fabric=4, Quilt=5, NeoForge=6
                return loaderName switch
                {
                    "Forge" => "1",
                    "Fabric" => "4",
                    "Quilt" => "5",
                    "NeoForge" => "6",
                    _ => ""
                };
            }

            private static async Task<JsonDocument> GetJsonAsync(string url, string apiKey, CancellationToken ct)
            {
                using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
                request.Headers.Add("x-api-key", apiKey);
                request.Headers.Accept.ParseAdd("application/json");
                using var response = await SecureHttp.Shared.SendAsync(request, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            }

            /// <summary>
            /// Страница мода: /v1/mods/&lt;id&gt; даёт метаданные и ссылку сайта,
            /// /files — последние файлы, /description — HTML-описание.
            /// Отдельные блоки не роняют всё: без описания страница тоже живёт.
            /// </summary>
            public static async Task<OnlineModDetail?> GetProjectDetailsAsync(
                ModCategoryKind kind, OnlineModEntry mod, string apiKey, CancellationToken ct)
            {
                using var meta = await GetJsonAsync(
                    $"{Base}/mods/{Uri.EscapeDataString(mod.Id)}", apiKey, ct).ConfigureAwait(false);
                if (!meta.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var detail = new OnlineModDetail
                {
                    Source = ModSource.CurseForge,
                    Id = mod.Id,
                    Slug = ReadString(data, "slug"),
                    Title = ReadString(data, "name"),
                    Author = mod.Author,
                    IconUrl = data.TryGetProperty("logo", out var logo) ? ReadString(logo, "thumbnailUrl") : "",
                    Downloads = data.TryGetProperty("downloadCount", out var dl) && dl.TryGetInt64(out var n) ? n : 0,
                    UpdatedAt = ReadDate(data, "dateModified")
                };

                if (data.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Object)
                {
                    detail.PageUrl = ReadString(links, "websiteUrl");
                }

                string summary = ReadString(data, "summary");

                try
                {
                    using var response = await SecureHttp.Shared.GetAsync(
                        $"{Base}/mods/{Uri.EscapeDataString(mod.Id)}/description", ct).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        string html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        detail.Description = HtmlToPlainText(html);
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    CrashLogWriter.Write("ModBrowserService", "CF description load failed", ex);
                }

                if (detail.Description.Length == 0)
                {
                    detail.Description = summary;
                }

                try
                {
                    using var filesDoc = await GetJsonAsync(
                        $"{Base}/mods/{Uri.EscapeDataString(mod.Id)}/files?pageSize=10", apiKey, ct).ConfigureAwait(false);
                    if (filesDoc.RootElement.TryGetProperty("data", out var files) && files.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var f in files.EnumerateArray())
                        {
                            var parsed = ParseFile(f, ModSource.CurseForge, "", null);
                            if (parsed != null) detail.Files.Add(parsed);
                        }
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException)
                {
                    CrashLogWriter.Write("ModBrowserService", "CF files load failed", ex);
                }

                if (detail.Files.Count == 0 && detail.Title.Length == 0) return null;
                return detail;
            }

            public static OnlineModEntry[] ParseSearch(JsonElement root, ModSource source)
            {
                if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                {
                    return Array.Empty<OnlineModEntry>();
                }

                var result = new List<OnlineModEntry>();
                foreach (var item in data.EnumerateArray())
                {
                    string author = "";
                    if (item.TryGetProperty("authors", out var authors) && authors.ValueKind == JsonValueKind.Array && authors.GetArrayLength() > 0)
                    {
                        author = ReadString(authors[0], "name");
                    }

                    result.Add(new OnlineModEntry
                    {
                        Source = source,
                        Id = ReadString(item, "id"),
                        Slug = ReadString(item, "slug"),
                        Title = ReadString(item, "name"),
                        Description = ReadString(item, "summary"),
                        Author = author,
                        IconUrl = item.TryGetProperty("logo", out var logo) ? ReadString(logo, "thumbnailUrl") : "",
                        Downloads = item.TryGetProperty("downloadCount", out var dl) && dl.TryGetInt64(out var n) ? n : 0,
                        UpdatedAt = ReadDate(item, "dateModified")
                    });
                }
                return result.ToArray();
            }
        }

        // ------------------------------------------------------------------
        //  Modrinth
        // ------------------------------------------------------------------

        private static class Modrinth
        {
            private const string Base = "https://api.modrinth.com/v2";

            private static string ProjectType(ModCategoryKind kind) => kind switch
            {
                ModCategoryKind.ResourcePack => "resourcepack",
                ModCategoryKind.Shader => "shader",
                ModCategoryKind.DataPack => "datapack",
                ModCategoryKind.Plugin => "plugin",
                ModCategoryKind.Modpack => "modpack",
                _ => "mod"
            };

            /// <summary>Типы, которые Modrinth реально ищет по project_type.</summary>
            public static bool IsSupported(ModCategoryKind kind) =>
                ModCategoryKinds.IsModrinthSupported(kind);

            /// <summary>Заглушки EntityType для ссылок на сайт: /mod /plugin и т.д.</summary>
            private static string SiteEntityType(ModCategoryKind kind) => kind switch
            {
                ModCategoryKind.ResourcePack => "resourcepack",
                ModCategoryKind.Shader => "shader",
                ModCategoryKind.DataPack => "datapack",
                ModCategoryKind.Plugin => "plugin",
                ModCategoryKind.Modpack => "modpack",
                _ => "mod"
            };

            private static string SortIndex(ModSortMode mode) => mode switch
            {
                ModSortMode.Downloads => "downloads",
                ModSortMode.RecentlyUpdated => "updated",
                _ => "relevance"
            };

            public static async Task<IReadOnlyList<OnlineModEntry>> SearchAsync(
                ModCategoryKind kind, ModSearchRequest request, CancellationToken ct)
            {
                var facets = new List<List<string>> { new() { $"project_type:{ProjectType(kind)}" } };
                if (!string.IsNullOrWhiteSpace(request.GameVersion))
                {
                    facets.Add(new() { $"versions:{request.GameVersion}" });
                }
                if (!string.IsNullOrWhiteSpace(request.Loader))
                {
                    facets.Add(new() { $"loaders:{request.Loader.ToLowerInvariant()}" });
                }
                if (!string.IsNullOrWhiteSpace(request.CategoryId))
                {
                    facets.Add(new() { $"categories:{request.CategoryId}" });
                }

                string facetsJson = JsonSerializer.Serialize(facets);
                var url = new StringBuilder($"{Base}/search?query={Uri.EscapeDataString(request.Query ?? "")}");
                url.Append("&facets=").Append(Uri.EscapeDataString(facetsJson));
                url.Append("&index=").Append(SortIndex(request.Sort));
                url.Append("&limit=").Append(request.PageSize);
                if (request.Page > 0)
                {
                    url.Append("&offset=").Append(request.Page * request.PageSize);
                }

                using var document = await GetJsonAsync(url.ToString(), ct).ConfigureAwait(false);
                var result = new List<OnlineModEntry>();
                if (document.RootElement.TryGetProperty("hits", out var hits) && hits.ValueKind == JsonValueKind.Array)
                {
                    foreach (var hit in hits.EnumerateArray())
                    {
                        result.Add(new OnlineModEntry
                        {
                            Source = ModSource.Modrinth,
                            Id = ReadString(hit, "project_id"),
                            Slug = ReadString(hit, "slug"),
                            Title = ReadString(hit, "title"),
                            Description = ReadString(hit, "description"),
                            Author = ReadString(hit, "author"),
                            IconUrl = ReadString(hit, "icon_url"),
                            Downloads = hit.TryGetProperty("downloads", out var dl) && dl.TryGetInt64(out var n) ? n : 0,
                            UpdatedAt = ReadDate(hit, "date_modified")
                        });
                    }
                }
                return result;
            }

            public static async Task<IReadOnlyList<ModCategory>> GetCategoriesAsync(
                ModCategoryKind kind, CancellationToken ct)
            {
                using var document = await GetJsonAsync($"{Base}/tag/category", ct).ConfigureAwait(false);

                string projectType = ProjectType(kind);
                var result = new List<ModCategory>();
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (!string.Equals(ReadString(item, "project_type"), projectType, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string name = ReadString(item, "name");
                    if (name.Length > 0)
                    {
                        // name у Modrinth — слаг ("game-mechanics"); общий локализатор
                        // сводит его к тому же ключу, что и названия CurseForge.
                        result.Add(new ModCategory { Id = name, Name = ModCategoryLocalizer.Localize(name) });
                    }
                }
                return result;
            }

            /// <summary>
            /// Страница проекта: /project/&lt;id&gt; даёт тело описания (HTML),
            /// /version?limit=10 — последние версии.
            /// </summary>
            public static async Task<OnlineModDetail?> GetProjectDetailsAsync(
                ModCategoryKind kind, OnlineModEntry mod, CancellationToken ct)
            {
                using var document = await GetJsonAsync(
                    $"{Base}/project/{Uri.EscapeDataString(mod.Id.Length > 0 ? mod.Id : mod.Slug)}", ct).ConfigureAwait(false);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var root = document.RootElement;
                var detail = new OnlineModDetail
                {
                    Source = ModSource.Modrinth,
                    Id = ReadString(root, "id"),
                    Slug = ReadString(root, "slug"),
                    Title = ReadString(root, "title"),
                    Author = mod.Author,
                    IconUrl = ReadString(root, "icon_url"),
                    Downloads = root.TryGetProperty("downloads", out var dl) && dl.TryGetInt64(out var n) ? n : 0,
                    UpdatedAt = ReadDate(root, "date_modified"),
                    PageUrl = $"https://modrinth.com/{SiteEntityType(kind)}/{ReadString(root, "slug")}",
                    Description = HtmlToPlainText(ReadString(root, "body"))
                };

                if (detail.Description.Length == 0)
                {
                    detail.Description = ReadString(root, "description");
                }

                try
                {
                    using var filesDoc = await GetJsonAsync(
                        $"{Base}/project/{Uri.EscapeDataString(detail.Id.Length > 0 ? detail.Id : detail.Slug)}/version?limit=10", ct).ConfigureAwait(false);
                    if (filesDoc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var version in filesDoc.RootElement.EnumerateArray())
                        {
                            var parsed = ParseVersionFile(version);
                            if (parsed != null) detail.Files.Add(parsed);
                        }
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException)
                {
                    CrashLogWriter.Write("ModBrowserService", "MR versions load failed", ex);
                }

                if (detail.Files.Count == 0 && detail.Title.Length == 0) return null;
                return detail;
            }

            public static async Task<OnlineModFile?> ResolveFileAsync(
                string projectId, string? gameVersion, string? loader, CancellationToken ct)
            {
                var url = new StringBuilder($"{Base}/project/{Uri.EscapeDataString(projectId)}/version");

                bool hasVersion = !string.IsNullOrWhiteSpace(gameVersion);
                bool hasLoader = !string.IsNullOrWhiteSpace(loader);

                if (hasVersion || hasLoader)
                {
                    url.Append("?");
                    var query = new List<string>();
                    if (hasVersion)
                    {
                        query.Add($"game_versions=[\"{Uri.EscapeDataString(gameVersion!)}\"]");
                    }
                    if (hasLoader)
                    {
                        query.Add($"loaders=[\"{Uri.EscapeDataString(loader!.ToLowerInvariant())}\"]");
                    }
                    url.Append(string.Join("&", query));
                }

                using var document = await GetJsonAsync(url.ToString(), ct).ConfigureAwait(false);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                // API отдаёт версии новыми первыми, фильтры применены на сервере.
                // Берём первую подходящую версию, остальные — кандидаты на всякий случай.
                foreach (var version in document.RootElement.EnumerateArray())
                {
                    var file = ParseVersionFile(version);
                    if (file != null) return file;
                }
                return null;
            }

            private static OnlineModFile? ParseVersionFile(JsonElement version)
            {
                if (!version.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array || files.GetArrayLength() == 0)
                {
                    return null;
                }

                // primary:true — основной артефакт версии.
                JsonElement chosen = files[0];
                foreach (var f in files.EnumerateArray())
                {
                    if (f.TryGetProperty("primary", out var p) && p.ValueKind == JsonValueKind.True)
                    {
                        chosen = f;
                        break;
                    }
                }

                string sha1 = "";
                if (chosen.TryGetProperty("hashes", out var hashes) &&
                    hashes.TryGetProperty("sha1", out var sha1El) &&
                    sha1El.ValueKind == JsonValueKind.String)
                {
                    sha1 = sha1El.GetString() ?? "";
                }

                var result = new OnlineModFile
                {
                    Url = ReadString(chosen, "url"),
                    FileName = ReadString(chosen, "filename"),
                    Sha1 = sha1,
                    Size = chosen.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var size) ? size : 0
                };

                string versionNumber = ReadString(version, "version_number");
                result.DisplayName = versionNumber.Length > 0 ? versionNumber : result.FileName;

                if (version.TryGetProperty("game_versions", out var gv) && gv.ValueKind == JsonValueKind.Array)
                {
                    foreach (var v in gv.EnumerateArray())
                    {
                        if (v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s)
                        {
                            result.GameVersions.Add(s);
                        }
                    }
                }

                if (version.TryGetProperty("dependencies", out var deps) && deps.ValueKind == JsonValueKind.Array)
                {
                    foreach (var dep in deps.EnumerateArray())
                    {
                        bool required = string.Equals(ReadString(dep, "dependency_type"), "required", StringComparison.OrdinalIgnoreCase);
                        string depId = ReadString(dep, "project_id");
                        if (required && depId.Length > 0)
                        {
                            result.RequiredDependencies.Add(new OnlineModDependency
                            {
                                Source = ModSource.Modrinth,
                                Id = depId,
                                Title = depId,
                                Required = true
                            });
                        }
                    }
                }

                return result.Url.Length > 0 ? result : null;
            }

            private static async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
            {
                using var response = await SecureHttp.Shared.GetAsync(url, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                return await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            }
        }

        // ------------------------------------------------------------------
        //  Общий разбор JSON
        // ------------------------------------------------------------------

        /// <summary>
        /// HTML-описания каталогов вычищаем до простого текста: теги убираем,
        /// ключевые структурные теги превращаем в переносы строк, entities
        /// декодируем. Сделано наивно, но для чтения текста глазами - достаточно.
        /// </summary>
        private static string HtmlToPlainText(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return "";

            string text = html;
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"(?i)<(br|/p|/h[1-6]|/li|/div|/tr|/pre)[^>]*>", Environment.NewLine + Environment.NewLine);
            text = System.Text.RegularExpressions.Regex.Replace(
                text, @"(?i)<li[^>]*>", "- ");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]+>", "");
            text = System.Net.WebUtility.HtmlDecode(text);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"(\r?\n){3,}", Environment.NewLine + Environment.NewLine);
            return text.Trim();
        }

        private static string ReadString(JsonElement parent, string property)
        {
            if (parent.ValueKind == JsonValueKind.Object &&
                parent.TryGetProperty(property, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }
            return "";
        }

        private static DateTimeOffset ReadDate(JsonElement parent, string property)
        {
            if (parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return DateTimeOffset.TryParse(value.GetString(), out var date) ? date : DateTimeOffset.MinValue;
            }
            return DateTimeOffset.MinValue;
        }

        private static bool StringArrayMatches(JsonElement parent, string property, string expected, bool when)
        {
            if (!when || expected.Length == 0) return true;

            if (parent.TryGetProperty(property, out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String &&
                        string.Equals(item.GetString(), expected, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
