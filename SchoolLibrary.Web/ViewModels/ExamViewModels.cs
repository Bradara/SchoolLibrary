using System.ComponentModel.DataAnnotations;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Exams;

namespace SchoolLibrary.ViewModels;

/// <summary>The exams panel: the teacher's classes and their exams (optionally one class only).</summary>
public class ExamsIndexViewModel
{
    public IReadOnlyList<SchoolClassListItem> Classes { get; set; } = new List<SchoolClassListItem>();

    public IReadOnlyList<ExamAssignmentListItem> Exams { get; set; } = new List<ExamAssignmentListItem>();

    /// <summary>The class the exam list is narrowed to; null shows every class.</summary>
    public int? SelectedClassId { get; set; }
}

/// <summary>The new/rename class form.</summary>
public class SchoolClassFormViewModel
{
    [Required(ErrorMessage = "Въведете име на класа")]
    [StringLength(50, ErrorMessage = "Името е най-много 50 символа")]
    [Display(Name = "Клас")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>The time limit and proctoring of a class exam (used when assigning and when editing).</summary>
public class ExamSettingsViewModel
{
    [Range(1, 600, ErrorMessage = "Времето е между 1 и 600 минути")]
    [Display(Name = "Време за изпит (минути)")]
    public int? TimeLimitMinutes { get; set; }

    [Range(0, 100, ErrorMessage = "Напусканията са между 0 и 100")]
    [Display(Name = "Допустими напускания")]
    public int? MaxLeaves { get; set; }

    [Display(Name = "Подсказки при писане на код")]
    public bool CodeHints { get; set; } = true;

    [Display(Name = "По един въпрос на екран")]
    public bool OneQuestionPerPage { get; set; } = true;
}

/// <summary>The "give a practice quiz to a class as an exam" form, with the pickers' choices.</summary>
public class ExamAssignFormViewModel : ExamSettingsViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Изберете тест")]
    [Display(Name = "Тест")]
    public int QuizId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Изберете клас")]
    [Display(Name = "Клас")]
    public int SchoolClassId { get; set; }

    public IReadOnlyList<SchoolClassListItem> Classes { get; set; } = new List<SchoolClassListItem>();

    public IReadOnlyList<Quiz> Quizzes { get; set; } = new List<Quiz>();
}

/// <summary>One class exam's page: its results and the settings form.</summary>
public class ExamDetailsViewModel
{
    public ExamResults Results { get; set; } = null!;

    public ExamSettingsViewModel Settings { get; set; } = new();
}
