using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MinecraftLauncher.Services.LaunchEngine.Models
{
    public class MojangManifest
    {
        [JsonPropertyName("latest")]
        public LatestVersions? Latest { get; set; }

        [JsonPropertyName("versions")]
        public List<MojangVersionHeader> Versions { get; set; } = new();
    }

    public class LatestVersions
    {
        [JsonPropertyName("release")]
        public string Release { get; set; } = "";

        [JsonPropertyName("snapshot")]
        public string Snapshot { get; set; } = "";
    }

    public class MojangVersionHeader
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "";

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("releaseTime")]
        public string ReleaseTime { get; set; } = "";
    }

    public class MojangVersionInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("inheritsFrom")]
        public string? InheritsFrom { get; set; }

        [JsonPropertyName("mainClass")]
        public string MainClass { get; set; } = "net.minecraft.client.main.Main";

        [JsonPropertyName("minecraftArguments")]
        public string? MinecraftArguments { get; set; }

        [JsonPropertyName("arguments")]
        public GameArguments? Arguments { get; set; }

        [JsonPropertyName("assetIndex")]
        public AssetIndexInfo? AssetIndex { get; set; }

        [JsonPropertyName("assets")]
        public string? Assets { get; set; }

        [JsonPropertyName("downloads")]
        public VersionDownloads? Downloads { get; set; }

        [JsonPropertyName("libraries")]
        public List<LibraryInfo> Libraries { get; set; } = new();

        [JsonPropertyName("type")]
        public string Type { get; set; } = "release";
    }

    public class GameArguments
    {
        [JsonPropertyName("game")]
        public List<object>? Game { get; set; }

        [JsonPropertyName("jvm")]
        public List<object>? Jvm { get; set; }
    }

    public class AssetIndexInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("sha1")]
        public string Sha1 { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("totalSize")]
        public long TotalSize { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";
    }

    public class VersionDownloads
    {
        [JsonPropertyName("client")]
        public DownloadFile? Client { get; set; }

        [JsonPropertyName("server")]
        public DownloadFile? Server { get; set; }
    }

    public class DownloadFile
    {
        [JsonPropertyName("sha1")]
        public string Sha1 { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";
    }

    public class LibraryInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("downloads")]
        public LibraryDownloads? Downloads { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("rules")]
        public List<RuleInfo>? Rules { get; set; }

        [JsonPropertyName("natives")]
        public Dictionary<string, string>? Natives { get; set; }
    }

    public class LibraryDownloads
    {
        [JsonPropertyName("artifact")]
        public DownloadFile? Artifact { get; set; }

        [JsonPropertyName("classifiers")]
        public Dictionary<string, DownloadFile>? Classifiers { get; set; }
    }

    public class RuleInfo
    {
        [JsonPropertyName("action")]
        public string Action { get; set; } = "allow";

        [JsonPropertyName("os")]
        public OsRule? Os { get; set; }

        [JsonPropertyName("features")]
        public Dictionary<string, bool>? Features { get; set; }
    }

    public class OsRule
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("arch")]
        public string? Arch { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }
    }
}
