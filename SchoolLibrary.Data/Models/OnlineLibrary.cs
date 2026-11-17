using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolLibrary.Data.Models
{
    /// <summary>An external online library listed in the catalog: a card with an icon, a short description and a link.</summary>
    public class OnlineLibrary
    {
        public const string DefaultIcon = "bi-book";

        public int Id { get; set; }

        [Required(ErrorMessage = "Името на библиотеката е задължително")]
        [MaxLength(100, ErrorMessage = "Името не може да бъде по-дълго от 100 символа")]
        [Display(Name = "Име")]
        public string Name { get; set; } = string.Empty;

        [MaxLength(300, ErrorMessage = "Описанието не може да бъде по-дълго от 300 символа")]
        [Display(Name = "Кратко описание")]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Уеб адресът е задължителен")]
        [MaxLength(500, ErrorMessage = "Уеб адресът не може да бъде по-дълъг от 500 символа")]
        [Display(Name = "Уеб адрес")]
        public string Url { get; set; } = string.Empty;

        /// <summary>Bootstrap Icons class shown on the card (one of a fixed list).</summary>
        [MaxLength(40)]
        public string Icon { get; set; } = DefaultIcon;

        /// <summary>Cards are shown by this number first, then by name.</summary>
        public int SortOrder { get; set; }

        /// <summary>The address for a link (href), or null when it is not an http(s) address.</summary>
        [NotMapped]
        public string? SafeUrl =>
            Uri.TryCreate(Url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? Url
                : null;

        /// <summary>The site name without "www.", shown under the title.</summary>
        [NotMapped]
        public string? Host =>
            Uri.TryCreate(SafeUrl, UriKind.Absolute, out var uri)
                ? (uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host)
                : null;
    }
}
