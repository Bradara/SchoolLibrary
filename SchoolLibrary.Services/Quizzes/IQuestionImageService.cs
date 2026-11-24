using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>Pictures in the text of a question: upload, description, removal and serving.</summary>
public interface IQuestionImageService
{
    /// <summary>
    /// Stores a picture for the question and gives it the lowest free number. Only PNG, JPEG, GIF and
    /// WebP (recognized from the bytes) up to 2 MB, at most 10 per question.
    /// </summary>
    Task<ServiceResult<QuestionImage>> AddAsync(int questionId, UploadedFile file, string? altText,
        string userId, bool isAdministrator, CancellationToken cancellationToken = default);

    /// <summary>Changes the description read by screen readers (empty = decorative picture).</summary>
    Task<ServiceResult<QuestionImage>> UpdateAltTextAsync(int imageId, string? altText, string userId, bool isAdministrator);

    /// <summary>
    /// Removes the picture and its markers from the question text. The stored file is deleted when no
    /// other question uses it.
    /// </summary>
    Task<ServiceResult<QuestionImage>> DeleteAsync(int imageId, string userId, bool isAdministrator);

    /// <summary>
    /// Opens a stored picture by the file id in its address, or returns null when there is none.
    /// The id is a random guid, so only someone shown the question knows it.
    /// </summary>
    Task<StoredQuestionImage?> OpenAsync(Guid fileId, CancellationToken cancellationToken = default);
}

/// <summary>A picture ready to be sent to the browser.</summary>
public sealed record StoredQuestionImage(Stream Content, string ContentType);
