using System.Windows.Media;
using MinecraftLauncher.Common;

namespace MinecraftLauncher.Models
{
    public class ServerItem : ObservableObject
    {
        private string _name = "";
        private string _ip = "";
        private string _onlineText = Services.LocalizationService.Instance.GetString("Str_Server_Checking");
        private string _version = "...";
        private double _progressWidth = 0;
        private string _pingText = "";
        private Brush _pingColor = new SolidColorBrush(Color.FromRgb(150, 150, 150));

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string Ip
        {
            get => _ip;
            set => SetProperty(ref _ip, value);
        }

        public string OnlineText
        {
            get => _onlineText;
            set => SetProperty(ref _onlineText, value);
        }

        public string Version
        {
            get => _version;
            set => SetProperty(ref _version, value);
        }

        public double ProgressWidth
        {
            get => _progressWidth;
            set => SetProperty(ref _progressWidth, value);
        }

        public string PingText
        {
            get => _pingText;
            set => SetProperty(ref _pingText, value);
        }

        public Brush PingColor
        {
            get => _pingColor;
            set => SetProperty(ref _pingColor, value);
        }
    }

    public class ServerStatus
    {
        public bool Online { get; set; }
        public int PlayersNow { get; set; }
        public int PlayersMax { get; set; }
        public string Version { get; set; } = "";
        public string Motd { get; set; } = "";
        public long PingMs { get; set; } = -1;
    }
}
