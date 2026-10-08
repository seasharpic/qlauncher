namespace MinecraftLauncher.Models
{
    public class ScreenshotItem
    {
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
    }

    public class ReleaseInfo
    {
        public string TagName { get; set; } = "";
        public string HtmlUrl { get; set; } = "";
        public string Body { get; set; } = "";
        public bool HasUpdate { get; set; }

        public string DownloadUrl { get; set; } = "";
        public string SourceForgeUrl { get; set; } = "";
        public string ActiveDownloadUrl { get; set; } = "";
        public string FileName { get; set; } = "QLauncher.exe";
        public long FileSizeBytes { get; set; }
        public string FileSizeFormatted { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public bool IsPrerelease { get; set; }
        public string ActiveMirror { get; set; } = "GitHub";
        public string ReleaseDateFormatted { get; set; } = "";
        public string Author { get; set; } = "dyagyatis";
        public string ReleaseTypeBadge { get; set; } = Services.LocalizationService.Instance.GetString("Str_Release_Generic");
        public string FormattedChangelog { get; set; } = "";
    }

    public class CrashAnalysisResult
    {
        public bool HasCrash { get; set; }
        public string Title { get; set; } = Services.LocalizationService.Instance.GetString("Str_Crash_Title");
        public string Summary { get; set; } = "";
        public string Recommendation { get; set; } = "";
        public string LogSnippet { get; set; } = "";
    }
}
