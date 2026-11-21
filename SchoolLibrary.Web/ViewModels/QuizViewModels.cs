using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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

/// <summary>The add/edit question form: the type, the text, the answer options and which one is correct.</summary>
public class QuestionFormViewModel
{
    [Display(Name = "Вид")]
    public QuestionType Type { get; set; } = QuestionType.SingleChoice;

    [Required(ErrorMessage = "Въведете текст на въпроса")]
    [StringLength(1000, ErrorMessage = "Въпросът е най-много 1000 символа")]
    [Display(Name = "Въпрос")]
    public string Text { get; set; } = string.Empty;

    /// <summary>Option texts by position; empty ones are dropped before saving.</summary>
    public List<string> Options { get; set; } = new();

    /// <summary>The position of the correct option; -1 when none is chosen.</summary>
    public int CorrectIndex { get; set; } = -1;

    /// <summary>The positions of the correct options of a multiple-choice question.</summary>
    public List<int> CorrectIndexes { get; set; } = new();

    /// <summary>The correct value of a true/false question; null when neither is chosen.</summary>
    public bool? TrueFalseAnswer { get; set; }

    /// <summary>The answer on the right of each row of a matching question (the item is in <see cref="Options"/>).</summary>
    public List<string> Matches { get; set; } = new();

    /// <summary>The tolerance typed next to each accepted value of a numerical question; blank means exact.</summary>
    public List<string> Tolerances { get; set; } = new();

    /// <summary>Whether a short answer must match the letter case of an accepted answer.</summary>
    [Display(Name = "Различавай малки и главни букви")]
    public bool CaseSensitive { get; set; }

    /// <summary>
    /// Set when a saved multiple-choice, short-answer or numerical question has shares other than the ones the form
    /// produces (e.g. imported from Moodle), so the editor can warn that saving replaces them.
    /// </summary>
    public bool HasCustomShares { get; set; }

    [Range(1, 100, ErrorMessage = "Точките са между 1 и 100")]
    [Display(Name = "Точки")]
    public int Points { get; set; } = 1;

    /// <summary>The form filled in with a saved question, for editing.</summary>
    public static QuestionFormViewModel FromQuestion(Question question)
    {
        var options = question.Options.OrderBy(o => o.Order).ToList();
        var form = new QuestionFormViewModel
        {
            Type = question.Type,
            Text = question.Text,
            Points = question.Points
        };

        if (question.Type == QuestionType.Description)
        {
            // A description stores 0 points; the hidden points field keeps a valid value
            form.Points = 1;
        }
        else if (question.Type == QuestionType.TrueFalse)
        {
            // "Вярно" is the first option, so the answer is true when the first option is the correct one
            var correct = options.FindIndex(o => o.IsCorrect);
            form.TrueFalseAnswer = correct < 0 ? null : correct == 0;
        }
        else if (question.Type == QuestionType.MultipleChoice)
        {
            form.Options = options.Select(o => o.Text).ToList();
            form.CorrectIndexes = options
                .Select((o, i) => (o, i))
                .Where(x => x.o.Fraction > 0)
                .Select(x => x.i)
                .ToList();

            var equal = QuestionInput.MultipleChoice(question.Text, form.Options, form.CorrectIndexes).Fractions!;
            form.HasCustomShares = !options.Select(o => o.Fraction).SequenceEqual(equal);
        }
        else if (question.Type == QuestionType.Matching)
        {
            form.Options = options.Select(o => o.Text).ToList();
            form.Matches = options.Select(o => o.MatchText ?? string.Empty).ToList();
        }
        else if (question.Type == QuestionType.Numerical)
        {
            // Values and tolerances are shown with a decimal comma; every accepted value gets all the points
            form.Options = options.Select(o => o.Text == NumericAnswer.AnyNumber ? o.Text : NumericAnswer.Display(o.Text)).ToList();
            form.Tolerances = options.Select(o => NumericAnswer.Display(o.Tolerance ?? 0m)).ToList();
            form.HasCustomShares = options.Any(o => o.Fraction != AnswerOption.FullCredit);
        }
        else if (question.Type == QuestionType.ShortAnswer)
        {
            // The form gives every accepted answer all the points
            form.Options = options.Select(o => o.Text).ToList();
            form.CaseSensitive = question.CaseSensitive;
            form.HasCustomShares = options.Any(o => o.Fraction != AnswerOption.FullCredit);
        }
        else
        {
            form.Options = options.Select(o => o.Text).ToList();
            form.CorrectIndex = options.FindIndex(o => o.IsCorrect);
        }

        return form;
    }

    /// <summary>
    /// Maps the form to a service input. A true/false question takes its fixed options; for the others,
    /// empty option rows are dropped and the correct index (or, for multiple choice, the correct indexes)
    /// is remapped. An unknown type is passed on as sent, so the service rejects it.
    /// </summary>
    public QuestionInput ToInput()
    {
        if (Type == QuestionType.TrueFalse)
        {
            return QuestionInput.TrueFalse(Text ?? string.Empty, TrueFalseAnswer, Points);
        }

        if (Type == QuestionType.Description)
        {
            return QuestionInput.Description(Text ?? string.Empty);
        }

        if (Type == QuestionType.Matching)
        {
            // A row with only an answer is an extra wrong answer; a row with only an item is kept so the service
            // reports the missing answer; fully empty rows are dropped
            var pairs = new List<(string Item, string Answer)>();
            var items = Options ?? new List<string>();
            var answers = Matches ?? new List<string>();
            for (var i = 0; i < Math.Max(items.Count, answers.Count); i++)
            {
                var item = (i < items.Count ? items[i] : null)?.Trim() ?? string.Empty;
                var answer = (i < answers.Count ? answers[i] : null)?.Trim() ?? string.Empty;
                if (item.Length > 0 || answer.Length > 0)
                {
                    pairs.Add((item, answer));
                }
            }

            return QuestionInput.Matching(Text ?? string.Empty, pairs, Points);
        }

        var options = new List<string>();
        var tolerances = new List<decimal?>();
        var correctIndex = -1;
        var correctIndexes = new List<int>();
        var ticked = (CorrectIndexes ?? new List<int>()).ToHashSet();
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

            if (ticked.Contains(i))
            {
                correctIndexes.Add(options.Count);
            }

            // Blank tolerance means exact; text that is not a number becomes null, which the service rejects
            var toleranceText = Tolerances != null && i < Tolerances.Count ? Tolerances[i] : null;
            tolerances.Add(string.IsNullOrWhiteSpace(toleranceText) ? 0m : NumericAnswer.Parse(toleranceText));

            options.Add(text);
        }

        return Type switch
        {
            QuestionType.MultipleChoice => QuestionInput.MultipleChoice(Text ?? string.Empty, options, correctIndexes, Points),
            QuestionType.ShortAnswer => QuestionInput.ShortAnswer(Text ?? string.Empty, options, CaseSensitive, Points),
            QuestionType.Numerical => QuestionInput.Numerical(Text ?? string.Empty, options, tolerances, Points),
            _ => new QuestionInput(Text ?? string.Empty, options, correctIndex, Points, Type)
        };
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

/// <summary>The first exam entry step: the student types the code the teacher gave.</summary>
public class ExamCodeViewModel
{
    [Required(ErrorMessage = "Въведете код на изпита")]
    [StringLength(12, ErrorMessage = "Кодът е най-много 12 символа")]
    [Display(Name = "Код на изпита")]
    public string Code { get; set; } = string.Empty;
}

/// <summary>
/// The second exam entry step: the code (carried in a hidden field) plus the student's name and class
/// number. The exam's title, class and state are shown above the form and are not posted back.
/// </summary>
public class ExamEntryViewModel
{
    [Required(ErrorMessage = "Въведете код на изпита")]
    [StringLength(12, ErrorMessage = "Кодът е най-много 12 символа")]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Въведете име")]
    [StringLength(100, ErrorMessage = "Името е най-много 100 символа")]
    [Display(Name = "Име и фамилия")]
    public string StudentName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Въведете номер в класа")]
    [StringLength(20, ErrorMessage = "Номерът е най-много 20 символа")]
    [Display(Name = "Номер в класа")]
    public string? StudentNumber { get; set; }

    /// <summary>The exam behind the code (for display); null until it is looked up.</summary>
    [BindNever]
    public ExamEntry? Exam { get; set; }
}

/// <summary>
/// An exam handed in by force because the student left it more often than allowed. Carries the graded
/// result only when the student may see it; the answers are never shown.
/// </summary>
public class ExamForcedViewModel : ExamPendingViewModel
{
    public QuizResult? Result { get; set; }
}

/// <summary>A submitted class exam whose result the teacher has not released yet.</summary>
public class ExamPendingViewModel
{
    public string Title { get; set; } = string.Empty;

    public string? ClassName { get; set; }

    public string StudentName { get; set; } = string.Empty;

    public DateTime? SubmittedOn { get; set; }
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
