using System.ComponentModel.DataAnnotations;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.ViewModels;

/// <summary>The new/edit quiz form: title, description, mode and (for an exam) the time limit.</summary>
public class QuizFormViewModel
{
    [Required(ErrorMessage = "Въведете заглавие")]
    [StringLength(200, ErrorMessage = "Заглавието е най-много 200 символа")]
    [Display(Name = "Заглавие")]
    public string Title { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Описанието е най-много 1000 символа")]
    [Display(Name = "Описание")]
    public string? Description { get; set; }

    [Display(Name = "Вид")]
    public QuizMode Mode { get; set; }

    [Range(1, 600, ErrorMessage = "Времето е между 1 и 600 минути")]
    [Display(Name = "Време за изпит (минути)")]
    public int? TimeLimitMinutes { get; set; }

    [Range(0, 100, ErrorMessage = "Напусканията са между 0 и 100")]
    [Display(Name = "Допустими напускания (изпит)")]
    public int? MaxLeaves { get; set; }

    [Display(Name = "Разбъркай въпросите")]
    public bool ShuffleQuestions { get; set; } = true;

    [Display(Name = "Разбъркай отговорите")]
    public bool ShuffleOptions { get; set; } = true;
}

/// <summary>The add-question form: the text, the answer options and which one is correct.</summary>
public class QuestionFormViewModel
{
    [Required(ErrorMessage = "Въведете текст на въпроса")]
    [StringLength(1000, ErrorMessage = "Въпросът е най-много 1000 символа")]
    [Display(Name = "Въпрос")]
    public string Text { get; set; } = string.Empty;

    /// <summary>Option texts by position; empty ones are dropped before saving.</summary>
    public List<string> Options { get; set; } = new();

    /// <summary>The position of the correct option; -1 when none is chosen.</summary>
    public int CorrectIndex { get; set; } = -1;

    [Range(1, 100, ErrorMessage = "Точките са между 1 и 100")]
    [Display(Name = "Точки")]
    public int Points { get; set; } = 1;

    /// <summary>Maps the form to a service input, dropping empty option rows and remapping the correct index.</summary>
    public QuestionInput ToInput()
    {
        var options = new List<string>();
        var correctIndex = -1;
        var raw = Options ?? new List<string>();
        for (var i = 0; i < raw.Count; i++)
        {
            var text = (raw[i] ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (i == CorrectIndex)
            {
                correctIndex = options.Count;
            }

            options.Add(text);
        }

        return new QuestionInput(Text ?? string.Empty, options, correctIndex, Points);
    }
}

/// <summary>The manage page: a quiz with its questions, bank composition and an add-question form.</summary>
public class QuizManageViewModel
{
    public Quiz Quiz { get; set; } = null!;

    public QuestionFormViewModel NewQuestion { get; set; } = new();

    /// <summary>The quiz's composition items (pinned questions and random-from-section draws), ordered.</summary>
    public IReadOnlyList<QuizItem> Items { get; set; } = new List<QuizItem>();

    /// <summary>The teacher's sections, for the "draw N random" picker.</summary>
    public IReadOnlyList<Section> Sections { get; set; } = new List<Section>();

    /// <summary>The teacher's bank questions (with section), for the "pin a question" picker.</summary>
    public IReadOnlyList<Question> BankQuestions { get; set; } = new List<Question>();
}

/// <summary>The new/edit section form. <see cref="ParentId"/> is set when creating a subsection.</summary>
public class SectionFormViewModel
{
    [Required(ErrorMessage = "Въведете име на раздела")]
    [StringLength(200, ErrorMessage = "Името е най-много 200 символа")]
    [Display(Name = "Име")]
    public string Name { get; set; } = string.Empty;

    [StringLength(1000, ErrorMessage = "Описанието е най-много 1000 символа")]
    [Display(Name = "Описание")]
    public string? Description { get; set; }

    /// <summary>The parent section when creating a subsection; null for a top-level section.</summary>
    public int? ParentId { get; set; }

    /// <summary>The parent's name, shown on the create form (not posted back).</summary>
    public string? ParentName { get; set; }
}

/// <summary>The section page: the section, its subsections and bank questions, plus an add-question form.</summary>
public class SectionManageViewModel
{
    public Section Section { get; set; } = null!;

    public IReadOnlyList<SectionListItem> Subsections { get; set; } = new List<SectionListItem>();

    public IReadOnlyList<Section> Ancestors { get; set; } = new List<Section>();

    public IReadOnlyList<Question> Questions { get; set; } = new List<Question>();

    /// <summary>The owner's other sections, for the "move selected questions to…" picker (excludes this one).</summary>
    public IReadOnlyList<Section> TargetSections { get; set; } = new List<Section>();

    public QuestionFormViewModel NewQuestion { get; set; } = new();
}

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
