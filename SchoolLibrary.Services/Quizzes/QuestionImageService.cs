using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Resources;
using SchoolLibrary.Services.Storage;

namespace SchoolLibrary.Services.Quizzes;

public class QuestionImageService : IQuestionImageService
{
    private const string NotFoundMessage = "Въпросът не е намерен.";
    private const string ImageNotFoundMessage = "Изображението не е намерено.";
    private const string ForbiddenMessage = "Нямате права за това действие.";
    private const string NoFileMessage = "Изберете изображение за качване.";
    private const string NotAllowedMessage = "Разрешени са само изображения PNG, JPEG, GIF и WebP.";
    private const string AltTooLongMessage = "Описанието на изображението не може да бъде по-дълго от 200 символа.";

    private static readonly string TooLargeMessage =
        $"Изображението не може да бъде по-голямо от {QuestionImage.MaxSizeBytes / (1024 * 1024)} МБ.";

    private static readonly string TooManyMessage =
        $"Към един въпрос могат да се добавят най-много {QuestionImage.MaxPerQuestion} изображения.";

    private readonly ApplicationDbContext _context;
    private readonly IFileStorage _storage;

    public QuestionImageService(ApplicationDbContext context, IFileStorage storage)
    {
        _context = context;
        _storage = storage;
    }

    public async Task<ServiceResult<QuestionImage>> AddAsync(int questionId, UploadedFile file, string? altText,
        string userId, bool isAdministrator, CancellationToken cancellationToken = default)
    {
        var question = await LoadQuestionAsync(questionId, cancellationToken);
        if (question == null)
        {
            return Failure(ServiceError.NotFound, NotFoundMessage);
        }

        if (!CanManage(question, userId, isAdministrator))
        {
            return Failure(ServiceError.Forbidden, ForbiddenMessage);
        }

        if (!QuestionImageFiles.TryCleanAltText(altText, out var cleanedAlt))
        {
            return Failure(ServiceError.Invalid, AltTooLongMessage);
        }

        var number = QuestionImageFiles.NextFreeNumber(question.Images.Select(i => i.Number));
        if (number == null)
        {
            return Failure(ServiceError.Invalid, TooManyMessage);
        }

        if (file == null || file.Length <= 0)
        {
            return Failure(ServiceError.Invalid, NoFileMessage);
        }

        if (file.Length > QuestionImage.MaxSizeBytes)
        {
            return Failure(ServiceError.Invalid, TooLargeMessage);
        }

        // The declared length may be wrong: read at most one byte over the limit
        var bytes = await ReadLimitedAsync(file.Content, QuestionImage.MaxSizeBytes + 1, cancellationToken);
        if (bytes.Length == 0)
        {
            return Failure(ServiceError.Invalid, NoFileMessage);
        }

        if (bytes.Length > QuestionImage.MaxSizeBytes)
        {
            return Failure(ServiceError.Invalid, TooLargeMessage);
        }

        var type = QuestionImageFiles.DetectType(bytes);
        if (type == null)
        {
            return Failure(ServiceError.Invalid, NotAllowedMessage);
        }

        var image = new QuestionImage
        {
            QuestionId = question.Id,
            Number = number.Value,
            StorageKey = QuestionImageFiles.NewKey(type.Value.Extension),
            FileName = QuestionImageFiles.CleanFileName(file.FileName, type.Value.Extension),
            ContentType = type.Value.ContentType,
            SizeBytes = bytes.Length,
            AltText = cleanedAlt
        };

        using (var content = new MemoryStream(bytes, writable: false))
        {
            await _storage.SaveAsync(image.StorageKey, content, cancellationToken);
        }

        try
        {
            _context.QuestionImages.Add(image);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Keep no file without a row
            await _storage.DeleteAsync(image.StorageKey, CancellationToken.None);
            throw;
        }

        return ServiceResult<QuestionImage>.Success(image);
    }

    public async Task<ServiceResult<QuestionImage>> UpdateAltTextAsync(int imageId, string? altText, string userId, bool isAdministrator)
    {
        var image = await LoadImageAsync(imageId);
        if (image == null)
        {
            return Failure(ServiceError.NotFound, ImageNotFoundMessage);
        }

        if (!CanManage(image.Question, userId, isAdministrator))
        {
            return Failure(ServiceError.Forbidden, ForbiddenMessage);
        }

        if (!QuestionImageFiles.TryCleanAltText(altText, out var cleaned))
        {
            return Failure(ServiceError.Invalid, AltTooLongMessage);
        }

        image.AltText = cleaned;
        await _context.SaveChangesAsync();

        return ServiceResult<QuestionImage>.Success(image);
    }

    public async Task<ServiceResult<QuestionImage>> DeleteAsync(int imageId, string userId, bool isAdministrator)
    {
        var image = await LoadImageAsync(imageId);
        if (image == null)
        {
            return Failure(ServiceError.NotFound, ImageNotFoundMessage);
        }

        var question = image.Question;
        if (!CanManage(question, userId, isAdministrator))
        {
            return Failure(ServiceError.Forbidden, ForbiddenMessage);
        }

        // A marker without its picture would show as text
        question.Text = QuestionImageMarkers.Remove(question.Text, image.Number) ?? string.Empty;
        question.Statement = QuestionImageMarkers.Remove(question.Statement, image.Number);

        _context.QuestionImages.Remove(image);
        await _context.SaveChangesAsync();

        await QuestionImageFiles.DeleteUnusedAsync(_context, _storage, new[] { image.StorageKey });

        return ServiceResult<QuestionImage>.Success(image);
    }

    public async Task<StoredQuestionImage?> OpenAsync(Guid fileId, CancellationToken cancellationToken = default)
    {
        // Every key made for a picture is "questions/{guid}{extension}"
        var prefix = $"{QuestionImageFiles.KeyPrefix}{fileId:N}.";
        var image = await _context.QuestionImages
            .AsNoTracking()
            .Where(i => i.StorageKey.StartsWith(prefix))
            .Select(i => new { i.StorageKey, i.ContentType })
            .FirstOrDefaultAsync(cancellationToken);
        if (image == null)
        {
            return null;
        }

        var content = await _storage.OpenReadAsync(image.StorageKey, cancellationToken);
        return content == null ? null : new StoredQuestionImage(content, image.ContentType);
    }

    // A bank question belongs to a section, a direct question to a quiz
    public static bool CanManage(Question question, string? userId, bool isAdministrator)
    {
        if (question.Quiz != null)
        {
            return QuizPermissions.CanManage(question.Quiz, userId, isAdministrator);
        }

        return question.Section != null && SectionPermissions.CanManage(question.Section, userId, isAdministrator);
    }

    private Task<Question?> LoadQuestionAsync(int questionId, CancellationToken cancellationToken)
        => _context.Questions
            .Include(q => q.Quiz)
            .Include(q => q.Section)
            .Include(q => q.Images)
            .FirstOrDefaultAsync(q => q.Id == questionId, cancellationToken);

    private Task<QuestionImage?> LoadImageAsync(int imageId)
        => _context.QuestionImages
            .Include(i => i.Question).ThenInclude(q => q.Quiz)
            .Include(i => i.Question).ThenInclude(q => q.Section)
            .FirstOrDefaultAsync(i => i.Id == imageId);

    private static async Task<byte[]> ReadLimitedAsync(Stream content, int limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while (buffer.Length < limit
               && (read = await content.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, limit - buffer.Length)), cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static ServiceResult<QuestionImage> Failure(ServiceError error, string message)
        => ServiceResult<QuestionImage>.Failure(error, message);
}
