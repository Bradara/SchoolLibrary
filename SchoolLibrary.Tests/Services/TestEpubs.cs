using System.IO.Compression;
using System.Text;

namespace SchoolLibrary.Tests.Services;

/// <summary>Small EPUB books built in memory for tests.</summary>
internal static class TestEpubs
{
    public static readonly byte[] JpegCover = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };

    /// <summary>A book with a title and, optionally, a JPEG cover marked as the cover image.</summary>
    public static byte[] Book(byte[]? cover)
    {
        var manifest = cover == null ? "" : """<item id="c" href="c.jpg" media-type="image/jpeg" properties="cover-image"/>""";
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, byte[] bytes)
            {
                using var stream = zip.CreateEntry(path).Open();
                stream.Write(bytes);
            }

            Add("META-INF/container.xml", Encoding.UTF8.GetBytes(
                """<container xmlns="urn:oasis:names:tc:opendocument:xmlns:container"><rootfiles><rootfile full-path="content.opf"/></rootfiles></container>"""));
            Add("content.opf", Encoding.UTF8.GetBytes(
                $"""<package xmlns="http://www.idpf.org/2007/opf"><metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title>Книга</dc:title></metadata><manifest>{manifest}</manifest></package>"""));
            if (cover != null)
            {
                Add("c.jpg", cover);
            }
        }

        return buffer.ToArray();
    }
}
