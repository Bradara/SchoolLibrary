using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>Grades one type of question. Pure logic: it reads a loaded question and the student's answer.</summary>
public interface IQuestionGrader
{
    /// <summary>The question type this grader handles.</summary>
    QuestionType Type { get; }

    /// <summary>
    /// Grades <paramref name="answer"/> against <paramref name="question"/>. The returned answer keeps only
    /// what belongs to the question (an option id from another question is dropped), so it is safe to store.
    /// </summary>
    QuestionGrade Grade(Question question, SubmittedAnswer answer);
}

/// <summary>The outcome of grading one question.</summary>
/// <param name="AwardedPoints">Points earned, from 0 to the question's points, rounded to two decimals.</param>
/// <param name="IsCorrect">Whether the answer earned full credit.</param>
/// <param name="Answer">The answer as accepted (foreign or invalid parts removed).</param>
public record QuestionGrade(decimal AwardedPoints, bool IsCorrect, SubmittedAnswer Answer);
