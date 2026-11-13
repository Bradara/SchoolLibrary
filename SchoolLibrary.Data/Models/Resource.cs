using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using SchoolLibrary.Data.Enums;

namespace SchoolLibrary.Data.Models
{
    public class Resource
    {
        public int Id { get; set; }

        [Required(ErrorMessage = "Заглавието е задължително")]
        [MaxLength(100, ErrorMessage = "Заглавието не може да бъде по-дълго от 100 символа")]
        [Display(Name = "Заглавие")]
        public string Title { get; set; } = string.Empty;

        [MaxLength(1000, ErrorMessage = "Описанието не може да бъде по-дълго от 1000 символа")]
        [Display(Name = "Описание")]
        public string Description { get; set; } = string.Empty;

        [Required(ErrorMessage = "Уеб адресът е задължителен")]
        [MaxLength(500, ErrorMessage = "Уеб адресът не може да бъде по-дълъг от 500 символа")]
        [Display(Name = "Уеб адрес")]
        public string Url { get; set; } = string.Empty;

        public DateTime CreatedOn { get; set; }

        [Display(Name = "Вид")]
        public ResourceKind Kind { get; set; } = ResourceKind.Lesson;

        // Many-to-many: a resource can belong to several subjects and several grades.
        // EF Core creates the join tables (CategoryResource, GradeLevelResource) by convention.
        [Display(Name = "Предмети")]
        public ICollection<Category> Categories { get; set; } = new List<Category>();

        [Display(Name = "Класове")]
        public ICollection<GradeLevel> GradeLevels { get; set; } = new List<GradeLevel>();

        // Null when the author deleted their account: the resource stays in the catalog
        public string? OwnerId { get; set; }
        public ApplicationUser? Owner { get; set; }

        // Null only for resources added before licenses existed; new resources must have one
        [Display(Name = "Лиценз")]
        public int? LicenseId { get; set; }
        public License? License { get; set; }

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

                // Опитва да вгради и статични страници и общо достъпни pdf (pdf, html, htm)
                if (Url.EndsWith(".html") || Url.EndsWith(".htm") || Url.EndsWith(".pdf"))
                {
                    return Url;
                }

                //За Github Pages
                if (host.EndsWith("github.io"))
                {
                    return Url;
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

        [Required(ErrorMessage = "Посочете автора на материала.")]
        [MaxLength(100)]
        [Display(Name = "Автор")]
        public string Author { get; set; } = string.Empty;

        [MaxLength(100)]
        [Display(Name = "Източник")]
        public string? Source { get; set; }
    }
}
