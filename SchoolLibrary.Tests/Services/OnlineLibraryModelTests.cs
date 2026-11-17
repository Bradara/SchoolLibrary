using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Tests.Services;

public class OnlineLibraryModelTests
{
    [Theory]
    [InlineData("https://chitanka.info", "https://chitanka.info", "chitanka.info")]
    [InlineData("http://www.slovo.bg/path", "http://www.slovo.bg/path", "slovo.bg")]
    public void AnHttpAddress_IsALinkWithTheHostWithoutWww(string url, string safe, string host)
    {
        var library = new OnlineLibrary { Url = url };

        Assert.Equal(safe, library.SafeUrl);
        Assert.Equal(host, library.Host);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,x")]
    [InlineData("не е адрес")]
    [InlineData("")]
    public void ADangerousOrBrokenAddress_IsNeverALink(string url)
    {
        var library = new OnlineLibrary { Url = url };

        Assert.Null(library.SafeUrl);
        Assert.Null(library.Host);
    }
}
