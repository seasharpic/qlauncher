using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;
using MinecraftLauncher.Common;

namespace MinecraftLauncher.Models
{
    public class AccountProfile : ObservableObject
    {
        private string _nickname = "";
        private string _accessToken = "";
        private string _uuid = "";
        private BitmapImage? _avatarImage;

        public string Nickname
        {
            get => _nickname;
            set => SetProperty(ref _nickname, value);
        }

        /// <summary>
        /// Токен сессии Mojang/Xbox. В памяти — обычная строка, на диск сериализуется
        /// зашифрованной через DPAPI (см. <see cref="ProtectedStringConverter"/>).
        /// </summary>
        [JsonConverter(typeof(ProtectedStringConverter))]
        public string AccessToken
        {
            get => _accessToken;
            set => SetProperty(ref _accessToken, value);
        }

        public string Uuid
        {
            get => _uuid;
            set => SetProperty(ref _uuid, value);
        }

        [JsonIgnore]
        public BitmapImage? AvatarImage
        {
            get => _avatarImage;
            set => SetProperty(ref _avatarImage, value);
        }

        public override string ToString() => Nickname;
    }
}
