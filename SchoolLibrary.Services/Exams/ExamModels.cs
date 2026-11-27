using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Services.Exams;

/// <summary>A class with how many exams it has, for the exams panel.</summary>
public record SchoolClassListItem(SchoolClass SchoolClass, int ExamCount);

/// <summary>A teacher's grade scale with the number of exams that use it.</summary>
public record GradeScaleListItem(GradeScale Scale, int ExamCount);

/// <summary>What a teacher types for a grade scale: its name and the lowest percentage for a 3, 4, 5 and 6.</summary>
public record GradeScaleInput(string? Name, decimal Three, decimal Four, decimal Five, decimal Six);

/// <summary>What a teacher types when giving a quiz to a class as an exam. The code is generated.</summary>
/// <param name="GradeScaleId">The teacher's scale for <see cref="GradingMethod.Thresholds"/>; null is the system scale.</param>
public record ExamAssignmentInput(int QuizId, int SchoolClassId, int? TimeLimitMinutes, int? MaxLeaves, bool CodeHints = true,
    bool OneQuestionPerPage = true, GradingMethod GradingMethod = GradingMethod.Thresholds, int? GradeScaleId = null);

/// <summary>The exam settings a teacher can change after assigning (the quiz and the class stay).</summary>
public record ExamSettingsInput(int? TimeLimitMinutes, int? MaxLeaves, bool CodeHints = true, bool OneQuestionPerPage = true,
    GradingMethod GradingMethod = GradingMethod.Thresholds, int? GradeScaleId = null);

/// <summary>A class exam (with its quiz and class) and its attempt counts, for the exams panel.</summary>
public record ExamAssignmentListItem(ExamAssignment Exam, int SubmittedCount, int InProgressCount);

/// <summary>A class exam with one page of its attempts (submitted and in progress), by class number.</summary>
public record ExamResults(ExamAssignment Exam, PagedResult<QuizAttempt> Attempts);

/// <summary>One student's attempt at a class exam, graded, for the teacher's review.</summary>
public record ExamAttemptReview(QuizAttempt Attempt, QuizResult Result);
