using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>Manages the questions inside a bank section (create, edit, delete, list).</summary>
public interface IQuestionBankService
{
    /// <summary>The questions of a section, ordered, each with its options. Empty when the section has none.</summary>
    Task<IReadOnlyList<Question>> GetBySectionAsync(int sectionId);

    /// <summary>Every bank question of the owner, with its section, for a picker. Null owner returns all.</summary>
    Task<IReadOnlyList<Question>> GetForOwnerAsync(string? ownerId);

    /// <summary>A bank question with its section and options (ordered), or null when it does not exist.</summary>
    Task<Question?> GetQuestionAsync(int questionId);

    /// <summary>Adds a question to a section. Fails when the section is missing, not allowed, or the input is invalid.</summary>
    Task<ServiceResult<Question>> CreateAsync(int sectionId, QuestionInput input, string userId, bool isAdministrator);

    /// <summary>Replaces a bank question's text, options and points. Fails when missing, not allowed, or invalid.</summary>
    Task<ServiceResult<Question>> UpdateAsync(int questionId, QuestionInput input, string userId, bool isAdministrator);

    /// <summary>Removes a bank question. Fails when it is missing or not allowed.</summary>
    Task<ServiceResult<Question>> DeleteAsync(int questionId, string userId, bool isAdministrator);

    /// <summary>
    /// Moves the given bank questions into another section, appending them after its current questions.
    /// Skips ids the user may not manage or that are missing; returns how many were actually moved.
    /// Fails when the target section is missing or not allowed.
    /// </summary>
    Task<ServiceResult<int>> MoveQuestionsAsync(IReadOnlyCollection<int> questionIds, int targetSectionId, string userId, bool isAdministrator);

    /// <summary>
    /// Imports questions into a section from Aiken text or Moodle XML (detected from the content),
    /// adding the valid ones and reporting the rest. Fails when the section is missing or not allowed.
    /// </summary>
    Task<ServiceResult<ImportSummary>> ImportAsync(int sectionId, string text, string userId, bool isAdministrator);
}
