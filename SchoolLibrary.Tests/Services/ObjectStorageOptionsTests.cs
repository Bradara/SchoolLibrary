using SchoolLibrary.Services.Storage;

namespace SchoolLibrary.Tests.Services;

public class ObjectStorageOptionsTests
{
    private static ObjectStorageOptions Valid() => new()
    {
        Endpoint = "https://ns.compat.objectstorage.eu-frankfurt-1.oraclecloud.com",
        Region = "eu-frankfurt-1",
        Bucket = "schoollibrary-uploads",
        AccessKey = "access",
        SecretKey = "secret",
    };

    [Fact]
    public void Validate_Passes_WhenEverythingIsSet()
    {
        var exception = Record.Exception(() => Valid().Validate());

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("Endpoint")]
    [InlineData("Region")]
    [InlineData("Bucket")]
    [InlineData("AccessKey")]
    [InlineData("SecretKey")]
    public void Validate_Throws_AndNamesTheMissingSetting(string missing)
    {
        var options = Valid();
        typeof(ObjectStorageOptions).GetProperty(missing)!.SetValue(options, " ");

        var exception = Assert.Throws<InvalidOperationException>(() => options.Validate());
        Assert.Contains(missing, exception.Message);
    }

    [Fact]
    public void Factory_Create_ValidatesTheOptions()
    {
        var options = Valid();
        options.Bucket = string.Empty;

        Assert.Throws<InvalidOperationException>(() => ObjectStorageClientFactory.Create(options));
    }

    [Fact]
    public void Factory_Create_BuildsAClient_WhenConfigured()
    {
        using var client = ObjectStorageClientFactory.Create(Valid());

        Assert.NotNull(client);
    }
}
