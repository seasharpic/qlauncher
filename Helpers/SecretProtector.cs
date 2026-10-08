using System;
using System.Security.Cryptography;
using System.Text;

namespace MinecraftLauncher.Helpers
{
    /// <summary>
    /// Шифрование секретов при сохранении на диск через DPAPI (CurrentUser scope).
    ///
    /// Раньше токены Mojang/Xbox хранились в settings.json открытым текстом, а
    /// «Экспорт настроек» выгружал их в любой выбранный файл. Теперь в JSON попадает
    /// только зашифрованный блоб, который может расшифровать тот же пользователь
    /// на той же машине.
    /// </summary>
    public static class SecretProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("QLauncher.v2.settings");

        private const string Prefix = "dpapi:";

        /// <summary>
        /// Шифрует значение. Пустые строки возвращаются как есть — чтобы не плодить
        /// шифротекст для офлайн-аккаунтов без токена.
        /// </summary>
        public static string? Protect(string? plaintext)
        {
            if (string.IsNullOrEmpty(plaintext)) return plaintext;

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(plaintext);
                byte[] encrypted = ProtectedData.Protect(
                    data,
                    Entropy,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);

                // Base64 в JSON безопаснее raw-байтов, но читаемее hex.
                return Prefix + Convert.ToBase64String(encrypted);
            }
            catch (CryptographicException)
            {
                // Если DPAPI недоступен (редкие конфигурации), честно отказываемся
                // сохранять секрет, а не пишем его открытым текстом.
                return null;
            }
        }

        /// <summary>
        /// Расшифровывает значение. Если префикса нет — считаем, что это старый
        /// формат с открытым текстом, и возвращаем как есть (миграция без потери сессий).
        /// </summary>
        public static string? Unprotect(string? stored)
        {
            if (string.IsNullOrEmpty(stored)) return stored;
            if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored;

            try
            {
                byte[] encrypted = Convert.FromBase64String(stored[Prefix.Length..]);
                byte[] decrypted = ProtectedData.Unprotect(
                    encrypted,
                    Entropy,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);

                return Encoding.UTF8.GetString(decrypted);
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                // Токен был зашифрован другим пользователем/машиной либо повреждён.
                // Возвращаем пустую строку: аккаунт останется, но потребует повторного входа.
                return "";
            }
        }
    }
}