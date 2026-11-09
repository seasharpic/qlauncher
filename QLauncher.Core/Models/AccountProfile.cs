using System.Text.Json.Serialization;
using MinecraftLauncher.Common;

namespace MinecraftLauncher.Models
{
    /// <summary>
    /// Аккаунт игры (Microsoft или Ely.by). Ядро знает только данные сессии;
    /// картинка аватарки — [JsonIgnore] и object?: хост-приложение кладёт
    /// туда своё платформенное представление (в WPF — BitmapImage), ядро
    /// не ссылается на UI-типы.
    /// </summary>
    public class AccountProfile : ObservableObject
    {
        private string _nickname = "";
        private string _accessToken = "";
        private string _uuid = "";
        private bool _isElyBy;
        private string _elyOauthRefreshToken = "";
        private object? _avatarImage;

        /// <summary>
        /// Аккаунт Ely.by. Отличается от Microsoft тем, как продлевается
        /// сессия (Yggdrasil /auth/refresh вместо Xbox Live) и тем, что при
        /// запуске той игре подставляется authlib-injector — иначе скины
        /// Ely.by не загрузятся даже с правильным токеном.
        /// </summary>
        public bool IsElyBy
        {
            get => _isElyBy;
            set => SetProperty(ref _isElyBy, value);
        }

        public string Nickname
        {
            get => _nickname;
            set => SetProperty(ref _nickname, value);
        }

        /// <summary>
        /// Токен сессии Mojang/Xbox. В памяти — обычная строка, на диск сериализуется
        /// зашифрованной (см. <see cref="ProtectedStringConverter"/>; реализация
        /// шифрования — ISecretProtector, на Windows DPAPI).
        /// </summary>
        [JsonConverter(typeof(ProtectedStringConverter))]
        public string AccessToken
        {
            get => _accessToken;
            set => SetProperty(ref _accessToken, value);
        }

        /// <summary>
        /// Refresh-токен OAuth2 для аккаунтов Ely.by, вошедших через браузер
        /// (device code flow): позволяет продлевать сессию по OAuth2-гранту
        /// вместо Yggdrasil /auth/refresh. В памяти — обычная строка, на диск
        /// сериализуется зашифрованной (ProtectedStringConverter).
        /// </summary>
        [JsonConverter(typeof(ProtectedStringConverter))]
        public string ElyOauthRefreshToken
        {
            get => _elyOauthRefreshToken;
            set => SetProperty(ref _elyOauthRefreshToken, value);
        }

        public string Uuid
        {
            get => _uuid;
            set => SetProperty(ref _uuid, value);
        }

        /// <summary>UI-представление аватарки; на диск не сохраняется.</summary>
        [JsonIgnore]
        public object? AvatarImage
        {
            get => _avatarImage;
            set => SetProperty(ref _avatarImage, value);
        }

        public override string ToString() => Nickname;
    }
}
