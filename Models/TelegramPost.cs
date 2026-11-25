namespace MinecraftLauncher.Models
{
    public class TelegramPost
    {
        public string Date { get; set; } = "";
        public string Text { get; set; } = "";
        public string ImageUrl { get; set; } = "";
        public bool HasImage => !string.IsNullOrEmpty(ImageUrl);
    }
}
