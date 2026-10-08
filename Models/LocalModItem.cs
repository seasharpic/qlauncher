using System.Windows.Media;
using MinecraftLauncher.Common;

namespace MinecraftLauncher.Models
{
    public class LocalModItem : ObservableObject
    {
        private string _fileName = "";
        private string _fullPath = "";
        private bool _isEnabled;

        public string FileName
        {
            get => _fileName;
            set => SetProperty(ref _fileName, value);
        }

        public string FullPath
        {
            get => _fullPath;
            set => SetProperty(ref _fullPath, value);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (SetProperty(ref _isEnabled, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StatusColor));
                    OnPropertyChanged(nameof(ActionText));
                }
            }
        }

        public string StatusText => Services.LocalizationService.Instance.GetString(IsEnabled ? "Str_Mod_Enabled" : "Str_Mod_Disabled");
        public Brush StatusColor => IsEnabled
            ? new SolidColorBrush(Color.FromRgb(80, 220, 100))
            : new SolidColorBrush(Color.FromRgb(220, 80, 80));
        public string ActionText => Services.LocalizationService.Instance.GetString(IsEnabled ? "Str_Mod_Disable" : "Str_Mod_Enable");
    }

    public class ModpackCardItem : ObservableObject
    {
        private string _name = "";
        private string _loaderTag = "";
        private string _gameVersionTag = "";
        private string _playtimeText = "";
        private System.Windows.Visibility _isActiveVisibility = System.Windows.Visibility.Collapsed;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string LoaderTag
        {
            get => _loaderTag;
            set => SetProperty(ref _loaderTag, value);
        }

        public string GameVersionTag
        {
            get => _gameVersionTag;
            set => SetProperty(ref _gameVersionTag, value);
        }

        public string PlaytimeText
        {
            get => _playtimeText;
            set => SetProperty(ref _playtimeText, value);
        }

        public System.Windows.Visibility IsActiveVisibility
        {
            get => _isActiveVisibility;
            set => SetProperty(ref _isActiveVisibility, value);
        }
    }
}
