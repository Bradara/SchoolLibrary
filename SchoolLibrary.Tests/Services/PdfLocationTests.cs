using SchoolLibrary.Services.Reading;

namespace SchoolLibrary.Tests.Services;

public class PdfLocationTests
{
    [Theory]
    [InlineData(1, "pdfpage(1)")]
    [InlineData(12, "pdfpage(12)")]
    [InlineData(PdfLocation.MaxPage, "pdfpage(100000)")]
    public void Format_And_PageOf_GoBothWays(int page, string location)
    {
        Assert.Equal(location, PdfLocation.Format(page));
        Assert.Equal(page, PdfLocation.PageOf(location));
        Assert.True(PdfLocation.IsValid(location));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pdfpage()")]
    [InlineData("pdfpage(0)")]
    [InlineData("pdfpage(007)")]
    [InlineData("pdfpage(100001)")]
    [InlineData("pdfpage(9999999)")]
    [InlineData("pdfpage(12) ")]
    [InlineData("PDFPAGE(12)")]
    [InlineData("pdfpage(１２)")]
    [InlineData("epubcfi(/6/4!/4/2/1:0)")]
    public void PageOf_RefusesAnythingElse(string? location)
    {
        Assert.Null(PdfLocation.PageOf(location));
        Assert.False(PdfLocation.IsValid(location));
    }
}
