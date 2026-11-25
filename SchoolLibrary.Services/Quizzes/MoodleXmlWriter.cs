using System.Globalization;
using System.Net;
using System.Xml.Linq;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// Writes questions in the Moodle XML quiz format, the counterpart of <see cref="MoodleXmlParser"/>.
/// Every supported question type is written: single choice and multiple choice (<c>multichoice</c>),
/// <c>truefalse</c>, <c>shortanswer</c>, <c>numerical</c>, <c>matching</c>, <c>description</c> and code questions
/// (<c>coderunner</c>: Python as the built-in <c>python3</c> type, C# as the <c>csharp_program</c> prototype,
/// which has to be imported into Moodle first).
/// Answer shares are written as Moodle expects them (33.33 → 33.33333), so Moodle accepts them as listed grades.
/// The pictures of a question text are written as Moodle does: <c>&lt;img src="@@PLUGINFILE@@/name"&gt;</c> in
/// place of the <c>[[img:N]]</c> marker (unplaced ones at the end) and the bytes in a base64 <c>&lt;file&gt;</c>.
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

    // Moodle keeps up to 255 characters of a question's name
    private const int MaxCodeNameLength = 255;

    private static string Shorten(string text, int length)
        => text.Length <= length ? text : text[..(length - 1)].TrimEnd() + "…";

    /// <summary>Writes the questions (in the given order) without category markers.</summary>
    public static string Write(IEnumerable<Question> questions, IReadOnlyDictionary<string, byte[]>? imageFiles = null)
        => Write(new[] { new Group(null, questions.ToList()) }, imageFiles);

    /// <summary>
    /// Writes each group, preceded by its category marker (a path such as <c>$course$/top/Тема/Подтема</c>).
    /// <paramref name="imageFiles"/> gives the bytes of the pictures by storage key; a picture without bytes is left out.
    /// </summary>
    public static string Write(IEnumerable<Group> groups, IReadOnlyDictionary<string, byte[]>? imageFiles = null)
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
                quiz.Add(WriteQuestion(question, imageFiles));
            }
        }

        var document = new XDocument(new XDeclaration("1.0", "UTF-8", null), quiz);
        return document.Declaration + Environment.NewLine + document;
    }

    /// <summary>A category path from section names (root first); a "/" inside a name is doubled, as in Moodle.</summary>
    public static string CategoryPath(IEnumerable<string> names)
        => "$course$/top/" + string.Join("/", names.Select(n => n.Replace("/", "//")));

    private static XElement WriteQuestion(Question question, IReadOnlyDictionary<string, byte[]>? imageFiles)
    {
        var options = question.Options.OrderBy(o => o.Order).ToList();
        var element = new XElement("question", new XAttribute("type", MoodleType(question.Type)),
            // A code question's text is its title (the name) and its statement is the question text
            new XElement("name", new XElement("text", question.Type == QuestionType.Code
                ? Shorten(question.Text, MaxCodeNameLength) : Name(question.Text))),
            QuestionText(question, imageFiles),
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

            case QuestionType.Code:
                AddCodeRunner(element, question);
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
        QuestionType.Code => "coderunner",
        _ => "multichoice"
    };

    /// <summary>The CodeRunner type a code question is written as, by language.</summary>
    public static string CodeRunnerType(string? language) => language == CodeRunning.CodeLanguages.CSharp ? "csharp_program" : "python3";

    // The CodeRunner fields: the type, scoring and starting code, everything else inherited from the prototype
    // (empty), and the tests (input and expected output only)
    private static void AddCodeRunner(XElement element, Question question)
    {
        element.Add(
            new XElement("idnumber", string.Empty),
            new XElement("coderunnertype", CodeRunnerType(question.CodeLanguage)),
            new XElement("prototypetype", "0"),
            new XElement("allornothing", question.AllOrNothing ? "1" : "0"),
            // Every wrong check costs the same share: "10, 20, ..." (CodeRunner carries the last step on)
            new XElement("penaltyregime", question.CheckPenaltyPercent == 0
                ? "0"
                : $"{question.CheckPenaltyPercent}, {Math.Min(100, 2 * question.CheckPenaltyPercent)}, ..."),
            new XElement("precheck", "0"),
            new XElement("hidecheck", "0"),
            new XElement("showsource", "0"),
            new XElement("answerboxlines", "18"),
            new XElement("answerboxcolumns", "100"),
            new XElement("answerpreload", question.AnswerPreload ?? string.Empty),
            new XElement("globalextra", string.Empty),
            new XElement("useace", string.Empty),
            new XElement("resultcolumns", string.Empty),
            new XElement("template", string.Empty),
            new XElement("iscombinatortemplate", string.Empty),
            new XElement("allowmultiplestdins", string.Empty),
            new XElement("answer", string.Empty),
            new XElement("validateonsave", "0"),
            new XElement("testsplitterre", string.Empty),
            new XElement("language", string.Empty),
            new XElement("acelang", string.Empty),
            new XElement("sandbox", string.Empty),
            new XElement("grader", string.Empty),
            new XElement("cputimelimitsecs", string.Empty),
            new XElement("memlimitmb", string.Empty),
            new XElement("sandboxparams", string.Empty),
            new XElement("templateparams", string.Empty),
            new XElement("hoisttemplateparams", "0"),
            new XElement("templateparamslang", "twig"),
            new XElement("templateparamsevalpertry", "0"),
            new XElement("templateparamsevald", string.Empty),
            new XElement("twigall", "0"),
            new XElement("uiplugin", string.Empty),
            new XElement("uiparameters", string.Empty),
            new XElement("attachments", "0"),
            new XElement("attachmentsrequired", "0"),
            new XElement("maxfilesize", "0"),
            new XElement("filenamesregex", string.Empty),
            new XElement("filenamesexplain", string.Empty),
            new XElement("displayfeedback", "1"),
            new XElement("giveupallowed", "0"),
            new XElement("prototypeextra", string.Empty),
            new XElement("testcases", question.TestCases.OrderBy(t => t.Order).Select(t => new XElement("testcase",
                new XAttribute("testtype", "0"),
                new XAttribute("useasexample", t.IsExample ? "1" : "0"),
                new XAttribute("hiderestiffail", "0"),
                new XAttribute("mark", Number(t.Weight)),
                new XElement("testcode", new XElement("text", string.Empty)),
                new XElement("stdin", new XElement("text", t.Stdin)),
                new XElement("expected", new XElement("text", t.Expected)),
                new XElement("extra", new XElement("text", string.Empty)),
                new XElement("display", new XElement("text", t.IsHidden ? "HIDE" : "SHOW"))))));
    }

    // An answer whose text is plain (not HTML): true/false, short answer, numerical
    private static XElement PlainAnswer(string text, decimal fraction)
        => new("answer", new XAttribute("fraction", Fraction(fraction)), new XAttribute("format", "moodle_auto_format"),
            new XElement("text", text),
            new XElement("feedback", new XAttribute("format", "html"), new XElement("text", string.Empty)));

    // The question text (the task of a code question) as HTML, with its pictures: an <img> in place of each
    // marker, the unplaced ones at the end, and a <file> with the bytes of each picture that is written
    private static XElement QuestionText(Question question, IReadOnlyDictionary<string, byte[]>? imageFiles)
    {
        var text = question.Type == QuestionType.Code && !string.IsNullOrEmpty(question.Statement)
            ? question.Statement
            : question.Text;

        // Only the pictures whose bytes are known; each gets a file name unique within the question
        var images = question.Images
            .Where(i => imageFiles != null && imageFiles.ContainsKey(i.StorageKey))
            .OrderBy(i => i.Number)
            .ToList();
        var names = new Dictionary<int, string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var image in images)
        {
            var name = used.Add(image.FileName) ? image.FileName : $"{image.Number}-{image.FileName}";
            used.Add(name);
            names[image.Number] = name;
        }

        var html = new System.Text.StringBuilder();
        foreach (var part in QuestionImageMarkers.Split(text, images))
        {
            html.Append(part.Image == null ? Html(part.Text!, paragraph: false) : Img(part.Image, names[part.Image.Number]));
        }

        foreach (var image in QuestionImageMarkers.Unplaced(images, question.Text, question.Statement))
        {
            html.Append("<br>").Append(Img(image, names[image.Number]));
        }

        var element = new XElement("questiontext", new XAttribute("format", "html"),
            new XElement("text", new XCData($"<p>{html}</p>")));
        foreach (var image in images)
        {
            element.Add(new XElement("file",
                new XAttribute("name", names[image.Number]),
                new XAttribute("path", "/"),
                new XAttribute("encoding", "base64"),
                Convert.ToBase64String(imageFiles![image.StorageKey])));
        }

        return element;
    }

    private static string Img(QuestionImage image, string fileName)
        => $"<img src=\"@@PLUGINFILE@@/{Uri.EscapeDataString(fileName)}\" alt=\"{WebUtility.HtmlEncode(image.AltText ?? string.Empty)}\">";

    // The question's name in the bank: the start of its text on one line (a picture marker as "[изображение]")
    private static string Name(string text)
    {
        var line = string.Join(' ', QuestionImageMarkers.ToPlainText(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
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
