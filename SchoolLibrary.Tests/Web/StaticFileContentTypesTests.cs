using SchoolLibrary.Extensions;

namespace SchoolLibrary.Tests.Web;

public class StaticFileContentTypesTests
{
    [Theory]
    [InlineData("lib/pdfjs/pdf.min.mjs", "text/javascript")]
    [InlineData("lib/pdfjs/wasm/openjpeg.wasm", "application/wasm")]
    [InlineData("lib/pdfjs/cmaps/78-H.bcmap", "application/octet-stream")]
    [InlineData("lib/pdfjs/standard_fonts/FoxitSerif.pfb", "application/octet-stream")]
    [InlineData("lib/pdfjs/iccs/CGATS001Compat-v2-micro.icc", "application/vnd.iccprofile")]
    public void PdfReaderFiles_HaveAType(string path, string expected)
    {
        Assert.True(StaticFileContentTypes.Create().TryGetContentType(path, out var type));
        Assert.Equal(expected, type);
    }

    [Theory]
    [InlineData("css/site.css", "text/css")]
    [InlineData("js/site.js", "text/javascript")]
    [InlineData("lib/pdfjs/standard_fonts/LiberationSans-Regular.ttf", "application/x-font-ttf")]
    public void KnownTypes_StayTheSame(string path, string expected)
    {
        Assert.True(StaticFileContentTypes.Create().TryGetContentType(path, out var type));
        Assert.Equal(expected, type);
    }
}
