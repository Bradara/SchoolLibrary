using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SchoolLibrary.Areas.Admin.Controllers;
using SchoolLibrary.Areas.Admin.Models;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Categories;
using SchoolLibrary.Services.GradeLevels;
using SchoolLibrary.Services.Licenses;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Tests.Web;

public class ResourceImportControllerTests
{
    private readonly Mock<IResourceImportService> _import = new();
    private readonly Mock<ICategoryService> _categories = new();
    private readonly Mock<ILicenseService> _licenses = new();
    private readonly Mock<IGradeLevelService> _gradeLevels = new();

    public ResourceImportControllerTests()
    {
        _categories.Setup(c => c.GetOptionsAsync()).ReturnsAsync(new List<Category> { new() { Name = "Информатика" }, new() { Name = "Музика" } });
        _licenses.Setup(l => l.GetOptionsAsync()).ReturnsAsync(new List<License> { new() { Name = "Собствен материал" }, new() { Name = "CC BY 4.0" } });
        _gradeLevels.Setup(g => g.GetAllAsync()).ReturnsAsync(new List<GradeLevel> { new() { Number = 5 }, new() { Number = 6 } });
    }

    private ResourceImportController Controller() =>
        new ResourceImportController(_import.Object, _categories.Object, _licenses.Object, _gradeLevels.Object)
            .WithUser("admin", Roles.Administrator);

    private static IFormFile CsvFile(string name, byte[]? bytes = null, long? declaredLength = null)
    {
        bytes ??= Encoding.UTF8.GetBytes("title;author\nA;B");
        return new FormFile(new MemoryStream(bytes), 0, declaredLength ?? bytes.Length, "file", name);
    }

    // ---------- the upload page ----------

    [Fact]
    public async Task Index_ListsTheNamesThatCanBeUsedInTheFile()
    {
        var result = await Controller().Index();

        var model = Assert.IsType<ResourceImportIndexViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(new[] { "Собствен материал", "CC BY 4.0" }, model.Licenses);
        Assert.Equal(new[] { "Информатика", "Музика" }, model.Subjects);
        Assert.Equal(new[] { "5", "6" }, model.Grades);
    }

    // ---------- the file is refused before it is read ----------

    [Fact]
    public async Task Preview_NoFile_GoesBackWithAMessage()
    {
        var controller = Controller();

        var result = await controller.Preview(null);

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal("Изберете CSV файл.", controller.TempDataText("ErrorMessage"));
        _import.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Preview_EmptyFile_GoesBackWithAMessage()
    {
        var controller = Controller();

        var result = await controller.Preview(CsvFile("data.csv", Array.Empty<byte>()));

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Изберете CSV файл.", controller.TempDataText("ErrorMessage"));
    }

    [Theory]
    [InlineData("data.txt")]
    [InlineData("data.xlsx")]
    [InlineData("data.csv.exe")]
    [InlineData("data")]
    [InlineData("data.html")]
    public async Task Preview_NotACsvFile_IsRefused(string name)
    {
        var controller = Controller();

        var result = await controller.Preview(CsvFile(name));

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Contains("CSV", controller.TempDataText("ErrorMessage"));
        _import.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("data.csv")]
    [InlineData("DATA.CSV")]
    [InlineData("моите ресурси.Csv")]
    public async Task Preview_TheExtensionIsCheckedIgnoringCase(string name)
    {
        _import.Setup(i => i.PreviewAsync(It.IsAny<string>())).ReturnsAsync(new ResourceImportPreview());

        var result = await Controller().Preview(CsvFile(name));

        Assert.IsType<ViewResult>(result);
    }

    [Fact]
    public async Task Preview_ATooLargeFile_IsRefusedWithoutReadingIt()
    {
        var controller = Controller();

        var result = await controller.Preview(CsvFile("big.csv", declaredLength: 3 * 1024 * 1024));

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Файлът е твърде голям.", controller.TempDataText("ErrorMessage"));
        _import.VerifyNoOtherCalls();
    }

    // ---------- the preview ----------

    [Fact]
    public async Task Preview_ShowsTheAnalysisAndKeepsTheTextForTheConfirmButton()
    {
        var preview = new ResourceImportPreview { Rows = new List<ResourceImportRow> { new() { LineNumber = 2, Title = "А" } } };
        _import.Setup(i => i.PreviewAsync("title;author\nА;Б")).ReturnsAsync(preview);

        var result = await Controller().Preview(CsvFile("моят файл.csv", Encoding.UTF8.GetBytes("title;author\nА;Б")));

        var model = Assert.IsType<ResourceImportViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Same(preview, model.Preview);
        Assert.Equal("title;author\nА;Б", model.CsvText);
        Assert.Equal("моят файл.csv", model.FileName);
    }

    [Fact]
    public async Task Preview_AFileSavedByExcelInTheOldBulgarianEncoding_IsReadAsCyrillic()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(1251).GetBytes("title;author\nДроби;Автор");
        string? received = null;
        _import.Setup(i => i.PreviewAsync(It.IsAny<string>()))
            .Callback<string>(t => received = t)
            .ReturnsAsync(new ResourceImportPreview());

        await Controller().Preview(CsvFile("excel.csv", bytes));

        Assert.Equal("title;author\nДроби;Автор", received);
    }

    [Fact]
    public async Task Preview_OnlyTheNameOfTheFileIsShown_NotAPathFromTheClient()
    {
        _import.Setup(i => i.PreviewAsync(It.IsAny<string>())).ReturnsAsync(new ResourceImportPreview());

        var result = await Controller().Preview(CsvFile("C:/Users/someone/secret/data.csv"));

        var model = Assert.IsType<ResourceImportViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal("data.csv", model.FileName);
    }

    // ---------- the import ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Import_WithoutData_GoesBackToTheUpload(string? text)
    {
        var controller = Controller();

        var result = await controller.Import(text);

        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.False(string.IsNullOrEmpty(controller.TempDataText("ErrorMessage")));
        _import.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Import_ImportsThePostedTextAndShowsTheResult()
    {
        var outcome = new ResourceImportPreview();
        _import.Setup(i => i.ImportAsync("title;author\nА;Б")).ReturnsAsync(outcome);

        var result = await Controller().Import("title;author\nА;Б");

        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Result", view.ViewName);
        Assert.Same(outcome, Assert.IsType<ResourceImportViewModel>(view.Model).Preview);
    }

    // ---------- the template ----------

    [Fact]
    public async Task Template_IsACsvFileBuiltFromTheRealNames()
    {
        var result = await Controller().Template();

        var file = Assert.IsType<FileContentResult>(result);
        Assert.StartsWith("text/csv", file.ContentType);
        Assert.Equal(ResourceImportCsv.TemplateFileName, file.FileDownloadName);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, file.FileContents.Take(3));
        var text = Encoding.UTF8.GetString(file.FileContents);
        Assert.Contains(";Собствен материал;Информатика;", text);
    }
}
