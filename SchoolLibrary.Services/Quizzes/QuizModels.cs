using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>What a teacher types in the quiz form. The exam code is generated, not entered.</summary>
public record QuizInput(string Title, string? Description, QuizMode Mode, int? TimeLimitMinutes, int? ResourceId, int? MaxLeaves = null);

/// <summary>
/// A single-choice question with its options, which one is correct (by index in <paramref name="Options"/>),
/// and how many points it is worth.
/// </summary>
public record QuestionInput(string Text, IReadOnlyList<string> Options, int CorrectIndex, int Points = 1);

/// <summary>A quiz with its question and (submitted) attempt counts, for the management list.</summary>
public record QuizListItem(Quiz Quiz, int QuestionCount, int AttemptCount);

/// <summary>How one question was answered: what was picked, which option was correct, and the outcome.</summary>
public record QuestionResult(Question Question, int? SelectedOptionId, int? CorrectOptionId, bool IsCorrect);

/// <summary>The graded outcome of a quiz: the score, grade and the per-question breakdown.</summary>
public record QuizResult(Quiz Quiz, int Score, int MaxScore, IReadOnlyList<QuestionResult> Questions)
{
    /// <summary>The score as a whole-number percentage of the maximum.</summary>
    public int Percent => QuizGradeScale.Percent(Score, MaxScore);

    /// <summary>The 2–6 grade for the score.</summary>
    public int Grade => QuizGradeScale.Grade(Score, MaxScore);

    /// <summary>The Bulgarian label for <see cref="Grade"/> (Отличен, Много добър, …).</summary>
    public string GradeLabel => QuizGradeScale.Label(Grade);
}

/// <summary>A practice quiz with the concrete questions resolved for this run (random draws fixed).</summary>
public record PracticeSet(Quiz Quiz, IReadOnlyList<Question> Questions);

/// <summary>A quiz with one page of its submitted attempts, for the teacher's review page.</summary>
public record QuizReview(Quiz Quiz, PagedResult<QuizAttempt> Attempts);

/// <summary>Proctoring state after a student left the exam: the leave count and whether to submit now.</summary>
public record ProctorStatus(int LeaveCount, bool Flagged, bool ShouldSubmit);
