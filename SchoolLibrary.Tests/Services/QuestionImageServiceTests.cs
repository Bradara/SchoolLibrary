using System.Text;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Tests.Services;

public class QuestionImageServiceTests
{
    private const string Owner = "teacher-1";
    private const string Other = "teacher-2";

    private readonly ApplicationDbContext _context = TestDbContextFactory.Create();
    private readonly InMemoryFileStorage _storage = new();
    private readonly QuestionImageService _service;

    public QuestionImageServiceTests()
    {
        _service = new QuestionImageService(_context, _storage);
    }

    // ---------- helpers ----------

    private static readonly byte[] PngHeader = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static byte[] Png(int length = 64)
    {
        var bytes = new byte[length];
        PngHeader.CopyTo(bytes, 0);
        return bytes;
    }

    private static byte[] Jpeg() => new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
    private static byte[] Gif() => Encoding.ASCII.GetBytes("GIF89a....");
    private static byte[] Webp() => Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 ");

    private static UploadedFile File(string name, byte[] bytes, long? declaredLength = null) =>
        new(name, "image/png", declaredLength ?? bytes.Length, new MemoryStream(bytes));

    private Question BankQuestion(string ownerId = Owner, string text = "Какво показва фигурата?")
    {
        var section = new Section { Name = "Фигури", OwnerId = ownerId, CreatedOn = DateTime.UtcNow };
        var question = new Question { Text = text, Type = QuestionType.SingleChoice, Section = section };
        _context.Questions.Add(question);
        _context.SaveChanges();
        return question;
    }

    private Question QuizQuestion(string ownerId = Owner)
    {
        var quiz = new Quiz { Title = "Тест", OwnerId = ownerId, CreatedOn = DateTime.UtcNow };
        var question = new Question { Text = "Въпрос", Type = QuestionType.SingleChoice, Quiz = quiz };
        _context.Questions.Add(question);
        _context.SaveChanges();
        return question;
    }

    private Task<ServiceResult<QuestionImage>> AddAsync(Question question, byte[]? bytes = null,
        string name = "image.png", string? alt = null, string user = Owner, bool admin = false) =>
        _service.AddAsync(question.Id, File(name, bytes ?? Png()), alt, user, admin);

    // ---------- add ----------

    [Fact]
    public async Task AddAsync_Png_StoresFileAndRowWithNumberOne()
    {
        var question = BankQuestion();
        var bytes = Png();

        var result = await AddAsync(question, bytes, alt: "Схема на стек");

        Assert.True(result.Succeeded);
        var image = _context.QuestionImages.Single();
        Assert.Equal(question.Id, image.QuestionId);
        Assert.Equal(1, image.Number);
        Assert.Equal("image/png", image.ContentType);
        Assert.Equal(bytes.Length, image.SizeBytes);
        Assert.Equal("image.png", image.FileName);
        Assert.Equal("Схема на стек", image.AltText);
        Assert.StartsWith(QuestionImageFiles.KeyPrefix, image.StorageKey);
        Assert.EndsWith(".png", image.StorageKey);
        Assert.Equal(bytes, _storage.Files[image.StorageKey]);
    }

    [Theory]
    [InlineData("jpeg", "image/jpeg", ".jpg")]
    [InlineData("gif", "image/gif", ".gif")]
    [InlineData("webp", "image/webp", ".webp")]
    public async Task AddAsync_OtherAllowedTypes_AreRecognizedFromBytes(string kind, string contentType, string extension)
    {
        var question = BankQuestion();
        var bytes = kind switch { "jpeg" => Jpeg(), "gif" => Gif(), _ => Webp() };

        var result = await AddAsync(question, bytes, name: "picture.png");

        Assert.True(result.Succeeded);
        Assert.Equal(contentType, result.Value!.ContentType);
        Assert.EndsWith(extension, result.Value.StorageKey);
        Assert.Equal("picture" + extension, result.Value.FileName);
    }

    [Fact]
    public async Task AddAsync_DirectQuizQuestion_OwnerMayAdd()
    {
        var question = QuizQuestion();

        var result = await AddAsync(question);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AddAsync_TakesLowestFreeNumber()
    {
        var question = BankQuestion();
        var first = (await AddAsync(question)).Value!;
        await AddAsync(question);
        await _service.DeleteAsync(first.Id, Owner, false);

        var result = await AddAsync(question);

        Assert.Equal(1, result.Value!.Number);
    }

    [Fact]
    public async Task AddAsync_EleventhImage_IsRefused()
    {
        var question = BankQuestion();
        for (var i = 0; i < QuestionImage.MaxPerQuestion; i++)
        {
            Assert.True((await AddAsync(question)).Succeeded);
        }

        var result = await AddAsync(question);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Equal(QuestionImage.MaxPerQuestion, _context.QuestionImages.Count());
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("<html><body>не е снимка</body></html>")]
    public async Task AddAsync_SvgOrHtml_IsRefused(string content)
    {
        var question = BankQuestion();

        var result = await AddAsync(question, Encoding.UTF8.GetBytes(content), name: "image.png");

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(_storage.Files);
        Assert.Empty(_context.QuestionImages);
    }

    [Fact]
    public async Task AddAsync_IconFile_IsRefused()
    {
        var question = BankQuestion();

        var result = await AddAsync(question, new byte[] { 0x00, 0x00, 0x01, 0x00, 0x01, 0x00 }, name: "favicon.ico");

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task AddAsync_EmptyFile_IsRefused()
    {
        var question = BankQuestion();

        var result = await AddAsync(question, Array.Empty<byte>());

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task AddAsync_DeclaredTooLarge_IsRefused()
    {
        var question = BankQuestion();

        var result = await _service.AddAsync(question.Id, File("big.png", Png(), QuestionImage.MaxSizeBytes + 1L), null, Owner, false);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task AddAsync_RealSizeOverLimitWithWrongDeclaredLength_IsRefused()
    {
        var question = BankQuestion();
        var bytes = Png(QuestionImage.MaxSizeBytes + 10);

        var result = await _service.AddAsync(question.Id, File("big.png", bytes, declaredLength: 100), null, Owner, false);

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task AddAsync_ExactlyAtLimit_IsAccepted()
    {
        var question = BankQuestion();

        var result = await AddAsync(question, Png(QuestionImage.MaxSizeBytes));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AddAsync_OtherTeacher_IsForbidden()
    {
        var question = BankQuestion();

        var result = await AddAsync(question, user: Other);

        Assert.Equal(ServiceError.Forbidden, result.Error);
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task AddAsync_OtherTeachersQuiz_IsForbidden()
    {
        var question = QuizQuestion();

        var result = await AddAsync(question, user: Other);

        Assert.Equal(ServiceError.Forbidden, result.Error);
    }

    [Fact]
    public async Task AddAsync_Administrator_MayAddToAnyQuestion()
    {
        var question = BankQuestion();

        var result = await AddAsync(question, user: "admin", admin: true);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AddAsync_MissingQuestion_IsNotFound()
    {
        var result = await _service.AddAsync(999, File("image.png", Png()), null, Owner, false);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    [Fact]
    public async Task AddAsync_AltTextTooLong_IsRefused()
    {
        var question = BankQuestion();

        var result = await AddAsync(question, alt: new string('а', QuestionImage.AltTextMaxLength + 1));

        Assert.Equal(ServiceError.Invalid, result.Error);
        Assert.Empty(_storage.Files);
    }

    [Fact]
    public async Task AddAsync_AltText_IsCollapsedToOneLine_AndBlankBecomesNull()
    {
        var question = BankQuestion();

        var described = await AddAsync(question, alt: "  Масив\nс   5 елемента ");
        var blank = await AddAsync(question, alt: "   ");

        Assert.Equal("Масив с 5 елемента", described.Value!.AltText);
        Assert.Null(blank.Value!.AltText);
    }

    [Theory]
    [InlineData(@"C:\Users\учител\Desktop\стек.png", "стек.png")]
    [InlineData("../../etc/passwd.png", "passwd.png")]
    [InlineData("", "image.png")]
    [InlineData(".png", "image.png")]
    public void CleanFileName_KeepsOnlyTheName(string fileName, string expected)
    {
        Assert.Equal(expected, QuestionImageFiles.CleanFileName(fileName, ".png"));
    }

    [Fact]
    public void CleanFileName_LongName_IsCutToTheColumnLength()
    {
        var name = QuestionImageFiles.CleanFileName(new string('x', 400) + ".png", ".png");

        Assert.Equal(QuestionImage.FileNameMaxLength, name.Length);
        Assert.EndsWith(".png", name);
    }

    // ---------- alt text ----------

    [Fact]
    public async Task UpdateAltTextAsync_Owner_ChangesDescription()
    {
        var image = (await AddAsync(BankQuestion())).Value!;

        var result = await _service.UpdateAltTextAsync(image.Id, "Опашка", Owner, false);

        Assert.True(result.Succeeded);
        Assert.Equal("Опашка", _context.QuestionImages.Single().AltText);
    }

    [Fact]
    public async Task UpdateAltTextAsync_OtherTeacher_IsForbidden()
    {
        var image = (await AddAsync(BankQuestion())).Value!;

        var result = await _service.UpdateAltTextAsync(image.Id, "Опашка", Other, false);

        Assert.Equal(ServiceError.Forbidden, result.Error);
    }

    [Fact]
    public async Task UpdateAltTextAsync_TooLong_IsRefused()
    {
        var image = (await AddAsync(BankQuestion())).Value!;

        var result = await _service.UpdateAltTextAsync(image.Id, new string('a', 201), Owner, false);

        Assert.Equal(ServiceError.Invalid, result.Error);
    }

    [Fact]
    public async Task UpdateAltTextAsync_Missing_IsNotFound()
    {
        var result = await _service.UpdateAltTextAsync(999, "x", Owner, false);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    // ---------- delete ----------

    [Fact]
    public async Task DeleteAsync_RemovesRowFileAndMarkers()
    {
        var question = BankQuestion(text: "Разгледайте:\n[[img:1]]\nКакво е [[img:1]] това?");
        question.Statement = "Условие [[img:1]] и [[img:2]]";
        _context.SaveChanges();
        var image = (await AddAsync(question)).Value!;

        var result = await _service.DeleteAsync(image.Id, Owner, false);

        Assert.True(result.Succeeded);
        Assert.Empty(_context.QuestionImages);
        Assert.Empty(_storage.Files);
        var saved = _context.Questions.Single();
        Assert.Equal("Разгледайте:\nКакво е  това?", saved.Text);
        Assert.Equal("Условие  и [[img:2]]", saved.Statement);
    }

    [Fact]
    public async Task DeleteAsync_FileSharedWithACopy_IsKept()
    {
        var question = BankQuestion();
        var image = (await AddAsync(question)).Value!;
        var copy = BankQuestion();
        _context.QuestionImages.Add(new QuestionImage
        {
            QuestionId = copy.Id, Number = 1, StorageKey = image.StorageKey,
            FileName = image.FileName, ContentType = image.ContentType, SizeBytes = image.SizeBytes
        });
        _context.SaveChanges();

        await _service.DeleteAsync(image.Id, Owner, false);

        Assert.True(_storage.Files.ContainsKey(image.StorageKey));
        Assert.Single(_context.QuestionImages);
    }

    [Fact]
    public async Task DeleteAsync_OtherTeacher_IsForbiddenAndKeepsEverything()
    {
        var image = (await AddAsync(BankQuestion())).Value!;

        var result = await _service.DeleteAsync(image.Id, Other, false);

        Assert.Equal(ServiceError.Forbidden, result.Error);
        Assert.Single(_context.QuestionImages);
        Assert.Single(_storage.Files);
    }

    [Fact]
    public async Task DeleteAsync_Missing_IsNotFound()
    {
        var result = await _service.DeleteAsync(999, Owner, false);

        Assert.Equal(ServiceError.NotFound, result.Error);
    }

    // ---------- open ----------

    [Fact]
    public async Task OpenAsync_KnownFileId_ReturnsBytesAndType()
    {
        var bytes = Png();
        var image = (await AddAsync(BankQuestion(), bytes)).Value!;
        var fileId = QuestionImageFiles.FileIdOf(image.StorageKey)!.Value;

        var stored = await _service.OpenAsync(fileId);

        Assert.NotNull(stored);
        Assert.Equal("image/png", stored!.ContentType);
        using var buffer = new MemoryStream();
        await stored.Content.CopyToAsync(buffer);
        Assert.Equal(bytes, buffer.ToArray());
    }

    [Fact]
    public async Task OpenAsync_UnknownFileId_ReturnsNull()
    {
        await AddAsync(BankQuestion());

        Assert.Null(await _service.OpenAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task OpenAsync_RowWithoutStoredFile_ReturnsNull()
    {
        var image = (await AddAsync(BankQuestion())).Value!;
        _storage.Files.Clear();

        Assert.Null(await _service.OpenAsync(QuestionImageFiles.FileIdOf(image.StorageKey)!.Value));
    }

    // ---------- helpers of the files ----------

    [Fact]
    public void FileIdOf_ForeignKey_ReturnsNull()
    {
        Assert.Null(QuestionImageFiles.FileIdOf("resources/abc.pdf"));
        Assert.Null(QuestionImageFiles.FileIdOf("questions/not-a-guid.png"));
    }

    [Fact]
    public void NextFreeNumber_AllTaken_ReturnsNull()
    {
        Assert.Null(QuestionImageFiles.NextFreeNumber(Enumerable.Range(1, QuestionImage.MaxPerQuestion)));
        Assert.Equal(3, QuestionImageFiles.NextFreeNumber(new[] { 1, 2, 4 }));
    }

    [Fact]
    public async Task DeleteUnusedAsync_DeletesOnlyKeysWithoutRows()
    {
        var image = (await AddAsync(BankQuestion())).Value!;
        _storage.Files["questions/orphan.png"] = Png();

        await QuestionImageFiles.DeleteUnusedAsync(_context, _storage, new[] { image.StorageKey, "questions/orphan.png" });

        Assert.True(_storage.Files.ContainsKey(image.StorageKey));
        Assert.False(_storage.Files.ContainsKey("questions/orphan.png"));
    }

    // ---------- markers ----------

    [Fact]
    public void Markers_NumbersIn_ReturnsDistinctInOrder()
    {
        Assert.Equal(new[] { 2, 1 }, QuestionImageMarkers.NumbersIn("а [[img:2]] б [[img:1]] [[img:2]] [[img:x]]"));
        Assert.Empty(QuestionImageMarkers.NumbersIn(null));
    }

    [Fact]
    public void Markers_Remove_LeavesOtherMarkersAndNull()
    {
        Assert.Equal("[[img:10]] и", QuestionImageMarkers.Remove("[[img:10]] и [[img:1]]", 1));
        Assert.Null(QuestionImageMarkers.Remove(null, 1));
    }
}
