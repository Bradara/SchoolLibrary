using System.ComponentModel.DataAnnotations;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.ViewModels;

/// <summary>The code-entry form: a student types the exam code, their name and class number.</summary>
public class ExamEntryViewModel
{
    [Required(ErrorMessage = "Въведете код на изпита")]
    [StringLength(12, ErrorMessage = "Кодът е най-много 12 символа")]
    [Display(Name = "Код на изпита")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Въведете име")]
    [StringLength(100, ErrorMessage = "Името е най-много 100 символа")]
    [Display(Name = "Име и фамилия")]
    public string StudentName { get; set; } = string.Empty;

    [StringLength(20, ErrorMessage = "Номерът е най-много 20 символа")]
    [Display(Name = "Номер в класа")]
    public string? StudentNumber { get; set; }
}

/// <summary>An exam attempt in progress, with the seconds left (null when the exam has no time limit).</summary>
public class ExamTakeViewModel
{
    public QuizAttempt Attempt { get; set; } = null!;

    public int? RemainingSeconds { get; set; }
}

/// <summary>A finished exam attempt: the graded result plus who took it and when.</summary>
public class ExamResultViewModel
{
    public QuizResult Result { get; set; } = null!;

    public string StudentName { get; set; } = string.Empty;

    public DateTime? SubmittedOn { get; set; }
}
