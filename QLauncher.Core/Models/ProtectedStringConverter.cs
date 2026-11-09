using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using MinecraftLauncher.Services;

namespace MinecraftLauncher.Models
{
    /// <summary>
    /// Сериализует секреты как "dpapi:&lt;шифротекст&gt;", а при чтении расшифровывает.
    ///
    /// Благодаря конвертеру не нужно менять ни строки в ViewModel, ни XAML-биндинги:
    /// в памяти свойство остаётся обычной строкой, а на диск уходит только шифротекст.
    /// Платформа (DPAPI/Keychain/libsecret) — за интерфейсом ISecretProtector из ядра.
    /// </summary>
    public class ProtectedStringConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? value = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
            return SecretProtection.Protector?.Unprotect(value) ?? "";
        }

        public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        {
            if (string.IsNullOrEmpty(value))
            {
                writer.WriteStringValue(value);
                return;
            }

            string? protectedValue = SecretProtection.Protector?.Protect(value);
            if (protectedValue == null)
            {
                // Не удалось зашифровать (реализация не зарегистрирована или
                // отказалась) — не сохраняем секрет в открытом виде.
                writer.WriteStringValue("");
                return;
            }

            writer.WriteStringValue(protectedValue);
        }
    }
}