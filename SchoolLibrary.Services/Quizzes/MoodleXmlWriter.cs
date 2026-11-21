using System.Globalization;
using System.Net;
using System.Xml.Linq;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// Writes questions in the Moodle XML quiz format, the counterpart of <see cref="MoodleXmlParser"/>.
/// Every supported question type is written: single choice and multiple choice (<c>multichoice</c>),
/// <c>truefalse</c>, <c>shortanswer</c>, <c>numerical</c>, <c>matching</c> and <c>description</c>.
/// Answer shares are written as Moodle expects them (33.33 → 33.33333), so Moodle accepts them as listed grades.
/// </summary>
public static class MoodleXmlWriter
{
    /// <summary>One group of questions, written after a category marker when <see cref="Category"/> is set.</summary>
    public record Group(string? Category, IReadOnlyList<Question> Questions);

    // The grades Moodle lists for an answer (in percent); a share within 0.01 of one is written as that grade
    private static readonly decimal[] MoodleGrades =
    {
        100m, 90m, 83.33333m, 80m, 75m, 70m, 66.66667m, 60m, 50m, 40m, 33.33333m, 30m, 25m, 20m,
        16.66667m, 14.28571m, 12.5m, 11.11111m, 10m, 5m, 0m
    };

    private const int MaxNameLength = 60;

    /// <summary>Writes the questions (in the given order) without category markers.</summary>
    public static string Write(IEnumerable<Question> questions)
        => Write(new[] { new Group(null, questions.ToList()) });

    /// <summary>Writes each group, preceded by its category marker (a path such as <c>$course$/top/Тема/Подтема</c>).</summary>
    public static string Write(IEnumerable<Group> groups)
    {
        var quiz = new XElement("quiz");
        foreach (var group in groups)
        {
            if (!string.IsNullOrEmpty(group.Category))
            {
                quiz.Add(new XElement("question", new XAttribute("type", "category"),
                    new XElement("category", new XElement("text", group.Category))));
            }

            foreach (var question in group.Questions)
            {
                quiz.Add(WriteQuestion(question));
            }
        }

        var document = new XDocument(new XDeclaration("1.0", "UTF-8", null), quiz);
        return document.Declaration + Environment.NewLine + document;
    }

    /// <summary>A category path from section names (root first); a "/" inside a name is doubled, as in Moodle.</summary>
    public static string CategoryPath(IEnumerable<string> names)
        => "$course$/top/" + string.Join("/", names.Select(n => n.Replace("/", "//")));

    private static XElement WriteQuestion(Question question)
    {
        var options = question.Options.OrderBy(o => o.Order).ToList();
        var element = new XElement("question", new XAttribute("type", MoodleType(question.Type)),
            new XElement("name", new XElement("text", Name(question.Text))),
            new XElement("questiontext", new XAttribute("format", "html"),
                new XElement("text", new XCData(Html(question.Text)))),
            new XElement("generalfeedback", new XAttribute("format", "html"), new XElement("text", string.Empty)),
            new XElement("defaultgrade", Number(QuizGrading.MaxPoints(question))),
            new XElement("penalty", "0"),
            new XElement("hidden", "0"));

        switch (question.Type)
        {
            case QuestionType.Description:
                break;

            case QuestionType.TrueFalse:
                // Moodle's two answers are "true" and "false"; "Вярно" is the first option
                var trueOption = options.FirstOrDefault(o => o.Text == QuestionInput.TrueFalseOptions[0]) ?? options.FirstOrDefault();
                var answerIsTrue = trueOption?.IsCorrect ?? false;
                element.Add(PlainAnswer("true", answerIsTrue ? AnswerOption.FullCredit : 0m),
                    PlainAnswer("false", answerIsTrue ? 0m : AnswerOption.FullCredit));
                break;

            case QuestionType.ShortAnswer:
                element.Add(new XElement("usecase", question.CaseSensitive ? "1" : "0"));
                element.Add(options.Select(o => PlainAnswer(o.Text, o.Fraction)));
                break;

            case QuestionType.Numerical:
                foreach (var option in options)
                {
                    var answer = PlainAnswer(option.Text, option.Fraction);
                    answer.Add(new XElement("tolerance", Number(option.Tolerance ?? 0m)));
                    element.Add(answer);
                }

                // Units are not used
                element.Add(new XElement("unitgradingtype", "0"), new XElement("unitpenalty", "0"),
                    new XElement("showunits", "3"), new XElement("unitsleft", "0"));
                break;

            case QuestionType.Matching:
                element.Add(new XElement("shuffleanswers", "true"));
                element.Add(options.Select(o => new XElement("subquestion", new XAttribute("format", "html"),
                    new XElement("text", Html(o.Text, paragraph: false)),
                    new XElement("answer", new XElement("text", o.MatchText ?? string.Empty)))));
                break;

            default:
                // Single and multiple choice are both "multichoice"; <single> tells them apart
                element.Add(new XElement("single", question.Type == QuestionType.MultipleChoice ? "false" : "true"),
                    new XElement("shuffleanswers", "true"),
                    new XElement("answernumbering", "abc"));
                element.Add(options.Select(o => new XElement("answer",
                    new XAttribute("fraction", Fraction(o.Fraction)), new XAttribute("format", "html"),
                    new XElement("text", Html(o.Text, paragraph: false)),
                    new XElement("feedback", new XAttribute("format", "html"), new XElement("text", string.Empty)))));
                break;
        }

        return element;
    }

    private static string MoodleType(QuestionType type) => type switch
    {
        QuestionType.TrueFalse => "truefalse",
        QuestionType.ShortAnswer => "shortanswer",
        QuestionType.Numerical => "numerical",
        QuestionType.Matching => "matching",
        QuestionType.Description => "description",
        _ => "multichoice"
    };

    // An answer whose text is plain (not HTML): true/false, short answer, numerical
    private static XElement PlainAnswer(string text, decimal fraction)
        => new("answer", new XAttribute("fraction", Fraction(fraction)), new XAttribute("format", "moodle_auto_format"),
            new XElement("text", text),
            new XElement("feedback", new XAttribute("format", "html"), new XElement("text", string.Empty)));

    // The question's name in the bank: the start of its text on one line
    private static string Name(string text)
    {
        var line = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= MaxNameLength ? line : line[..MaxNameLength].TrimEnd() + "…";
    }

    // Plain text as HTML: encoded, with line breaks kept
    private static string Html(string text, bool paragraph = true)
    {
        var html = WebUtility.HtmlEncode(text).Replace("\r\n", "\n").Replace("\n", "<br>");
        return paragraph ? $"<p>{html}</p>" : html;
    }

    // A share in percent, snapped to Moodle's listed grade when it is within 0.01 of one (keeping the sign)
    private static string Fraction(decimal fraction)
    {
        var size = Math.Abs(fraction);
        var grade = MoodleGrades.FirstOrDefault(g => Math.Abs(g - size) < 0.01m, size);
        return Number(fraction < 0 ? -grade : grade);
    }

    private static string Number(decimal value)
        => value.ToString("0.#######", CultureInfo.InvariantCulture);
}
