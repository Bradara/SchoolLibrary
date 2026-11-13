using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Tests.Services;

public class ResourceImportServiceTests
{
    private const string Header = "title;author;url;license;subjects;grades;source;description";

    private readonly ApplicationDbContext _context = TestDbContextFactory.Create();
    private readonly ResourceImportService _service;
    private readonly ResourceService _resources;

    private readonly Category _math;
    private readonly Category _informatics;
    private readonly License _ccBy;

    public ResourceImportServiceTests()
    {
        _resources = new ResourceService(_context);
        _service = new ResourceImportService(_context, _resources);

        _math = new Category { Name = "Математика" };
        _informatics = new Category { Name = "Информатика" };
        _ccBy = new License { Name = "CC BY 4.0" };
        _context.Categories.AddRange(_math, _informatics);
        _context.Licenses.AddRange(_ccBy, new License { Name = "Собствен материал" });
        _context.GradeLevels.AddRange(new GradeLevel { Number = 5 }, new GradeLevel { Number = 6 }, new GradeLevel { Number = 7 });
        _context.SaveChanges();
    }

    private static string Csv(params string[] rows) => Header + "\n" + string.Join("\n", rows);

    private static string Row(string title = "Дроби", string url = "https://example.com/drobi", string license = "CC BY 4.0",
        string subjects = "Математика", string grades = "5", string author = "Автор") =>
        $"{title};{author};{url};{license};{subjects};{grades};Източник;Описание";

    // ---------- the file as a whole ----------

    [Theory]
    [InlineData("")]
    [InlineData("   \n  ")]
    public async Task PreviewAsync_EmptyText_ReturnsFileError(string text)
    {
        var preview = await _service.PreviewAsync(text);

        Assert.True(preview.HasFileError);
        Assert.Contains("празен", preview.FileError);
    }

    [Fact]
    public async Task PreviewAsync_OnlyHeader_ReturnsFileError()
    {
        var preview = await _service.PreviewAsync(Header);

        Assert.Contains("няма редове", preview.FileError);
    }

    [Fact]
    public async Task PreviewAsync_MissingColumns_NamesThem()
    {
        var preview = await _service.PreviewAsync("title;author;url\nА;Б;https://example.com");

        Assert.Contains("license", preview.FileError);
        Assert.Contains("subjects", preview.FileError);
        Assert.Contains("grades", preview.FileError);
        Assert.DoesNotContain("title,", preview.FileError);
    }

    [Fact]
    public async Task PreviewAsync_CommaSeparatedFile_ExplainsTheSeparator()
    {
        var preview = await _service.PreviewAsync("title,author,url,license,subjects,grades\nА,Б,https://example.com,CC BY 4.0,Математика,5");

        Assert.True(preview.HasFileError);
        Assert.Contains("(;)", preview.FileError);
    }

    [Fact]
    public async Task PreviewAsync_TooManyRows_ReturnsFileError()
    {
        var rows = Enumerable.Range(1, IResourceImportService.MaxRows + 1)
            .Select(i => Row(url: $"https://example.com/{i}"))
            .ToArray();

        var preview = await _service.PreviewAsync(Csv(rows));

        Assert.Contains("повече от", preview.FileError);
    }

    [Fact]
    public async Task PreviewAsync_ExactlyMaxRows_IsAccepted()
    {
        var rows = Enumerable.Range(1, IResourceImportService.MaxRows)
            .Select(i => Row(url: $"https://example.com/{i}"))
            .ToArray();

        var preview = await _service.PreviewAsync(Csv(rows));

        Assert.False(preview.HasFileError);
        Assert.Equal(IResourceImportService.MaxRows, preview.NewCount);
    }

    [Fact]
    public async Task PreviewAsync_TooLargeText_ReturnsFileError()
    {
        var preview = await _service.PreviewAsync(Header + "\n" + new string('а', IResourceImportService.MaxFileLength));

        Assert.Contains("голям", preview.FileError);
    }

    [Fact]
    public async Task PreviewAsync_ByteOrderMarkFromExcel_IsIgnored()
    {
        var preview = await _service.PreviewAsync("﻿" + Csv(Row()));

        Assert.False(preview.HasFileError);
        Assert.Equal(1, preview.NewCount);
    }

    [Fact]
    public async Task PreviewAsync_HeaderNamesIgnoreCaseAndSpaces_AndColumnOrder()
    {
        var text = " Grades ;URL; Title;Author;License;Subjects\n5;https://example.com/a;Дроби;Автор;CC BY 4.0;Математика";

        var preview = await _service.PreviewAsync(text);

        Assert.False(preview.HasFileError);
        Assert.Equal(1, preview.NewCount);
    }

    [Fact]
    public async Task PreviewAsync_OptionalColumnsMayBeMissing()
    {
        var text = "title;author;url;license;subjects;grades\nДроби;Автор;https://example.com/a;CC BY 4.0;Математика;5";

        var preview = await _service.PreviewAsync(text);

        Assert.Equal(1, preview.NewCount);
    }

    [Fact]
    public async Task PreviewAsync_QuotedFieldWithSeparatorAndLineBreak_IsOneValue()
    {
        var text = Csv("\"Дроби; първа част\";Автор;https://example.com/a;CC BY 4.0;Математика;5;Източник;\"Ред 1\nред 2\"");

        var preview = await _service.PreviewAsync(text);
        await _service.ImportAsync(text);

        Assert.Equal(1, preview.NewCount);
        var saved = _context.Resources.Single();
        Assert.Equal("Дроби; първа част", saved.Title);
        Assert.Equal("Ред 1\nред 2", saved.Description.Replace("\r", ""));
    }

    [Fact]
    public async Task PreviewAsync_BlankLinesBetweenRows_AreSkipped()
    {
        var text = Header + "\n\n" + Row(url: "https://example.com/a") + "\n\n" + Row(url: "https://example.com/b") + "\n";

        var preview = await _service.PreviewAsync(text);

        Assert.Equal(2, preview.Rows.Count);
    }

    // ---------- rows ----------

    [Fact]
    public async Task PreviewAsync_ValidRow_IsNewAndCarriesTheInput()
    {
        var preview = await _service.PreviewAsync(Csv(Row(subjects: "Математика|Информатика", grades: "5|6")));

        var row = Assert.Single(preview.Rows);
        Assert.Equal(ResourceImportRowStatus.New, row.Status);
        Assert.Null(row.Message);
        Assert.Equal(ResourceKind.External, row.Input!.Kind);
        Assert.Equal(_ccBy.Id, row.Input.LicenseId);
        Assert.Equal(new[] { _math.Id, _informatics.Id }, row.Input.CategoryIds);
        Assert.Equal(2, row.Input.GradeLevelIds.Count);
    }

    [Fact]
    public async Task PreviewAsync_ReportsTheLineOfEveryRow()
    {
        var preview = await _service.PreviewAsync(Csv(Row(url: "https://example.com/a"), Row(url: "https://example.com/b")));

        Assert.Equal(new[] { 2, 3 }, preview.Rows.Select(r => r.LineNumber));
    }

    [Fact]
    public async Task PreviewAsync_NamesAreMatchedIgnoringCaseAndSpaces()
    {
        var preview = await _service.PreviewAsync(Csv(Row(license: " cc by 4.0 ", subjects: " математика | ИНФОРМАТИКА ")));

        Assert.Equal(1, preview.NewCount);
    }

    [Theory]
    [InlineData("5 клас")]
    [InlineData("5.")]
    public async Task PreviewAsync_GradeWithTextAfterTheNumber_IsAccepted(string grade)
    {
        var preview = await _service.PreviewAsync(Csv(Row(grades: grade)));

        Assert.Equal(1, preview.NewCount);
    }

    [Fact]
    public async Task PreviewAsync_RepeatedSubjectInOneCell_IsCountedOnce()
    {
        var preview = await _service.PreviewAsync(Csv(Row(subjects: "Математика|математика")));

        Assert.Single(preview.Rows[0].Input!.CategoryIds);
    }

    [Theory]
    [InlineData("Неизвестен лиценз", "Математика", "5", "Непознат лиценз")]
    [InlineData("CC BY 4.0", "Астрология", "5", "Непознат предмет: Астрология")]
    [InlineData("CC BY 4.0", "Математика", "13", "Непознат клас: 13")]
    [InlineData("CC BY 4.0", "Математика", "пети", "Непознат клас: пети")]
    [InlineData("", "Математика", "5", "Изберете лиценз")]
    [InlineData("CC BY 4.0", "", "5", "предмет")]
    [InlineData("CC BY 4.0", "Математика", "", "клас")]
    public async Task PreviewAsync_RowWithUnknownOrMissingReference_IsInvalid(string license, string subjects, string grades, string expectedMessage)
    {
        var preview = await _service.PreviewAsync(Csv(Row(license: license, subjects: subjects, grades: grades)));

        var row = Assert.Single(preview.Rows);
        Assert.Equal(ResourceImportRowStatus.Invalid, row.Status);
        Assert.Contains(expectedMessage, row.Message);
        Assert.Null(row.Input);
    }

    [Fact]
    public async Task PreviewAsync_RowWithSeveralUnknownNames_ReportsAllOfThem()
    {
        var preview = await _service.PreviewAsync(Csv(Row(subjects: "Астрология|Магия")));

        Assert.Contains("Астрология", preview.Rows[0].Message);
        Assert.Contains("Магия", preview.Rows[0].Message);
    }

    [Theory]
    [InlineData("", "Автор", "https://example.com/a", "Заглавието е задължително")]
    [InlineData("Дроби", "", "https://example.com/a", "автора")]
    [InlineData("Дроби", "Автор", "", "Уеб адресът е задължителен")]
    [InlineData("Дроби", "Автор", "javascript:alert(1)", "http://")]
    [InlineData("Дроби", "Автор", "ftp://example.com/a", "http://")]
    [InlineData("Дроби", "Автор", "не е линк", "http://")]
    public async Task PreviewAsync_RowBreakingFormRules_IsInvalid(string title, string author, string url, string expectedMessage)
    {
        var preview = await _service.PreviewAsync(Csv(Row(title: title, author: author, url: url)));

        var row = Assert.Single(preview.Rows);
        Assert.Equal(ResourceImportRowStatus.Invalid, row.Status);
        Assert.Contains(expectedMessage, row.Message);
    }

    [Fact]
    public async Task PreviewAsync_TooLongTitle_IsInvalid()
    {
        var preview = await _service.PreviewAsync(Csv(Row(title: new string('а', ResourceService.TitleMaxLength + 1))));

        Assert.Equal(ResourceImportRowStatus.Invalid, preview.Rows[0].Status);
    }

    [Fact]
    public async Task PreviewAsync_ShortRow_IsInvalidInsteadOfFailingTheWholeFile()
    {
        var preview = await _service.PreviewAsync(Csv("Дроби;Автор", Row(url: "https://example.com/b")));

        Assert.False(preview.HasFileError);
        Assert.Equal(ResourceImportRowStatus.Invalid, preview.Rows[0].Status);
        Assert.Equal(ResourceImportRowStatus.New, preview.Rows[1].Status);
    }

    // ---------- duplicates ----------

    [Fact]
    public async Task PreviewAsync_LinkAlreadyInLibrary_IsDuplicateWithLinkToIt()
    {
        var existing = (await _resources.CreateAsync(new ResourceInput
        {
            Title = "Стар ресурс",
            Author = "А",
            Url = "https://example.com/drobi",
            LicenseId = _ccBy.Id,
            CategoryIds = { _math.Id },
            GradeLevelIds = { _context.GradeLevels.First().Id }
        }, null)).Value!;

        var preview = await _service.PreviewAsync(Csv(Row(url: "http://www.example.com/drobi/?utm_source=x")));

        var row = Assert.Single(preview.Rows);
        Assert.Equal(ResourceImportRowStatus.Duplicate, row.Status);
        Assert.Equal(existing.Id, row.DuplicateOfId);
        Assert.Contains("Стар ресурс", row.Message);
        Assert.Null(row.Input);
    }

    [Fact]
    public async Task PreviewAsync_SameLinkTwiceInFile_SecondRowIsDuplicateOfTheFirst()
    {
        var preview = await _service.PreviewAsync(Csv(
            Row(title: "Първи", url: "https://youtu.be/dQw4w9WgXcQ"),
            Row(title: "Втори", url: "https://www.youtube.com/watch?v=dQw4w9WgXcQ")));

        Assert.Equal(ResourceImportRowStatus.New, preview.Rows[0].Status);
        Assert.Equal(ResourceImportRowStatus.Duplicate, preview.Rows[1].Status);
        Assert.Contains("ред 2", preview.Rows[1].Message);
        Assert.Null(preview.Rows[1].DuplicateOfId);
    }

    [Fact]
    public async Task PreviewAsync_InvalidRowDoesNotMakeLaterRowWithSameLinkADuplicate()
    {
        var preview = await _service.PreviewAsync(Csv(
            Row(title: "", url: "https://example.com/a"),
            Row(title: "Добър", url: "https://example.com/a")));

        Assert.Equal(ResourceImportRowStatus.Invalid, preview.Rows[0].Status);
        Assert.Equal(ResourceImportRowStatus.New, preview.Rows[1].Status);
    }

    [Fact]
    public async Task PreviewAsync_CountsRowsByStatus()
    {
        await _resources.CreateAsync(new ResourceInput
        {
            Title = "Стар",
            Author = "А",
            Url = "https://example.com/old",
            LicenseId = _ccBy.Id,
            CategoryIds = { _math.Id },
            GradeLevelIds = { _context.GradeLevels.First().Id }
        }, null);

        var preview = await _service.PreviewAsync(Csv(
            Row(url: "https://example.com/new1"),
            Row(url: "https://example.com/new2"),
            Row(url: "https://example.com/old"),
            Row(title: "")));

        Assert.Equal(2, preview.NewCount);
        Assert.Equal(1, preview.DuplicateCount);
        Assert.Equal(1, preview.InvalidCount);
        Assert.Equal(0, preview.ImportedCount);
    }

    // ---------- preview saves nothing, import saves ----------

    [Fact]
    public async Task PreviewAsync_SavesNothing()
    {
        await _service.PreviewAsync(Csv(Row()));

        Assert.Empty(_context.Resources);
    }

    [Fact]
    public async Task ImportAsync_SavesNewRowsAsFreeResourcesWithoutOwner()
    {
        var preview = await _service.ImportAsync(Csv(Row(subjects: "Математика|Информатика", grades: "5|6")));

        Assert.Equal(1, preview.ImportedCount);
        var saved = _context.Resources
            .Include(r => r.Categories).Include(r => r.GradeLevels).Include(r => r.License)
            .Single();
        Assert.Null(saved.OwnerId);
        Assert.Equal(ResourceKind.External, saved.Kind);
        Assert.Equal("CC BY 4.0", saved.License!.Name);
        Assert.Equal(2, saved.Categories.Count);
        Assert.Equal(2, saved.GradeLevels.Count);
        Assert.Equal("https://example.com/drobi", saved.NormalizedUrl);
    }

    [Fact]
    public async Task ImportAsync_SkipsInvalidRowsAndDuplicatesButSavesTheRest()
    {
        var preview = await _service.ImportAsync(Csv(
            Row(title: "Добър", url: "https://example.com/a"),
            Row(title: "", url: "https://example.com/b"),
            Row(title: "Повторен", url: "https://example.com/a/"),
            Row(title: "Друг добър", url: "https://example.com/c")));

        Assert.Equal(2, preview.ImportedCount);
        Assert.Equal(1, preview.InvalidCount);
        Assert.Equal(1, preview.DuplicateCount);
        Assert.Equal(new[] { "Добър", "Друг добър" }, _context.Resources.OrderBy(r => r.Id).Select(r => r.Title));
    }

    [Fact]
    public async Task ImportAsync_RunTwice_SecondRunOnlyReportsDuplicates()
    {
        var text = Csv(Row(url: "https://example.com/a"), Row(url: "https://example.com/b"));

        await _service.ImportAsync(text);
        var second = await _service.ImportAsync(text);

        Assert.Equal(0, second.ImportedCount);
        Assert.Equal(2, second.DuplicateCount);
        Assert.Equal(2, _context.Resources.Count());
    }

    [Fact]
    public async Task ImportAsync_FileError_SavesNothing()
    {
        var preview = await _service.ImportAsync("title;author\nА;Б");

        Assert.True(preview.HasFileError);
        Assert.Empty(_context.Resources);
    }

    [Fact]
    public async Task ImportAsync_ImportedResourcesCanBeFoundInTheFreeResourcesSection()
    {
        await _service.ImportAsync(Csv(Row()));

        var free = await _resources.SearchAsync(new ResourceFilter { Kind = ResourceKind.External });
        var lessons = await _resources.SearchAsync(new ResourceFilter { Kind = ResourceKind.Lesson });

        Assert.Equal(1, free.TotalCount);
        Assert.Equal(0, lessons.TotalCount);
    }
}
