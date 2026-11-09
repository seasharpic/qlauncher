using System;

namespace MinecraftLauncher.Services
{
    /// <summary>
    /// Шифрование секретов для хранения на диске (токены аккаунтов,
    /// API-ключи каталогов). Ядро не знает о DPAPI/Keychain/libsecret:
    /// платформенная реализация регистрируется хостом в SecretProtection.
    /// </summary>
    public interface ISecretProtector
    {
        /// <summary>
        /// Шифрует значение. Пустые/NULL возвращает как есть, при любой
        /// ошибке NULL — никогда не сохраняем секрет открытым текстом.
        /// </summary>
        string? Protect(string? plaintext);

        /// <summary>
        /// Расшифровывает значение из хранилища. Значения без префикса
        /// (старый формат) возвращаются как есть — миграция без потери сессий.
        /// Повреждённые секреты дают пустую строку, а не исключение.
        /// </summary>
        string? Unprotect(string? stored);
    }

    /// <summary>
    /// Хост-приложение регистрирует здесь платформенную реализацию
    /// (на Windows — DPAPI через Helpers.SecretProtector) при старте,
    /// до первой десериализации настроек.
    /// </summary>
    public static class SecretProtection
    {
        public static ISecretProtector? Protector { get; set; }
    }
}
