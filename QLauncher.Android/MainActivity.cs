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
                "org.angelauramc.amethyst.debug",
                "net.kdt.pojavlaunch",
                "net.kdt.pojavlaunch.debug",
                "com.movtery.zalithlauncher",
                "com.movtery.zalithlauncher.debug",
                "git.artdeell.mojo",
                "git.artdeell.mojo.debug"
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

            // Fallback: scan launcher activities for Amethyst/Pojav/Zalith/Mojo forks
            var mainIntent = new Intent(Intent.ActionMain);
            mainIntent.AddCategory(Intent.CategoryLauncher);
            var resolved = pm.QueryIntentActivities(mainIntent, 0);
            if (resolved != null)
            {
                foreach (var info in resolved)
                {
                    var pkgName = info.ActivityInfo?.PackageName;
                    if (string.IsNullOrEmpty(pkgName) || pkgName == PackageName)
                        continue;

                    if (pkgName.Contains("amethyst", StringComparison.OrdinalIgnoreCase) ||
                        pkgName.Contains("pojav", StringComparison.OrdinalIgnoreCase) ||
                        pkgName.Contains("zalith", StringComparison.OrdinalIgnoreCase) ||
                        pkgName.Contains("mojo", StringComparison.OrdinalIgnoreCase))
                    {
                        var launchIntent = pm.GetLaunchIntentForPackage(pkgName);
                        if (launchIntent != null)
                        {
                            launchIntent.AddFlags(ActivityFlags.NewTask);
                            StartActivity(launchIntent);
                            return true;
                        }
                    }
                }
            }

            return false;
        }
    }
}
