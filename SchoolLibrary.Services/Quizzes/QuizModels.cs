using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>What a teacher types in the quiz form. The exam code is generated, not entered.</summary>
public record QuizInput(string Title, string? Description, QuizMode Mode, int? TimeLimitMinutes, int? ResourceId);

/// <summary>
/// A single-choice question with its options and which one is correct (by index in <paramref name="Options"/>).
/// </summary>
public record QuestionInput(string Text, IReadOnlyList<string> Options, int CorrectIndex);

/// <summary>A quiz with its question and (submitted) attempt counts, for the management list.</summary>
public record QuizListItem(Quiz Quiz, int QuestionCount, int AttemptCount);
