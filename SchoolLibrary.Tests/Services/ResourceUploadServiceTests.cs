using System.Text;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Resources;
using SchoolLibrary.Services.Storage;

namespace SchoolLibrary.Tests.Services;

public class ResourceUploadServiceTests
{
    private readonly ApplicationDbContext _context = TestDbContextFactory.Create();
    private readonly FakeFileStorage _storage = new();
    private readonly ResourceUploadService _service;

    private readonly Category _math;
    private readonly GradeLevel _grade5;
    private readonly License _license;

    public ResourceUploadServiceTests()
    {
        _service = new ResourceUploadService(_context, _storage);

        _math = new Category { Name = "Математика" };
        _grade5 = new GradeLevel { Number = 5 };
        _license = new License { Name = "CC BY 4.0" };
        _context.Categories.Add(_math);
        _context.GradeLevels.Add(_grade5);
        _context.Licenses.Add(_license);
        _context.SaveChanges();
    }

    // ---------- helpers ----------

    private ResourceInput Input() => new()
    {
        Title = "Моят материал",
        Author = "Иван Петров",
        Source = "Собствен",
        Description = "Описание",
        LicenseId = _license.Id,
        CategoryIds = new List<int> { _math.Id },
        GradeLevelIds = new List<int> { _grade5.Id }
    };

    private static UploadedFile File(string name, byte[] bytes, long? declaredLength = null) =>
        new(name, null, declaredLength ?? bytes.Length, new MemoryStream(bytes));

    private static byte[] Pdf() => Encoding.ASCII.GetBytes("%PDF-1.5\nтест съдържание");
    private static byte[] Epub() => new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00 };
    private static byte[] Markdown() => Encoding.UTF8.GetBytes("# Заглавие\n\nНякакъв **текст**.");

    private async Task<ServiceResult<Resource>> UploadAsync(string name, byte[] bytes, ResourceInput? input = null) =>
        await _service.UploadAsync(input ?? Input(), File(name, bytes), ownerId: "teacher-1");

    // ---------- success ----------

    [Fact]
    public async Task UploadAsync_ValidPdf_CreatesUploadResourceWithFileAndStoresBytes()
    {
        var bytes = Pdf();

        var result = await UploadAsync("lesson.pdf", bytes);

        Assert.True(result.Succeeded);
        var resource = _context.Resources.Single();
        Assert.Equal(ResourceKind.Upload, resource.Kind);
        Assert.Equal("teacher-1", resource.OwnerId);
        Assert.Equal(string.Empty, resource.Url);
        Assert.Null(resource.NormalizedUrl);

        var file = _context.ResourceFiles.Single();
        Assert.Equal(resource.Id, file.ResourceId);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("lesson.pdf", file.FileName);
        Assert.Equal(bytes.Length, file.SizeBytes);
        Assert.Equal(64, file.Sha256.Length);

        // The bytes landed in storage under the file's key
        Assert.True(_storage.Files.ContainsKey(file.StorageKey));
        Assert.Equal(bytes, _storage.Files[file.StorageKey]);
    }

    [Fact]
    public async Task UploadAsync_KeepsTheChosenAudience_WithoutUnknownFlags()
    {
        var input = Input();
        input.Audience = ResourceAudience.Teachers | (ResourceAudience)64;

        var result = await UploadAsync("lesson.pdf", Pdf(), input);

        Assert.True(result.Succeeded);
        Assert.Equal(ResourceAudience.Teachers, _context.Resources.Single().Audience);
    }

    [Fact]
    public async Task UploadAsync_ValidEpub_IsSavedWithEpubContentType()
    {
        var result = await UploadAsync("book.epub", Epub());

        Assert.True(result.Succeeded);
        Assert.Equal("application/epub+zip", _context.ResourceFiles.Single().ContentType);
    }

    [Fact]
    public async Task UploadAsync_ValidMarkdown_IsSavedWithMarkdownContentType()
    {
        var result = await UploadAsync("notes.md", Markdown());

        Assert.True(result.Succeeded);
        Assert.StartsWith("text/markdown", _context.ResourceFiles.Single().ContentType);
    }

    // ---------- type and content checks ----------

    [Fact]
    public async Task UploadAsync_PdfExtensionButWrongMagic_IsRejected()
    {
        var result = await UploadAsync("fake.pdf", Encoding.ASCII.GetBytes("not really a pdf"));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(_context.Resources);
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task UploadAsync_DisallowedExtension_IsRejected()
    {
        var result = await UploadAsync("program.exe", Pdf());

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Contains("PDF, EPUB", result.Message);
    }

    [Fact]
    public async Task UploadAsync_BinaryDisguisedAsMarkdown_IsRejected()
    {
        var result = await UploadAsync("evil.md", new byte[] { 0x4D, 0x00, 0x5A, 0x00 });

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(_context.Resources);
    }

    // ---------- size ----------

    [Fact]
    public async Task UploadAsync_EmptyFile_IsRejected()
    {
        var result = await UploadAsync("empty.pdf", Array.Empty<byte>());

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task UploadAsync_FileLargerThanTheLimit_IsRejected_WithoutReadingIt()
    {
        // A tiny stream but a declared length over the limit: rejected before the bytes are read
        var file = File("big.pdf", Pdf(), declaredLength: UploadConstraints.MaxFileSizeBytes + 1);

        var result = await _service.UploadAsync(Input(), file, ownerId: "teacher-1");

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Contains("по-голям", result.Message);
        Assert.Empty(_storage.Files);
    }

    // ---------- duplicates ----------

    [Fact]
    public async Task UploadAsync_SameFileTwice_SecondIsConflict_AndNothingIsDuplicated()
    {
        await UploadAsync("a.pdf", Pdf());

        var second = await UploadAsync("copy.pdf", Pdf());

        Assert.Equal(ServiceError.Conflict, second.Error);
        Assert.Single(_context.Resources);
        Assert.Single(_context.ResourceFiles);
        Assert.Single(_storage.Files);
    }

    // ---------- field validation ----------

    [Fact]
    public async Task UploadAsync_MissingLicense_IsRejected_AndFileIsNotStored()
    {
        var input = Input();
        input.LicenseId = null;

        var result = await UploadAsync("lesson.pdf", Pdf(), input);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Contains("лиценз", result.Message);
        Assert.Empty(_context.Resources);
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task UploadAsync_NoSubjectSelected_IsRejected()
    {
        var input = Input();
        input.CategoryIds = new List<int>();

        var result = await UploadAsync("lesson.pdf", Pdf(), input);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Contains("предмет", result.Message);
    }

    [Fact]
    public async Task UploadAsync_NonexistentSubject_IsRejected()
    {
        var input = Input();
        input.CategoryIds = new List<int> { 999 };

        var result = await UploadAsync("lesson.pdf", Pdf(), input);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Contains("несъществуващ предмет", result.Message);
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task UploadAsync_NonexistentGrade_IsRejected()
    {
        var input = Input();
        input.GradeLevelIds = new List<int> { 999 };

        var result = await UploadAsync("lesson.pdf", Pdf(), input);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Contains("несъществуващ клас", result.Message);
    }

    [Fact]
    public async Task UploadAsync_NonexistentLicense_IsRejected()
    {
        var input = Input();
        input.LicenseId = 999;

        var result = await UploadAsync("lesson.pdf", Pdf(), input);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Contains("несъществуващ лиценз", result.Message);
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task UploadAsync_LongFileName_IsTruncatedToTheColumnLimit()
    {
        var longName = new string('a', 300) + ".pdf";

        var result = await UploadAsync(longName, Pdf());

        Assert.True(result.Succeeded);
        Assert.Equal(ResourceFile.FileNameMaxLength, _context.ResourceFiles.Single().FileName.Length);
    }

    [Fact]
    public async Task UploadAsync_PositiveDeclaredLengthButEmptyContent_IsRejected()
    {
        var file = File("x.pdf", Array.Empty<byte>(), declaredLength: 10);

        var result = await _service.UploadAsync(Input(), file, ownerId: "teacher-1");

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(_context.Resources);
    }

    // A simple in-memory file storage that records what was saved
    private sealed class FakeFileStorage : IFileStorage
    {
        public Dictionary<string, byte[]> Files { get; } = new();

        public Task SaveAsync(string key, Stream content, CancellationToken cancellationToken = default)
        {
            using var buffer = new MemoryStream();
            content.CopyTo(buffer);
            Files[key] = buffer.ToArray();
            return Task.CompletedTask;
        }

        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(key, out var bytes) ? new MemoryStream(bytes) : null);

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            Files.Remove(key);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Files.ContainsKey(key));
    }
}
