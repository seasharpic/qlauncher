using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CmlLib.Core.Auth.Microsoft;
using MinecraftLauncher.Helpers;
using XboxAuthNet.Game;
using XboxAuthNet.Game.Accounts;
using XboxAuthNet.Game.SessionStorages;

namespace MinecraftLauncher.Services
{
    /// <summary>
    /// Результат тихого обновления сессии Microsoft.
    /// </summary>
    public sealed class MicrosoftSessionRefresh
    {
        public string AccessToken { get; init; } = "";
        public string Uuid { get; init; } = "";
        public string Username { get; init; } = "";

        /// <summary>Момент, когда токен перестанет приниматься серверами Minecraft.</summary>
        public DateTime ExpiresOn { get; init; }

        public bool IsUsable => !string.IsNullOrEmpty(AccessToken);
    }

    /// <summary>
    /// Тихое обновление токена Microsoft без повторного входа.
    ///
    /// Раньше в проекте не было слова RefreshToken ни разу, а настройки
    /// хранили только access_token. Токен Minecraft живёт около двух часов,
    /// поэтому пользователю приходилось снова и снова проходить вход через
    /// браузер — лаунчер не умел продлевать сессию.
    ///
    /// Как это работает: библиотека CmlLib внутри использует XboxAuthNet,
    /// который умеет обновлять сессию по refresh-токену, но держит его в
    /// памяти. Поэтому хранилище аккаунтов подменяется на файловый
    /// (JsonXboxGameAccountManager), и refresh-токен переживает перезапуск.
    ///
    /// Файл с refresh-токеном шифруется DPAPI: это секрет, который позволяет
    /// войти в аккаунт без пароля, и открытым текстом на диске он лежать
    /// не должен.
    /// </summary>
    public sealed class MicrosoftAuthService
    {
        private const string StoreFileName = "microsoft-accounts.dat";
        private const string TempFileName = "microsoft-accounts.tmp.json";

        private static readonly Lazy<MicrosoftAuthService> Lazy = new(() => new MicrosoftAuthService());

        public static MicrosoftAuthService Instance => Lazy.Value;

        private readonly SemaphoreSlim _gate = new(1, 1);

        // Менеджер создаётся один раз: он читает файл в конструкторе, и
        // повторное создание на каждый вызов означало бы гонку за файл.
        private readonly IXboxGameAccountManager _accounts;
        private readonly JELoginHandler _handler;
        private readonly string _tempPath;

        private MicrosoftAuthService()
        {
            _tempPath = Path.Combine(Path.GetTempPath(), TempFileName);
            RestoreDecrypted(_tempPath);

            _accounts = new JsonXboxGameAccountManager(_tempPath);
            _handler = new JELoginHandlerBuilder()
                .WithAccountManager(_accounts)
                .Build();
        }

        /// <summary>
        /// Обновляет сессию для аккаунта. Возвращает null, если обновить
        /// не удалось и пользователю нужно войти заново.
        /// </summary>
        public async Task<MicrosoftSessionRefresh?> TryRefreshAsync(
            string username,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;

            // Xbox Live добавляет к нику суффикс (Player#1234), а в хранилище
            // сессий он не хранится.
            string gamertag = StripLiveSuffix(username);

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // AuthenticateSilently() умеет обновлять только одну сессию —
                // последнюю использованную, и выбрать аккаунт параметром нельзя.
                // Поэтому нужный аккаунт сначала помечается как последний.
                var target = TryFindAccount(gamertag);
                if (target != null)
                {
                    LastAccessSource.Default.Set(target.SessionStorage, DateTime.UtcNow);
                    LastAccessSource.Default.SetKeyMode(target.SessionStorage, SessionStorageKeyMode.Default);
                }

                var session = await _handler.AuthenticateSilently().ConfigureAwait(false);

                if (session == null || string.IsNullOrEmpty(session.Username) ||
                    string.IsNullOrEmpty(session.AccessToken))
                {
                    return null;
                }

                // Сессий может быть несколько, а обновиться могла не та.
                // Отдавать игре чужой токен хуже, чем не обновить вовсе:
                // пользователь вошёл бы под другим аккаунтом.
                if (!string.Equals(session.Username, gamertag, StringComparison.OrdinalIgnoreCase))
                {
                    CrashLogWriter.Write(
                        "MicrosoftAuth",
                        $"Silent refresh returned '{session.Username}' instead of '{gamertag}'",
                        null);

                    return null;
                }

                var account = TryFindAccount(session.Username);

                PersistEncrypted();

                return new MicrosoftSessionRefresh
                {
                    AccessToken = session.AccessToken!,
                    Uuid = session.UUID ?? account?.Profile?.UUID ?? "",
                    Username = session.Username!,
                    ExpiresOn = account?.Token?.ExpiresOn ?? DateTime.MinValue
                };
            }
            catch (Exception ex)
            {
                // Молчаливый отказ: лаунчер должен работать и без сети,
                // а пользователь увидит сообщение об истёкшей сессии позже.
                CrashLogWriter.Write("MicrosoftAuth", $"Silent refresh failed for '{gamertag}'", ex);
                return null;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Выполняет вход через браузер и сохраняет refresh-токен на диск.
        /// После этого TryRefreshAsync работает без участия пользователя.
        /// </summary>
        public async Task<MicrosoftSessionRefresh?> LoginInteractivelyAsync(
            CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var session = await _handler.AuthenticateInteractively().ConfigureAwait(false);

                if (session == null || string.IsNullOrEmpty(session.Username) ||
                    string.IsNullOrEmpty(session.AccessToken))
                {
                    return null;
                }

                var account = TryFindAccount(session.Username);

                // Без этого refresh-токен остался бы в памяти и пропал бы
                // вместе с процессом — тихое обновление перестало бы работать.
                _accounts.SaveAccounts();
                PersistEncrypted();

                return new MicrosoftSessionRefresh
                {
                    AccessToken = session.AccessToken!,
                    Uuid = session.UUID ?? account?.Profile?.UUID ?? "",
                    Username = session.Username!,
                    ExpiresOn = account?.Token?.ExpiresOn ?? DateTime.MinValue
                };
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Сбрасывает все сохранённые сессии Microsoft.
        ///
        /// Отдельный аккаунт удалить нельзя: коллекция сессий в библиотеке
        /// не даёт доступа на удаление элемента, а обход через её
        /// перечисление привёл бы к потере несвязанных аккаунтов. Поэтому
        /// сброс общий — после него пользователь просто входит заново.
        /// Нужен, когда refresh-токен отозван сервером: молчаливое
        /// обновление перестало бы работать, и без сброса лаунчер
        /// бесконечно пытался бы обновить несуществующую сессию.
        /// </summary>
        public void SignOutAll()
        {
            lock (_gate)
            {
                try
                {
                    _accounts.ClearAccounts();

                    if (File.Exists(_tempPath)) File.Delete(_tempPath);
                    if (File.Exists(StorePath)) File.Delete(StorePath);
                }
                catch (Exception ex)
                {
                    CrashLogWriter.Write("MicrosoftAuth", "Failed to sign out", ex);
                }
            }
        }

        /// <summary>
        /// Ищет аккаунт в хранилище сессий, не бросая исключений.
        ///
        /// GetJEAccountByUsername внутри построен на Enumerable.First и
        /// БРОСАЕТ InvalidOperationException, если аккаунта нет, а не
        /// возвращает null. Наивная проверка на null поэтому не работала:
        /// первое же отсутствие аккаунта роняло обновление сессии целиком,
        /// хотя сама сессия была получена. Здесь отсутствие — это null.
        /// </summary>
        private CmlLib.Core.Auth.Microsoft.Sessions.JEGameAccount? TryFindAccount(string username)
        {
            try
            {
                return _accounts.GetAccounts().GetJEAccountByUsername(username);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>
        /// Убирает суффикс, который Xbox Live добавляет к нику (Player#1234).
        /// В настройках ник хранится с этим суффиксом, а в хранилище сессий — без.
        /// </summary>
        private static string StripLiveSuffix(string username)
        {
            int hash = username.IndexOf('#');
            return hash > 0 ? username[..hash] : username;
        }

        private static string StorePath =>
            Path.Combine(LauncherPathHelper.GetDefaultDataDirectory(), StoreFileName);

        /// <summary>Кладёт расшифрованный JSON во временный файл для библиотеки.</summary>
        private static void RestoreDecrypted(string tempPath)
        {
            try
            {
                string? encrypted = File.Exists(StorePath) ? File.ReadAllText(StorePath) : null;
                if (string.IsNullOrEmpty(encrypted)) return;

                string? json = SecretProtector.Unprotect(encrypted);
                if (string.IsNullOrEmpty(json)) return;

                File.WriteAllText(tempPath, json);
            }
            catch (Exception ex)
            {
                // Повреждённый или чужой файл — начинаем с чистого состояния,
                // пользователь просто войдёт заново.
                CrashLogWriter.Write("MicrosoftAuth", "Failed to restore account store", ex);
            }
        }

        /// <summary>Зашифровывает временный файл и убирает его с диска.</summary>
        private void PersistEncrypted()
        {
            try
            {
                if (!File.Exists(_tempPath)) return;

                string json = File.ReadAllText(_tempPath);
                string? encrypted = SecretProtector.Protect(json);

                if (encrypted != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                    File.WriteAllText(StorePath, encrypted);
                }

                File.Delete(_tempPath);
            }
            catch (Exception ex)
            {
                CrashLogWriter.Write("MicrosoftAuth", "Failed to persist account store", ex);
            }
        }
    }
}