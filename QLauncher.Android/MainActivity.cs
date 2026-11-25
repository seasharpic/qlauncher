using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;

namespace QLauncher.Android
{
    [Activity(
        Label = "QLauncher",
        Theme = "@style/MyTheme.NoActionBar",
        Icon = "@mipmap/ic_launcher",
        MainLauncher = true,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
    public class MainActivity : AvaloniaMainActivity<MinecraftLauncher.App>
    {
        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            return base.CustomizeAppBuilder(builder)
                .WithInterFont();
        }
    }
}
