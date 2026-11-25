using System.IO.Compression;
using System.Text;
using SchoolLibrary.Services.Reading;

namespace SchoolLibrary.Tests.Services;

public class EpubMetadataTests
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };

    private const string Container =
        """
        <?xml version="1.0"?>
        <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
          <rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles>
        </container>
        """;

    private static string Opf(string metadata, string manifest) =>
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://www.idpf.org/2007/opf" version="3.0">
          <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">{metadata}</metadata>
          <manifest>{manifest}</manifest>
        </package>
        """;

    private static byte[] Epub(params (string Path, byte[] Bytes)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, bytes) in entries)
            {
                using var stream = zip.CreateEntry(path).Open();
                stream.Write(bytes);
            }
        }

        return buffer.ToArray();
    }

    private static byte[] Text(string text) => Encoding.UTF8.GetBytes(text);

    private static byte[] Book(string opf, params (string Path, byte[] Bytes)[] files) =>
        Epub(new[] { ("META-INF/container.xml", Text(Container)), ("OEBPS/content.opf", Text(opf)) }.Concat(files).ToArray());

    [Fact]
    public void Read_Epub3_ReturnsTitleAuthorAndCoverImage()
    {
        var book = Book(
            Opf("<dc:title> Под  игото </dc:title><dc:creator>Иван Вазов</dc:creator>",
                """<item id="c" href="images/cover.png" media-type="image/png" properties="cover-image"/>"""),
            ("OEBPS/images/cover.png", Png));

        var info = EpubMetadata.Read(book);

        Assert.Equal("Под игото", info.Title);
        Assert.Equal("Иван Вазов", info.Author);
        Assert.NotNull(info.Cover);
        Assert.Equal("image/png", info.Cover!.ContentType);
        Assert.Equal(".png", info.Cover.Extension);
        Assert.Equal(Png, info.Cover.Bytes);
    }

    [Fact]
    public void Read_Epub2_FindsCoverThroughMetaElement()
    {
        var book = Book(
            Opf("""<dc:title>Бай Ганьо</dc:title><meta name="cover" content="img1"/>""",
                """<item id="img1" href="../Images/front.jpg" media-type="image/jpeg"/>"""),
            ("Images/front.jpg", Jpeg));

        var info = EpubMetadata.Read(book);

        Assert.Equal("image/jpeg", info.Cover?.ContentType);
    }

    [Fact]
    public void Read_WithoutCoverMarks_UsesImageNamedCover()
    {
        var book = Book(
            Opf("<dc:title>Книга</dc:title>",
                """<item id="x" href="pic.png" media-type="image/png"/><item id="y" href="Cover%20Page.png" media-type="image/png"/>"""),
            ("OEBPS/pic.png", Jpeg), ("OEBPS/Cover Page.png", Png));

        Assert.Equal(Png, EpubMetadata.Read(book).Cover?.Bytes);
    }

    [Fact]
    public void Read_SeveralCreators_JoinsThem()
    {
        var book = Book(Opf("<dc:creator>А. Б.</dc:creator><dc:creator>В. Г.</dc:creator><dc:creator>А. Б.</dc:creator>", ""));

        Assert.Equal("А. Б., В. Г.", EpubMetadata.Read(book).Author);
    }

    [Fact]
    public void Read_LongTitle_IsCutToOneHundredCharacters()
    {
        var book = Book(Opf($"<dc:title>{new string('а', 300)}</dc:title>", ""));

        var title = EpubMetadata.Read(book).Title!;

        Assert.Equal(100, title.Length);
        Assert.EndsWith("…", title);
    }

    [Fact]
    public void Read_SvgCover_IsLeftOut()
    {
        var book = Book(
            Opf("<dc:title>Книга</dc:title>", """<item id="c" href="c.svg" media-type="image/png" properties="cover-image"/>"""),
            ("OEBPS/c.svg", Text("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>")));

        var info = EpubMetadata.Read(book);

        Assert.Null(info.Cover);
        Assert.Equal("Книга", info.Title);
    }

    [Fact]
    public void Read_CoverTooLarge_IsLeftOut()
    {
        var big = new byte[EpubMetadata.MaxCoverBytes + 1];
        Png.CopyTo(big, 0);
        var book = Book(
            Opf("", """<item id="c" href="c.png" media-type="image/png" properties="cover-image"/>"""),
            ("OEBPS/c.png", big));

        Assert.Null(EpubMetadata.Read(book).Cover);
    }

    [Fact]
    public void Read_CoverOutsideTheArchive_IsLeftOut()
    {
        var book = Book(
            Opf("", """<item id="c" href="../../etc/c.png" media-type="image/png" properties="cover-image"/>"""),
            ("c.png", Png));

        Assert.Null(EpubMetadata.Read(book).Cover);
    }

    [Fact]
    public void Read_MissingCoverFile_GivesNoCover()
    {
        var book = Book(Opf("<dc:title>T</dc:title>", """<item id="c" href="none.png" media-type="image/png" properties="cover-image"/>"""));

        var info = EpubMetadata.Read(book);

        Assert.Null(info.Cover);
        Assert.Equal("T", info.Title);
    }

    [Fact]
    public void Read_DocumentWithDtd_IsRejected()
    {
        var opf = """
                  <?xml version="1.0"?>
                  <!DOCTYPE package [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
                  <package xmlns="http://www.idpf.org/2007/opf"><metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title>&xxe;</dc:title></metadata></package>
                  """;

        Assert.Equal(EpubInfo.Empty, EpubMetadata.Read(Book(opf)));
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3 })]
    [InlineData(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00 })]
    public void Read_NotAnEpub_ReturnsEmpty(byte[] bytes)
    {
        Assert.Equal(EpubInfo.Empty, EpubMetadata.Read(bytes));
    }

    [Fact]
    public void Read_WithoutContainer_ReturnsEmpty()
    {
        Assert.Equal(EpubInfo.Empty, EpubMetadata.Read(Epub(("mimetype", Text("application/epub+zip")))));
    }

    [Theory]
    [InlineData("OEBPS/content.opf", "images/c.png", "OEBPS/images/c.png")]
    [InlineData("OEBPS/content.opf", "../Images/c.png", "Images/c.png")]
    [InlineData("content.opf", "./c%20d.png", "c d.png")]
    [InlineData("OEBPS/content.opf", "c.png#frag", "OEBPS/c.png")]
    [InlineData("OEBPS/content.opf", "../../c.png", null)]
    [InlineData("OEBPS/content.opf", "/c.png", null)]
    [InlineData("OEBPS/content.opf", "http://example.com/c.png", null)]
    public void Resolve_LinksRelativeToThePackage(string opfPath, string href, string? expected)
    {
        Assert.Equal(expected, EpubMetadata.Resolve(opfPath, href));
    }
}
