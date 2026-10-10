using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace SchoolLibrary.Data.Models
{
    public class Resource
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Заглавието е задължително")]
        [MaxLength(100, ErrorMessage = "Заглавието не може да бъде по-дълго от 100 символа")]
        public string Title { get; set; } = string.Empty;

        [MaxLength(1000, ErrorMessage = "Описанието не може да бъде по-дълго от 1000 символа")]
        public string Description { get; set; } = string.Empty;

        [Required(ErrorMessage = "URL адресът е задължителен")]
        [MaxLength(500, ErrorMessage = "URL адресът не може да бъде по-дълъг от 500 символа")]
        public string Url { get; set; } = string.Empty;

        public DateTime CreatedOn { get; set; }

        public int CategoryId { get; set; }
        public Category? Category { get; set; }

        public int GradeLevelId { get; set; }
        public GradeLevel? GradeLevel { get; set; }

        public string OwnerId { get; set; } = string.Empty;
        public IdentityUser? Owner { get; set; }

        [NotMapped]
        public bool IsEmbeddable => GetEmbedUrl() != null;

        [NotMapped]
        public string? EmbedUrl => GetEmbedUrl();

        private string? GetEmbedUrl()
        {
            if (string.IsNullOrEmpty(Url)) return null;

            try
            {
                var uri = new Uri(Url);
                var host = uri.Host.ToLowerInvariant();

                // YouTube
                if (host.Contains("youtube.com") || host.Contains("youtu.be"))
                {
                    var videoId = ExtractYouTubeId(uri);
                    return videoId != null
                        ? $"https://www.youtube-nocookie.com/embed/{videoId}"
                        : null;
                }

                // За Google Drive (pdf файл)
                if (host == "drive.google.com" && Url.Contains("/file/d/"))
                {
                    return Url.Replace("/view", "/preview").Replace("?usp=sharing", "");
                }

                // За Google Docs / Slides / Sheets
                if (host == "docs.google.com")
                {
                    if (Url.Contains("/presentation/")) return Url.Replace("/edit", "/embed");
                    if (Url.Contains("/document/") || Url.Contains("/spreadsheets/"))
                        return Url.Replace("/edit", "/preview");
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string? ExtractYouTubeId(Uri uri)
        {
            if (uri.Host.Contains("youtu.be"))
                return uri.AbsolutePath.Trim('/');

            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            return query["v"];
        }
    }
}
