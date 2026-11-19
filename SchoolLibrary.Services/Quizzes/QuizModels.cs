using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>What a teacher types in the quiz form. The exam code is generated, not entered.</summary>
public record QuizInput(string Title, string? Description, QuizMode Mode, int? TimeLimitMinutes, int? ResourceId);

/// <summary>
/// A single-choice question with its options and which one is correct (by index in <paramref name="Options"/>).
/// </summary>
public record QuestionInput(string Text, IReadOnlyList<string> Options, int CorrectIndex);

/// <summary>A quiz with its question and (submitted) attempt counts, for the management list.</summary>
public record QuizListItem(Quiz Quiz, int QuestionCount, int AttemptCount);

/// <summary>How one question was answered: what was picked, which option was correct, and the outcome.</summary>
public record QuestionResult(Question Question, int? SelectedOptionId, int? CorrectOptionId, bool IsCorrect);

/// <summary>The graded outcome of a quiz: the score and the per-question breakdown.</summary>
public record QuizResult(Quiz Quiz, int Score, int MaxScore, IReadOnlyList<QuestionResult> Questions);

/// <summary>A quiz with one page of its submitted attempts, for the teacher's review page.</summary>
public record QuizReview(Quiz Quiz, PagedResult<QuizAttempt> Attempts);
