using MinecraftLauncher.Common;

namespace MinecraftLauncher.Models
{
    public class ModpackProfile : ObservableObject
    {
        private string _name = "";
        private string _loader = "";
        private string _version = "";
        private string _gameVersion = "";
        private string _folderPath = "";
        private long _playtimeMinutes = 0;
        private int _launchCount = 0;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string Loader
        {
            get => _loader;
            set => SetProperty(ref _loader, value);
        }

        public string Version
        {
            get => _version;
            set => SetProperty(ref _version, value);
        }

        public string GameVersion
        {
            get => _gameVersion;
            set => SetProperty(ref _gameVersion, value);
        }

        public string FolderPath
        {
            get => _folderPath;
            set => SetProperty(ref _folderPath, value);
        }

        public long PlaytimeMinutes
        {
            get => _playtimeMinutes;
            set => SetProperty(ref _playtimeMinutes, value);
        }

        public int LaunchCount
        {
            get => _launchCount;
            set => SetProperty(ref _launchCount, value);
        }
    }
}
