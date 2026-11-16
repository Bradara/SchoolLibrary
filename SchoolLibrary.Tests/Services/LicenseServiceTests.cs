using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Licenses;

namespace SchoolLibrary.Tests.Services;

public class LicenseServiceTests
{
    private static LicenseInput Input(string name = "CC BY 4.0", string? description = null, string? url = null) => new(name, description, url);

    [Fact]
    public async Task GetOptionsAsync_ReturnsLicensesInDefinedOrder()
    {
        using var context = TestDbContextFactory.Create();
        context.Licenses.Add(new License { Id = 1, Name = "Собствен материал" });
        context.Licenses.Add(new License { Id = 2, Name = "CC BY 4.0" });
        await context.SaveChangesAsync();
        var service = new LicenseService(context);

        var result = await service.GetOptionsAsync();

        Assert.Equal(new[] { "Собствен материал", "CC BY 4.0" }, result.Select(l => l.Name));
    }

    [Fact]
    public async Task GetOptionsAsync_WithNoLicenses_ReturnsEmptyList()
    {
        using var context = TestDbContextFactory.Create();
        var service = new LicenseService(context);

        Assert.Empty(await service.GetOptionsAsync());
    }

    [Fact]
    public async Task GetAllAsync_OrdersByName_AndCountsTheResourcesOfEach()
    {
        using var context = TestDbContextFactory.Create();
        var b = new License { Name = "B" };
        var a = new License { Name = "A" };
        context.Licenses.AddRange(b, a);
        await context.SaveChangesAsync();
        context.Resources.AddRange(
            new Resource { Title = "1", Author = "x", Url = "https://e.com/1", LicenseId = b.Id },
            new Resource { Title = "2", Author = "x", Url = "https://e.com/2", LicenseId = b.Id },
            new Resource { Title = "3", Author = "x", Url = "https://e.com/3" });
        await context.SaveChangesAsync();

        var result = await new LicenseService(context).GetAllAsync();

        Assert.Equal(new[] { "A", "B" }, result.Select(i => i.License.Name));
        Assert.Equal(new[] { 0, 2 }, result.Select(i => i.ResourceCount));
    }

    [Fact]
    public async Task GetByIdAsync_FindsTheLicense_OrReturnsNull()
    {
        using var context = TestDbContextFactory.Create();
        var license = new License { Name = "MIT" };
        context.Licenses.Add(license);
        await context.SaveChangesAsync();
        var service = new LicenseService(context);

        Assert.Equal("MIT", (await service.GetByIdAsync(license.Id))!.Name);
        Assert.Null(await service.GetByIdAsync(999));
    }

    [Fact]
    public async Task CreateAsync_SavesTheLicense_TrimmedAndWithEmptyOptionalFieldsAsNull()
    {
        using var context = TestDbContextFactory.Create();
        var service = new LicenseService(context);

        var result = await service.CreateAsync(Input("  CC BY 4.0  ", "   ", ""));

        Assert.True(result.Succeeded);
        var saved = context.Licenses.Single();
        Assert.Equal("CC BY 4.0", saved.Name);
        Assert.Null(saved.Description);
        Assert.Null(saved.Url);
    }

    [Fact]
    public async Task CreateAsync_KeepsTheDescriptionAndAddress()
    {
        using var context = TestDbContextFactory.Create();
        var service = new LicenseService(context);

        await service.CreateAsync(Input("MIT", " Свободен софтуер ", " https://opensource.org/license/mit "));

        var saved = context.Licenses.Single();
        Assert.Equal("Свободен софтуер", saved.Description);
        Assert.Equal("https://opensource.org/license/mit", saved.Url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_WithoutAName_IsInvalid(string name)
    {
        using var context = TestDbContextFactory.Create();

        var result = await new LicenseService(context).CreateAsync(Input(name));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(context.Licenses);
    }

    [Fact]
    public async Task CreateAsync_FieldsThatAreTooLong_AreInvalid()
    {
        using var context = TestDbContextFactory.Create();
        var service = new LicenseService(context);

        Assert.Equal(ServiceError.Invalid, (await service.CreateAsync(Input(new string('a', LicenseService.MaxNameLength + 1)))).Error);
        Assert.Equal(ServiceError.Invalid, (await service.CreateAsync(Input(description: new string('a', LicenseService.MaxDescriptionLength + 1)))).Error);
        Assert.Equal(ServiceError.Invalid, (await service.CreateAsync(Input(url: "https://e.com/" + new string('a', LicenseService.MaxUrlLength)))).Error);
        Assert.Empty(context.Licenses);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,x")]
    [InlineData("ftp://example.com/license")]
    [InlineData("example.com/license")]
    public async Task CreateAsync_AnAddressThatIsNotHttp_IsInvalid(string url)
    {
        using var context = TestDbContextFactory.Create();

        var result = await new LicenseService(context).CreateAsync(Input(url: url));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(context.Licenses);
    }

    [Fact]
    public async Task CreateAsync_ANameInAnotherCase_IsAConflict()
    {
        using var context = TestDbContextFactory.Create();
        context.Licenses.Add(new License { Name = "CC BY 4.0" });
        await context.SaveChangesAsync();

        var result = await new LicenseService(context).CreateAsync(Input("cc by 4.0"));

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Single(context.Licenses);
    }

    [Fact]
    public async Task UpdateAsync_ChangesAllFields()
    {
        using var context = TestDbContextFactory.Create();
        var license = new License { Name = "Стар", Description = "d", Url = "https://e.com" };
        context.Licenses.Add(license);
        await context.SaveChangesAsync();

        var result = await new LicenseService(context).UpdateAsync(license.Id, Input("Нов", null, null));

        Assert.True(result.Succeeded);
        var saved = context.Licenses.Single();
        Assert.Equal("Нов", saved.Name);
        Assert.Null(saved.Description);
        Assert.Null(saved.Url);
    }

    [Fact]
    public async Task UpdateAsync_KeepingTheOwnName_IsAllowed()
    {
        using var context = TestDbContextFactory.Create();
        var license = new License { Name = "MIT" };
        context.Licenses.Add(license);
        await context.SaveChangesAsync();

        var result = await new LicenseService(context).UpdateAsync(license.Id, Input("MIT", "ново описание"));

        Assert.True(result.Succeeded);
        Assert.Equal("ново описание", context.Licenses.Single().Description);
    }

    [Fact]
    public async Task UpdateAsync_UnknownLicense_IsNotFound()
    {
        using var context = TestDbContextFactory.Create();

        var result = await new LicenseService(context).UpdateAsync(5, Input());

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task UpdateAsync_InvalidValues_AreRejected_AndNothingChanges()
    {
        using var context = TestDbContextFactory.Create();
        var license = new License { Name = "MIT" };
        context.Licenses.Add(license);
        await context.SaveChangesAsync();

        var result = await new LicenseService(context).UpdateAsync(license.Id, Input("", url: "javascript:x"));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Equal("MIT", context.Licenses.Single().Name);
    }

    [Fact]
    public async Task UpdateAsync_ANameOfAnotherLicense_IsAConflict()
    {
        using var context = TestDbContextFactory.Create();
        var other = new License { Name = "GPL 2.0" };
        var license = new License { Name = "MIT" };
        context.Licenses.AddRange(other, license);
        await context.SaveChangesAsync();

        var result = await new LicenseService(context).UpdateAsync(license.Id, Input("gpl 2.0"));

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Equal("MIT", context.Licenses.Single(l => l.Id == license.Id).Name);
    }

    [Fact]
    public async Task DeleteAsync_RemovesAnUnusedLicense()
    {
        using var context = TestDbContextFactory.Create();
        var license = new License { Name = "MIT" };
        context.Licenses.Add(license);
        await context.SaveChangesAsync();

        var result = await new LicenseService(context).DeleteAsync(license.Id);

        Assert.True(result.Succeeded);
        Assert.Empty(context.Licenses);
    }

    [Fact]
    public async Task DeleteAsync_ALicenseInUse_IsAConflict_AndStays()
    {
        using var context = TestDbContextFactory.Create();
        var license = new License { Name = "MIT" };
        context.Licenses.Add(license);
        await context.SaveChangesAsync();
        context.Resources.Add(new Resource { Title = "1", Author = "x", Url = "https://e.com/1", LicenseId = license.Id });
        await context.SaveChangesAsync();

        var result = await new LicenseService(context).DeleteAsync(license.Id);

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Contains("1 ресурса", result.Message);
        Assert.Single(context.Licenses);
    }

    [Fact]
    public async Task DeleteAsync_UnknownLicense_IsNotFound()
    {
        using var context = TestDbContextFactory.Create();

        var result = await new LicenseService(context).DeleteAsync(5);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }
}
