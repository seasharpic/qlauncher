using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MinecraftLauncher.Helpers;
using MinecraftLauncher.Models;

namespace MinecraftLauncher.Services
{
    /// <summary>
    /// Результат авторизации Ely.by (Yggdrasil).
    /// </summary>
    public sealed class ElyBySession
    {
        /// <summary>Аккаунт защищён 2FA — нужна повторная попытка с кодом из приложения.</summary>
        public bool NeedsTwoFactor { get; init; }

        public bool IsUsable => !string.IsNullOrEmpty(AccessToken);

        public string AccessToken { get; init; } = "";
        public string Uuid { get; init; } = "";
        public string Username { get; init; } = "";

        /// <summary>Текст для тоста об ошибке (errorMessage сервера, форматированный).</summary>
        public string ErrorMessage { get; init; } = "";

        /// <summary>
        /// OAuth2 refresh-токен. Приходит только через device-вход (scope
        /// offline_access); при парольном Yggdrasil-входе остаётся пустым —
        /// там продление идёт клиентским clientToken через /auth/refresh.
        /// </summary>
        public string RefreshToken { get; init; } = "";
    }

    /// <summary>
    /// Параметры ожидания device-входа (RFC 8628).
    /// </summary>
    public sealed class ElyByDeviceAuthStart
    {
        public string DeviceCode { get; init; } = "";
        public string UserCode { get; init; } = "";
        public string VerificationUri { get; init; } = "";

        /// <summary>Тот же адрес, но с уже подставленным кодом: открываем его, пользователю остаётся только подтвердить.</summary>
        public string VerificationUriComplete { get; init; } = "";
        public int IntervalSeconds { get; init; } = 5;
        public int ExpiresInSeconds { get; init; } = 600;
    }

    /// <summary>Состояние одного опроса токена по RFC 8628.</summary>
    internal enum DevicePollState
    {
        Success,
        Pending,
        SlowDown,
        Expired,
        Denied,
        NetworkError
    }

    /// <summary>
    /// Авторизация Ely.by по Yggdrasil-протоколу (docs.ely.by, "Authentication for Minecraft").
    ///
    /// Ровно то, что умеет PineconeMC из экосистемы Ely.by, но проще его OAuth2:
    /// у Yggdrasil-входа нет обязательной регистрации приложения, поэтому
    /// аккаунт добавляется прямо логином и паролем, как в Legacy Launcher.
    /// OAuth2 (вход через браузер, пароль не проходит через лаунчер) можно
    /// добавить позже — он потребует зарегистрированного clientId/secret.
    ///
    /// Первый вход: POST /auth/authenticate (логин + пароль, при 2FA — пароль:код).
    /// Дальше хранится только accessToken и живёт годами: каждое продление
    /// через POST /auth/refresh возвращает свежий токен. Пароль лаунчер
    /// нигде не сохраняет и после успешного входа не запоминает.
    ///
    /// Access-токен сериализуется в settings.json зашифрованным (DPAPI) —
    /// он уже лежит в <see cref="AccountProfile.AccessToken"/> с конвертером
    /// <see cref="ProtectedStringConverter"/>.
    /// </summary>
    public sealed class ElyByAuthService
    {
        private const string AuthServerBase = "https://authserver.ely.by";

        /// <summary>
        /// clientId приложения, зарегистрированного на account.ely.by.
        /// Публичная строка — у PineconeMC она такая же по духу ("pineconemc").
        /// В device-потоке секрет не участвует и весь обмен изображён на
        /// стороне пользователя в браузере, поэтому коммитить clientId безопасно.
        /// </summary>
        private const string ElyClientId = "qlauncher";
        private const string DeviceCodeEndpoint = "https://account.ely.by/api/oauth2/v1/devicecode";
        private const string OAuthTokenEndpoint = "https://account.ely.by/api/oauth2/v1/token";
        private const string AccountInfoEndpoint = "https://account.ely.by/api/account/v1/info";

        /// <summary>Ровно те же права, что запрашивает PineconeMC.</summary>
        private const string OAuthScopes = "account_info offline_access minecraft_server_session";
        private const string InjectorReleasesApi = "https://api.github.com/repos/yushijinhun/authlib-injector/releases/latest";
        private const string InjectorMirrorList = "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/list.json";
        private const string InjectorMirrorBase = "https://bmclapi2.bangbang93.com";

        /// <summary>Официальная библиотека, подменяющая authlib на authserver.ely.by.</summary>
        private const string InjectorAgentSuffix = "ely.by";

        private static readonly Lazy<ElyByAuthService> Lazy = new(() => new ElyByAuthService());

        public static ElyByAuthService Instance => Lazy.Value;

        private readonly SemaphoreSlim _gate = new(1, 1);

        // Локализация строк ошибок для тостов.
        private static readonly ILocalizationService _loc = LocalizationService.Instance;

        private ElyByAuthService()
        {
        }

        /// <summary>
        /// clientToken конкретной установки лаунчера. Создаётся один раз и
        /// хранится в settings.json: сервер Ely.by привязывает access-токены
        /// к паре accessToken+clientToken, и потеря clientToken делает все
        /// сохранённые токены недействительными.
        /// </summary>
        private string GetOrCreateClientToken()
        {
            var settings = SettingsService.Instance.Settings;

            if (!string.IsNullOrWhiteSpace(settings.ElyByClientToken))
            {
                return settings.ElyByClientToken;
            }

            settings.ElyByClientToken = Guid.NewGuid().ToString("D");
            try
            {
                SettingsService.Instance.Save();
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("ElyByAuth", "Failed to persist ElyByClientToken", ex);
            }

            return settings.ElyByClientToken;
        }

        /// <summary>Штатный POST JSON через общий проверенный клиент.</summary>
        private static async Task<System.Net.Http.HttpResponseMessage> PostJsonAsync(
            string url, object body, CancellationToken cancellationToken)
        {
            string json = JsonSerializer.Serialize(body);
            var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
            return await SecureHttp.Shared.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Первый вход по логину и паролю. Если у аккаунта включена двухфакторная
        /// аутентификация, Ely.by отвечает ошибкой "Account protected with two
        /// factor auth." — тогда результат помечается NeedsTwoFactor, и вызывающий
        /// код запрашивает у пользователя 6-значный код, а эта команда повторяется
        /// с паролем вида "пароль:код".
        /// </summary>
        public async Task<ElyBySession> LoginAsync(
            string username, string password, string? totpCode = null,
            CancellationToken cancellationToken = default)
        {
            // Код 2FA конкатенируется к паролю через ':' — так протокол
            // Yggdrasil, не знающий про TOTP, передаёт оба значения разом.
            string fullPassword = password;
            if (!string.IsNullOrWhiteSpace(totpCode))
            {
                fullPassword = $"{password}:{totpCode.Trim()}";
            }

            var body = new
            {
                username = username.Trim(),
                password = fullPassword,
                clientToken = GetOrCreateClientToken(),
                requestUser = true
            };

            using var response = await PostJsonAsync(
                $"{AuthServerBase}/auth/authenticate", body, cancellationToken).ConfigureAwait(false);

            string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return ParseSession(content);
            }

            var error = TryParseError(content);

            // Двухфакторная аутентификация определяется по тексту ошибки:
            // специального кода у Yggdrasil для неё нет.
            bool needsTwoFactor =
                (error?.errorMessage?.Contains("two factor", StringComparison.OrdinalIgnoreCase) ?? false) ||
                (error?.error?.Contains("two factor", StringComparison.OrdinalIgnoreCase) ?? false);

            if (needsTwoFactor)
            {
                return new ElyBySession { NeedsTwoFactor = true };
            }

            return new ElyBySession
            {
                ErrorMessage = error?.errorMessage
                    ?? _loc.GetString("Str_Ely_AuthFailedGeneric")
            };
        }

        // ------------------------------------------------------------------
        // Device Code Flow — вход как в PineconeMC (их ElyDeviceCodeStep.cpp):
        // лаунчер никогда не видит пароль. Пользователь вводит короткий код
        // на account.ely.by, а токены лаунчер получает опросом сервера.
        // ------------------------------------------------------------------

        /// <summary>
        /// Первый шаг: сервер выдаёт короткий код и полный адрес страницы
        /// авторизации с уже вставленным кодом.
        /// </summary>
        public async Task<ElyByDeviceAuthStart?> StartDeviceAuthAsync(
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var content = new System.Net.Http.FormUrlEncodedContent(new Dictionary<string, string?>
                {
                    ["client_id"] = ElyClientId,
                    ["scope"] = OAuthScopes
                });

                using var response = await SecureHttp.Shared.PostAsync(
                    DeviceCodeEndpoint, content, cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    CrashLogWriter.Write("ElyByAuth",
                        $"Device code request returned {response.StatusCode}", null);
                    return null;
                }

                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var root = TryParseRoot(body);
                if (root == null)
                {
                    return null;
                }

                string deviceCode = GetString(root, "device_code") ?? "";
                string userCode = GetString(root, "user_code") ?? "";

                if (deviceCode.Length == 0 || userCode.Length == 0)
                {
                    CrashLogWriter.Write("ElyByAuth", "Device code response misses fields", null);
                    return null;
                }

                string verificationUri = GetString(root, "verification_uri") ?? "https://account.ely.by/oauth2/device";
                string complete = GetString(root, "verification_uri_complete")
                    ?? $"{verificationUri}?code={Uri.EscapeDataString(userCode)}";

                return new ElyByDeviceAuthStart
                {
                    DeviceCode = deviceCode,
                    UserCode = userCode,
                    VerificationUri = verificationUri,
                    VerificationUriComplete = complete,
                    IntervalSeconds = GetInt(root, "interval") ?? 5,
                    ExpiresInSeconds = GetInt(root, "expires_in") ?? 600
                };
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("ElyByAuth", "Device code request failed", ex);
                return null;
            }
        }

        /// <summary>
        /// Опрашивает сервер по предписанному интервалу, пока пользователь
        /// вводит код в браузере. Возвращает сессию или результат с ошибкой
        /// (истёк код, вход отклонён), но никогда не исключение из-за сети —
        /// единичный сбой не должен рвать ожидание.
        /// </summary>
        public async Task<ElyBySession?> WaitForDeviceTokenAsync(
            ElyByDeviceAuthStart start,
            CancellationToken cancellationToken = default)
        {
            int interval = Math.Max(5, start.IntervalSeconds);
            DateTime deadline = DateTime.UtcNow.AddSeconds(
                Math.Clamp(start.ExpiresInSeconds, 60, 1200));

            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(interval), cancellationToken).ConfigureAwait(false);

                ElyBySession? session;
                DevicePollState state;
                try
                {
                    (session, state) = await PollDeviceTokenAsync(start.DeviceCode, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    CrashLogWriter.Write("ElyByAuth", "Device token poll failed once", ex);
                    session = null;
                    state = DevicePollState.NetworkError;
                }

                switch (state)
                {
                    case DevicePollState.Success:
                        return session;

                    case DevicePollState.Pending:
                        continue;

                    case DevicePollState.SlowDown:
                        interval += 5;
                        continue;

                    case DevicePollState.Expired:
                        return session ?? new ElyBySession { ErrorMessage = _loc.GetString("Str_Ely_DeviceExpired") };

                    case DevicePollState.Denied:
                        return session ?? new ElyBySession { ErrorMessage = _loc.GetString("Str_Ely_DeviceDenied") };

                    case DevicePollState.NetworkError:
                        // Сетевые сбои между опросами штатны: ждать дальше.
                        continue;
                }
            }

            return new ElyBySession { ErrorMessage = _loc.GetString("Str_Ely_DeviceExpired") };
        }

        /// <summary>
        /// Один обмен на сервере: token-эндпоинт с grant_type=device_code.
        /// Пока пользователь не ввёл код, сервер отвечает authorization_pending.
        /// </summary>
        private async Task<(ElyBySession? Session, DevicePollState State)> PollDeviceTokenAsync(
            string deviceCode, CancellationToken cancellationToken)
        {
            using var content = new System.Net.Http.FormUrlEncodedContent(new Dictionary<string, string?>
            {
                ["client_id"] = ElyClientId,
                ["device_code"] = deviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            });

            using var response = await SecureHttp.Shared.PostAsync(
                OAuthTokenEndpoint, content, cancellationToken).ConfigureAwait(false);

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var root = TryParseRoot(body);

            if (response.IsSuccessStatusCode && root != null)
            {
                string accessToken = GetString(root, "access_token") ?? "";
                if (accessToken.Length == 0)
                {
                    return (null, DevicePollState.NetworkError);
                }

                // Access-токен сам по себе не сообщает ни ник, ни UUID профиля —
                // получаем их из account_info.
                var info = await GetAccountInfoAsync(accessToken, cancellationToken).ConfigureAwait(false);

                return (new ElyBySession
                {
                    AccessToken = accessToken,
                    RefreshToken = GetString(root, "refresh_token") ?? "",
                    Username = info?.Username ?? "",
                    Uuid = info?.Uuid ?? ""
                }, DevicePollState.Success);
            }

            string error = root != null ? GetString(root, "error") ?? "" : response.StatusCode.ToString();

            return error switch
            {
                "authorization_pending" => (null, DevicePollState.Pending),
                "slow_down" => (null, DevicePollState.SlowDown),
                "expired_token" => (null, DevicePollState.Expired),
                "access_denied" => (null, DevicePollState.Denied),
                _ => (new ElyBySession
                {
                    ErrorMessage = root != null
                        ? GetString(root, "error_description") ?? error
                        : error
                }, DevicePollState.Denied)
            };
        }

        /// <summary>Ник и UUID профиля по access-токену с правом account_info.</summary>
        private sealed record ElyAccountInfo(string Username, string Uuid);

        private static async Task<ElyAccountInfo?> GetAccountInfoAsync(string accessToken, CancellationToken cancellationToken)
        {
            try
            {
                using var request = new System.Net.Http.HttpRequestMessage(
                    System.Net.Http.HttpMethod.Get, AccountInfoEndpoint);
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                using var response = await SecureHttp.Shared.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    CrashLogWriter.Write("ElyByAuth",
                        $"Account info returned {response.StatusCode}", null);
                    return null;
                }

                var root = TryParseRoot(
                    await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

                if (root == null)
                {
                    return null;
                }

                return new ElyAccountInfo(
                    GetString(root, "username") ?? "",
                    GetString(root, "uuid") ?? "");
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("ElyByAuth", "Account info request failed", ex);
                return null;
            }
        }

        /// <summary>
        /// Продлевает сессию. Ely.by, в отличие от Mojang, рекомендует вместо
        /// чистой validate сразу делать refresh: старый токен становится
        /// недействительным, взамен приходит свежий и живёт дальше.
        /// Возвращает null, когда продлить не удалось (ожидается повторный
        /// вход по паролю), либо когда запросить сеть не вышло.
        /// </summary>
        public async Task<ElyBySession?> TryRefreshAsync(
            AccountProfile account,
            CancellationToken cancellationToken = default)
        {
            // Аккаунты, вошедшие через браузер, живут по OAuth2: их access-токен
            // действует ограниченное время и продлевается refresh-токеном.
            // Парольные аккаунты идут старым Yggdrasil-путём ниже.
            if (!string.IsNullOrWhiteSpace(account.ElyOauthRefreshToken))
            {
                return await RefreshByOAuthGrantAsync(account, cancellationToken).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(account.AccessToken)) return null;

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var body = new
                {
                    accessToken = account.AccessToken,
                    clientToken = GetOrCreateClientToken(),
                    requestUser = true
                };

                using var response = await PostJsonAsync(
                    $"{AuthServerBase}/auth/refresh", body, cancellationToken).ConfigureAwait(false);

                string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    // Токен отозван сервером или変更ён — дальше этой сессии нет.
                    return new ElyBySession
                    {
                        ErrorMessage = TryParseError(content)?.errorMessage ?? _loc.GetString("Str_Ely_SessionExpired")
                    };
                }

                return ParseSession(content, fallbackUsername: account.Nickname, fallbackUuid: account.Uuid);
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("ElyByAuth", $"Refresh failed for '{account.Nickname}'", ex);
                return null;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Отзывает access-токен на стороне сервера. Используется при удалении
        /// аккаунта. Осторожно: если токен не найден, сервер МОЛЧА отвечает
        /// успехом — это штатное поведение Yggdrasil.
        /// </summary>
        public async Task InvalidateAsync(AccountProfile account)
        {
            try
            {
                var body = new
                {
                    accessToken = account.AccessToken,
                    clientToken = GetOrCreateClientToken()
                };

                using var response = await PostJsonAsync(
                    $"{AuthServerBase}/auth/invalidate", body, CancellationToken.None).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    CrashLogWriter.Write("ElyByAuth",
                        $"Invalidate returned {response.StatusCode} for '{account.Nickname}'", null);
                }
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("ElyByAuth", $"Invalidate failed for '{account.Nickname}'", ex);
            }
        }

        /// <summary>
        /// Гарантирует, что authlib-injector.jar скачан, и возвращает путь к нему.
        /// Jar нужен для скинов Ely.by в игре: игра вызвает sessionserver Mojang,
        /// но при агентах Ely.by сессия подменяется на authserver.ely.by.
        /// Кэшируется один файл на все версии игры.
        /// </summary>
        public async Task<string?> EnsureInjectorJarAsync()
        {
            string cacheDir = Path.Combine(LauncherPathHelper.GetDefaultDataDirectory(), "authlib");
            string jarPath = Path.Combine(cacheDir, "authlib-injector.jar");

            if (File.Exists(jarPath) && new FileInfo(jarPath).Length > 0)
            {
                return jarPath;
            }

            List<string> urls = await GetInjectorDownloadUrlsAsync().ConfigureAwait(false);
            if (urls.Count == 0)
            {
                return null;
            }

            try
            {
                byte[]? bytes = await SecureHttp.TryDownloadBytesAsync(
                    urls, TimeSpan.FromSeconds(180), CancellationToken.None).ConfigureAwait(false);

                if (bytes == null || bytes.Length == 0)
                {
                    return null;
                }

                Directory.CreateDirectory(cacheDir);
                await File.WriteAllBytesAsync(jarPath, bytes, CancellationToken.None).ConfigureAwait(false);
                return jarPath;
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("ElyByAuth", "Failed to download authlib-injector.jar", ex);
                return null;
            }
        }

        /// <summary>
        /// URL последнего релиза authlib-injector: сначала GitHub API,
        /// при недоступности — зеркало BMCLAPI (официально поддерживается
        /// самим проектом authlib-injector для регионов со сложной сетью).
        /// </summary>
        private static async Task<List<string>> GetInjectorDownloadUrlsAsync()
        {
            var urls = new List<string>();

            try
            {
                using var response = await SecureHttp.Shared.GetAsync(InjectorReleasesApi, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);
                if (response.IsSuccessStatusCode)
                {
                    var json = JsonDocument.Parse(
                        await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                    var asset = json.RootElement
                        .GetProperty("assets")
                        .EnumerateArray()
                        .FirstOrDefault(a => a.GetProperty("name").GetString()?.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) == true);

                    string? downloadUrl = asset.ValueKind == JsonValueKind.Object
                        ? asset.GetProperty("browser_download_url").GetString()
                        : null;

                    if (!string.IsNullOrEmpty(downloadUrl))
                    {
                        urls.Add(downloadUrl);
                    }
                }
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("ElyByAuth", "GitHub releases lookup failed, trying mirror", ex);
            }

            try
            {
                using var response = await SecureHttp.Shared.GetAsync(InjectorMirrorList, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None);
                if (response.IsSuccessStatusCode)
                {
                    var json = JsonDocument.Parse(
                        await response.Content.ReadAsStringAsync().ConfigureAwait(false));

                    string? relative = json.RootElement
                        .EnumerateArray()
                        .LastOrDefault()
                        .GetProperty("url")
                        .GetString();

                    if (!string.IsNullOrEmpty(relative))
                    {
                        urls.Add(InjectorMirrorBase + relative);
                    }
                }
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("ElyByAuth", "Mirror artifact list lookup failed", ex);
            }

            return urls;
        }

        /// <summary>
        /// Разбирает ответ сервера. id профиля приходит без дефисов —
        /// приводим к стандартному виду 8-4-4-4-12, как у Mojang-аккаунтов.
        /// </summary>
        private static ElyBySession ParseSession(string json, string? fallbackUsername = null, string? fallbackUuid = null)
        {
            var root = JsonDocument.Parse(json).RootElement;

            string accessToken = root.TryGetProperty("accessToken", out var t) ? t.GetString() ?? "" : "";

            string uuid = fallbackUuid ?? "";
            string username = fallbackUsername ?? "";

            if (root.TryGetProperty("selectedProfile", out var profile) &&
                profile.ValueKind == JsonValueKind.Object)
            {
                uuid = profile.TryGetProperty("id", out var id) ? FormatUuidWithDashes(id.GetString() ?? "") : uuid;
                username = profile.TryGetProperty("name", out var n) ? n.GetString() ?? username : username;
            }

            return new ElyBySession
            {
                AccessToken = accessToken,
                Uuid = uuid,
                Username = username
            };
        }

        private static (string? error, string? errorMessage)? TryParseError(string json)        {
            try
            {
                var root = JsonDocument.Parse(json).RootElement;

                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("error", out var e))
                {
                    string? errorMessage = root.TryGetProperty("errorMessage", out var m)
                        ? m.GetString()
                        : null;

                    return (e.GetString(), errorMessage);
                }
            }
            catch (JsonException)
            {
                // Не JSON (прокси-заглушка и т.п.) — ошибки разобрать нечем.
            }

            return null;
        }

        /// <summary>
        /// Продление OAuth-сессии: обмен refresh_token на новый access_token
        /// (RFC 8628 + docs.ely.by "Refreshing access token"; для десктопного
        /// приложения передаётся только client_id, секрета у него нет).
        /// Если сервер вернул новый refresh_token, он обязателен для
        /// сохранения — старый после обновления недействителен.
        /// </summary>
        private async Task<ElyBySession?> RefreshByOAuthGrantAsync(
            AccountProfile account, CancellationToken cancellationToken)
        {
            try
            {
                using var content = new System.Net.Http.FormUrlEncodedContent(new Dictionary<string, string?>
                {
                    ["client_id"] = ElyClientId,
                    ["refresh_token"] = account.ElyOauthRefreshToken,
                    ["grant_type"] = "refresh_token",
                    ["scope"] = OAuthScopes
                });

                using var response = await SecureHttp.Shared.PostAsync(
                    OAuthTokenEndpoint, content, cancellationToken).ConfigureAwait(false);

                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var root = TryParseRoot(body);

                if (root == null || !response.IsSuccessStatusCode)
                {
                    string description = root != null ? GetString(root, "error_description") ?? "" : "";
                    return new ElyBySession
                    {
                        ErrorMessage = description.Length > 0
                            ? description
                            : _loc.GetString("Str_Ely_SessionExpired")
                    };
                }

                string accessToken = GetString(root, "access_token") ?? "";
                if (accessToken.Length == 0)
                {
                    return null;
                }

                var info = await GetAccountInfoAsync(accessToken, cancellationToken).ConfigureAwait(false);

                return new ElyBySession
                {
                    AccessToken = accessToken,
                    RefreshToken = GetString(root, "refresh_token") ?? account.ElyOauthRefreshToken,
                    Username = !string.IsNullOrEmpty(info?.Username) ? info.Username : account.Nickname,
                    Uuid = !string.IsNullOrEmpty(info?.Uuid) ? info.Uuid : account.Uuid
                };
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("ElyByAuth", $"OAuth refresh failed for '{account.Nickname}'", ex);
                return null;
            }
        }

        /// <summary>Разбор корня JSON с защитой от не-JSON ответов (прокси-заглушки).</summary>
        private static JsonElement? TryParseRoot(string json)
        {
            try
            {
                return JsonDocument.Parse(json).RootElement;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? GetString(JsonElement? root, string name)
        {
            if (root == null) return null;

            try
            {
                if (root.Value.ValueKind == JsonValueKind.Object &&
                    root.Value.TryGetProperty(name, out var el) &&
                    el.ValueKind == JsonValueKind.String)
                {
                    return el.GetString();
                }
            }
            catch (ObjectDisposedException) { }

            return null;
        }

        private static int? GetInt(JsonElement? root, string name)
        {
            if (root == null) return null;

            try
            {
                if (root.Value.ValueKind == JsonValueKind.Object &&
                    root.Value.TryGetProperty(name, out var el) &&
                    el.ValueKind == JsonValueKind.Number &&
                    el.TryGetInt32(out int value))
                {
                    return value;
                }
            }
            catch (ObjectDisposedException) { }

            return null;
        }

        private static string FormatUuidWithDashes(string id)
        {
            string clean = id.Replace("-", "");
            return clean.Length == 32
                ? $"{clean[..8]}-{clean[8..12]}-{clean[12..16]}-{clean[16..20]}-{clean[20..32]}"
                : id;
        }
    }
}
