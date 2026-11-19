using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>Composes a quiz from the question bank: pinned questions and random-from-section draws.</summary>
public interface IQuizItemService
{
    /// <summary>The quiz's items, ordered, with their question and section loaded (for the authoring page).</summary>
    Task<IReadOnlyList<QuizItem>> GetItemsAsync(int quizId);

    /// <summary>Pins a specific bank question to the quiz. Fails when the quiz/question is missing or not allowed.</summary>
    Task<ServiceResult<QuizItem>> AddFixedQuestionAsync(int quizId, int questionId, string userId, bool isAdministrator);

    /// <summary>
    /// Creates a new question in the owner's root bank section and pins it to the quiz, so a question
    /// typed on the quiz lives in the bank. Fails when the quiz is missing, not allowed, or input invalid.
    /// </summary>
    Task<ServiceResult<QuizItem>> AddNewQuestionAsync(int quizId, QuestionInput input, string userId, bool isAdministrator);

    /// <summary>Adds a "draw N random questions from a section" item. Fails when missing, not allowed, or the count is invalid.</summary>
    Task<ServiceResult<QuizItem>> AddRandomSectionAsync(int quizId, int sectionId, int pickCount, string userId, bool isAdministrator);

    /// <summary>Removes an item. Fails when it is missing or not allowed.</summary>
    Task<ServiceResult<QuizItem>> DeleteItemAsync(int itemId, string userId, bool isAdministrator);

    /// <summary>Moves an item one place up or down in the quiz. Fails when it is missing or not allowed.</summary>
    Task<ServiceResult<QuizItem>> MoveItemAsync(int itemId, bool up, string userId, bool isAdministrator);

    /// <summary>
    /// Resolves the quiz's items into a concrete ordered list of questions (with options): the pinned
    /// questions plus a random draw from each random item. Random items use <paramref name="rng"/>.
    /// </summary>
    Task<IReadOnlyList<Question>> ResolveAsync(int quizId, Random? rng = null);
}
