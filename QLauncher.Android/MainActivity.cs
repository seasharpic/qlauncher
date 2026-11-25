using Android.App;
using Android.Content;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using MinecraftLauncher.Views;

namespace QLauncher.Android
{
    [Activity(
        Label = "QLauncher",
        Theme = "@style/MyTheme.NoActionBar",
        MainLauncher = true,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
    public class MainActivity : AvaloniaMainActivity<MinecraftLauncher.AndroidApp>
    {
        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            AndroidLauncherBridge.OpenInAmethystHandler = TryOpenAmethyst;
            return base.CustomizeAppBuilder(builder)
                .WithInterFont();
        }

        private bool TryOpenAmethyst()
        {
            string[] candidatePackages =
            {
                "org.angelauramc.amethyst",
                "net.kdt.pojavlaunch",
                "net.kdt.pojavlaunch.debug",
                "git.artdeell.mojo"
            };

            var pm = PackageManager;
            if (pm == null) return false;

            foreach (string pkg in candidatePackages)
            {
                var launchIntent = pm.GetLaunchIntentForPackage(pkg);
                if (launchIntent != null)
                {
                    launchIntent.AddFlags(ActivityFlags.NewTask);
                    StartActivity(launchIntent);
                    return true;
                }
            }

            return false;
        }
    }
}
