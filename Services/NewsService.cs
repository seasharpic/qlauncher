using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;
using MinecraftLauncher.Models;

namespace MinecraftLauncher.Services
{
    public interface INewsService
    {
        Task<List<TelegramPost>> FetchTelegramNewsAsync();
    }

    public class NewsService : INewsService
    {
        private readonly IAntiBlockService _antiBlockService;

        public static NewsService Instance { get; } = new NewsService(AntiBlockService.Instance);

        public NewsService(IAntiBlockService antiBlockService)
        {
            _antiBlockService = antiBlockService;
        }

        public async Task<List<TelegramPost>> FetchTelegramNewsAsync()
        {
            try
            {
                const string primaryUrl = "https://t.me/s/QLauncher_MC";
                const string fallbackUrl = "https://tg.rip/s/QLauncher_MC";

                string html = await _antiBlockService.FetchWithFallbackAsync(_antiBlockService.HttpClient, primaryUrl, fallbackUrl);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var posts = new List<TelegramPost>();
                var messageNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'tgme_widget_message_wrap')]");

                if (messageNodes != null)
                {
                    foreach (var node in messageNodes)
                    {
                        var textNode = node.SelectSingleNode(".//div[contains(@class, 'tgme_widget_message_text')]");
                        var dateNode = node.SelectSingleNode(".//time");
                        var photoWrap = node.SelectSingleNode(".//a[contains(@class, 'tgme_widget_message_photo_wrap')]");

                        if (textNode != null || photoWrap != null)
                        {
                            string cleanText = "";
                            if (textNode != null)
                            {
                                string rawText = textNode.InnerHtml.Replace("<br>", "\n").Replace("<br/>", "\n");
                                cleanText = HttpUtility.HtmlDecode(rawText);
                                var textDoc = new HtmlDocument();
                                textDoc.LoadHtml(cleanText);
                                cleanText = textDoc.DocumentNode.InnerText.Trim();
                            }

                            string imageUrl = "";
                            if (photoWrap != null)
                            {
                                string style = photoWrap.GetAttributeValue("style", "");
                                var match = Regex.Match(style, @"url\('(.*?)'\)");
                                if (match.Success)
                                {
                                    imageUrl = match.Groups[1].Value;
                                }
                            }

                            posts.Add(new TelegramPost
                            {
                                Text = cleanText,
                                Date = dateNode?.InnerText ?? LocalizationService.Instance.GetString("Str_News_Recently"),
                                ImageUrl = imageUrl
                            });
                        }
                    }
                }

                if (posts.Count == 0)
                {
                    posts.Add(CreateWelcomePost());
                }

                posts.Reverse();
                return posts;
            }
            catch
            {
                return new List<TelegramPost> { CreateWelcomePost() };
            }
        }

        /// <summary>
        /// Заглушка, когда Telegram недоступен. Раньше текст был зашит по-русски
        /// в двух местах (обычный пустой список и блок catch), поэтому в
        /// английском интерфейсе приветствие оставалось русским.
        /// </summary>
        private static TelegramPost CreateWelcomePost()
        {
            var loc = LocalizationService.Instance;

            return new TelegramPost
            {
                Date = "QLauncher v2.0",
                Text = loc.GetString("Str_News_Welcome") + Environment.NewLine + Environment.NewLine +
                       loc.GetString("Str_News_WelcomeBody")
            };
        }
    }
}
