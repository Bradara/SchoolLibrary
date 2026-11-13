using System.Text;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Tests.Services;

public class ResourceImportCsvTests
{
    [Fact]
    public void Decode_Utf8_ReturnsText()
    {
        var bytes = Encoding.UTF8.GetBytes("Математика;5");

        Assert.Equal("Математика;5", ResourceImportCsv.Decode(bytes));
    }

    [Fact]
    public void Decode_Utf8WithByteOrderMark_KeepsTheTextReadable()
    {
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("Математика")).ToArray();

        Assert.EndsWith("Математика", ResourceImportCsv.Decode(bytes));
    }

    [Fact]
    public void Decode_Windows1251_IsReadAsCyrillic()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(1251).GetBytes("Български език;5");

        Assert.Equal("Български език;5", ResourceImportCsv.Decode(bytes));
    }

    [Fact]
    public void Decode_EmptyFile_ReturnsEmptyText()
    {
        Assert.Equal(string.Empty, ResourceImportCsv.Decode(Array.Empty<byte>()));
    }

    [Fact]
    public void BuildTemplate_UsesTheFirstLicenseAndSubject()
    {
        var text = ResourceImportCsv.BuildTemplate(new[] { "Собствен материал", "CC BY 4.0" }, new[] { "Информатика", "Музика" });

        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("title;author;url;license;subjects;grades;source;description", lines[0]);
        Assert.Contains(";Собствен материал;Информатика;", lines[1]);
    }

    [Fact]
    public void BuildTemplate_WithoutNames_FallsBackToPlaceholders()
    {
        var text = ResourceImportCsv.BuildTemplate(Array.Empty<string>(), Array.Empty<string>());

        Assert.Contains(";CC BY 4.0;Математика;", text);
    }

    [Fact]
    public void BuildTemplateFile_StartsWithByteOrderMark()
    {
        var bytes = ResourceImportCsv.BuildTemplateFile(new[] { "CC BY 4.0" }, new[] { "Математика" });

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
    }

    [Fact]
    public async Task Template_PassesTheImportChecks()
    {
        using var context = TestDbContextFactory.Create();
        var license = new License { Name = "Собствен материал" };
        var subject = new Category { Name = "Информатика" };
        context.Licenses.Add(license);
        context.Categories.Add(subject);
        context.GradeLevels.AddRange(new GradeLevel { Number = 5 }, new GradeLevel { Number = 6 });
        await context.SaveChangesAsync();
        var import = new ResourceImportService(context, new ResourceService(context));

        var template = ResourceImportCsv.BuildTemplate(new[] { license.Name }, new[] { subject.Name });
        var preview = await import.PreviewAsync(template);

        Assert.False(preview.HasFileError);
        Assert.Equal(1, preview.NewCount);
    }

    [Fact]
    public async Task Template_RoundTripsThroughDecodeOfTheDownloadedFile()
    {
        using var context = TestDbContextFactory.Create();
        context.Licenses.Add(new License { Name = "CC BY 4.0" });
        context.Categories.Add(new Category { Name = "Математика" });
        context.GradeLevels.AddRange(new GradeLevel { Number = 5 }, new GradeLevel { Number = 6 });
        await context.SaveChangesAsync();
        var import = new ResourceImportService(context, new ResourceService(context));

        var file = ResourceImportCsv.BuildTemplateFile(new[] { "CC BY 4.0" }, new[] { "Математика" });
        var preview = await import.PreviewAsync(ResourceImportCsv.Decode(file));

        Assert.Equal(1, preview.NewCount);
    }
}
