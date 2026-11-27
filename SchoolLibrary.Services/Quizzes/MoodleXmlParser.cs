using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// Parses the Moodle XML quiz format. Supports:
/// <list type="bullet">
/// <item><c>multichoice</c> with <c>&lt;single&gt;true&lt;/single&gt;</c>, read as single-choice: the correct
/// option is the answer whose <c>fraction</c> is the highest positive value;</item>
/// <item><c>multichoice</c> with <c>&lt;single&gt;false&lt;/single&gt;</c>, read as multiple-choice, keeping
/// each answer's <c>fraction</c>;</item>
/// <item><c>truefalse</c>, read as true/false with the fixed options "Вярно"/"Невярно";</item>
/// <item><c>shortanswer</c>: the accepted answers with their <c>fraction</c>, and <c>usecase</c>;</item>
/// <item><c>numerical</c>: the accepted values (or <c>*</c>) with their <c>fraction</c> and <c>tolerance</c>;
/// units are ignored;</item>
/// <item><c>matching</c>: each <c>subquestion</c> with its answer; one with empty text is an extra wrong answer;</item>
/// <item><c>gapselect</c> and <c>ddwtos</c>, read as matching: each gap <c>[[N]]</c> becomes the item "Празнина [N]"
/// (and "[N]" in the text) whose answer is the N-th <c>selectoption</c> / <c>dragbox</c>; choices no gap uses are
/// extra wrong answers. The groups are not kept: every gap offers all the choices;</item>
/// <item><c>description</c>: text shown between the questions, without points;</item>
/// <item><c>coderunner</c> of the types in <see cref="CodeRunnerTypes"/> whose tests give only an input
/// (<c>stdin</c>) and an expected output: the tests (without the precheck-only ones), <c>useasexample</c>,
/// <c>display</c> (HIDE and HIDE_IF_FAIL are hidden), <c>mark</c>, <c>allornothing</c> and <c>answerpreload</c>.
/// Prototypes, questions with their own template and tests with test code are skipped.</item>
/// </list>
/// A question's points come from <c>defaultgrade</c> (rounded, between 1 and 100; 1 when missing).
/// Other question types (and the <c>category</c> marker) are skipped, each with a reported reason.
/// </summary>
public static class MoodleXmlParser
{
    private static readonly Regex HtmlTag = new("<[^>]+>", RegexOptions.Compiled);

    // Tags that separate words (blocks, line breaks, table cells). Other tags (span, b, i, sub, a, ...) sit
    // inside a word as often as between words ("O<span>(</span>n", "<b>най</b>-лошия"), so they leave nothing.
    private static readonly Regex HtmlBlockTag = new(
        @"</?(p|div|br|hr|li|ul|ol|dl|dt|dd|table|thead|tbody|tfoot|tr|td|th|caption|h[1-6]|pre|blockquote|section|article|header|footer|figure|figcaption)\b[^>]*>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // A gap in a gapselect / ddwtos question: [[1]], [[2]], ... (picture markers are [[img:N]], so they never match)
    private static readonly Regex GapMarker = new(@"\[\[(\d+)\]\]", RegexOptions.Compiled);

    private static readonly Regex HtmlLineBreak = new(@"<br\s*/?>|</(p|div|pre|li|h[1-6]|tr)>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>CodeRunner question types that can be imported → the language they become.</summary>
    public static readonly IReadOnlyDictionary<string, string> CodeRunnerTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["python3"] = CodeRunning.CodeLanguages.Python,
        ["python3_w_input"] = CodeRunning.CodeLanguages.Python,
        ["csharp_program"] = CodeRunning.CodeLanguages.CSharp,
    };

    /// <summary>
    /// The language of a CodeRunner type: a known one (<see cref="CodeRunnerTypes"/>), or a teacher's own prototype
    /// whose name tells the language ("C_sharp_using_python", "csharp_console" → C#; "python3_…" → Python 3).
    /// Multi-language prototypes are left out (the student picks the language there). Null when not supported.
    /// </summary>
    public static string? CodeRunnerLanguage(string? codeRunnerType)
    {
        var type = (codeRunnerType ?? string.Empty).Trim();
        if (type.Length == 0 || type.StartsWith("multilanguage", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (CodeRunnerTypes.TryGetValue(type, out var known))
        {
            return known;
        }

        var name = type.ToLowerInvariant();
        if (name.Contains("csharp") || name.Contains("c_sharp") || name.Contains("c#"))
        {
            return CodeRunning.CodeLanguages.CSharp;
        }

        return name.StartsWith("python3") ? CodeRunning.CodeLanguages.Python : null;
    }

    public static ImportParseResult Parse(string? text)
    {
        var questions = new List<QuestionInput>();
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return new ImportParseResult(questions, errors);
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(text);
        }
        catch (System.Xml.XmlException ex)
        {
            errors.Add($"Невалиден XML: {ex.Message}");
            return new ImportParseResult(questions, errors);
        }

        var number = 0;
        foreach (var question in document.Descendants("question"))
        {
            var type = (string?)question.Attribute("type");

            // The category marker and other non-question entries carry no answerable content
            if (string.Equals(type, "category", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            number++;
            var label = Describe(question, number);

            // The pictures embedded in the question text become [[img:N]] markers in its HTML
            var (questionHtml, images) = MoodleImages.Extract(question.Element("questiontext"), label, errors);

            void Add(QuestionInput input) => questions.Add(images.Count == 0 ? input : input with { Images = images });

            if (!string.Equals(type, "multichoice", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(type, "truefalse", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(type, "shortanswer", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(type, "numerical", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(type, "matching", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(type, "description", StringComparison.OrdinalIgnoreCase)
                && GapChoiceElement(type) == null
                && !string.Equals(type, "coderunner", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{label}: типът „{type}“ не се поддържа.");
                continue;
            }

            if (string.Equals(type, "coderunner", StringComparison.OrdinalIgnoreCase))
            {
                var (code, error) = ParseCodeRunner(question, questionHtml);
                if (code == null)
                {
                    errors.Add($"{label}: {error}");
                }
                else
                {
                    Add(code);
                }

                continue;
            }

            // Gaps in the order they first appear in the text; in the text each one is shown as [N]
            var gapChoiceElement = GapChoiceElement(type);
            var gaps = new List<int>();
            if (gapChoiceElement != null)
            {
                gaps = GapMarker.Matches(questionHtml).Select(m => int.Parse(m.Groups[1].Value)).Distinct().ToList();
                questionHtml = GapMarker.Replace(questionHtml, m => $"[{m.Groups[1].Value}]");
            }

            var isMultiple = string.Equals(type, "multichoice", StringComparison.OrdinalIgnoreCase)
                && string.Equals(CleanText(question.Element("single")), "false", StringComparison.OrdinalIgnoreCase);

            var questionText = CleanHtml(questionHtml);
            if (questionText.Length == 0)
            {
                errors.Add($"{label}: липсва текст на въпроса.");
                continue;
            }

            if (string.Equals(type, "description", StringComparison.OrdinalIgnoreCase))
            {
                Add(QuestionInput.Description(questionText));
                continue;
            }

            var answers = question.Elements("answer").ToList();
            var points = Points(question);

            if (string.Equals(type, "truefalse", StringComparison.OrdinalIgnoreCase))
            {
                var answer = TrueFalseAnswer(answers);
                if (answer == null)
                {
                    errors.Add($"{label}: няма посочен верен отговор.");
                    continue;
                }

                Add(QuestionInput.TrueFalse(questionText, answer, points));
                continue;
            }

            if (string.Equals(type, "shortanswer", StringComparison.OrdinalIgnoreCase))
            {
                // The accepted answers with their shares; blank answers are dropped
                var accepted = answers
                    .Select(a => (Text: CleanText(a.Element("text")), Fraction: Fraction(a)))
                    .Where(a => a.Text.Length > 0)
                    .ToList();
                if (!accepted.Any(a => a.Fraction >= AnswerOption.FullCredit))
                {
                    errors.Add($"{label}: няма приемлив отговор за всички точки (100%).");
                    continue;
                }

                var caseSensitive = CleanText(question.Element("usecase")) == "1";
                Add(new QuestionInput(questionText, accepted.Select(a => a.Text).ToList(), -1, points,
                    QuestionType.ShortAnswer, accepted.Select(a => a.Fraction).ToList(), caseSensitive));
                continue;
            }

            if (string.Equals(type, "matching", StringComparison.OrdinalIgnoreCase))
            {
                // Each <subquestion> is an item (its <text>) with its answer (<answer><text>); an item with
                // empty text is an extra wrong answer, and rows without an answer are dropped
                var pairs = question.Elements("subquestion")
                    .Select(s => (Item: CleanText(s.Element("text")), Answer: CleanText(s.Element("answer")?.Element("text"))))
                    .Where(p => p.Answer.Length > 0)
                    .ToList();
                if (pairs.Count(p => p.Item.Length > 0) < 2)
                {
                    errors.Add($"{label}: нужни са поне 2 двойки за съпоставяне.");
                    continue;
                }

                Add(QuestionInput.Matching(questionText, pairs, points));
                continue;
            }

            if (gapChoiceElement != null)
            {
                var pairs = GapPairs(question, gapChoiceElement, gaps, out var error);
                if (pairs == null)
                {
                    errors.Add($"{label}: {error}");
                    continue;
                }

                Add(QuestionInput.Matching(questionText, pairs, points));
                continue;
            }

            if (string.Equals(type, "numerical", StringComparison.OrdinalIgnoreCase))
            {
                // The accepted values with their shares and tolerances; units are not supported and are ignored
                var accepted = answers
                    .Select(a => (Text: CleanText(a.Element("text")), Fraction: Fraction(a),
                        Tolerance: NumericAnswer.Parse(CleanText(a.Element("tolerance"))) ?? 0m))
                    .Where(a => a.Text.Length > 0)
                    .ToList();
                if (!accepted.Any(a => a.Fraction >= AnswerOption.FullCredit))
                {
                    errors.Add($"{label}: няма приемлив отговор за всички точки (100%).");
                    continue;
                }

                if (accepted.Any(a => a.Text != NumericAnswer.AnyNumber && NumericAnswer.Parse(a.Text) == null))
                {
                    errors.Add($"{label}: приемливите отговори трябва да са числа.");
                    continue;
                }

                Add(new QuestionInput(questionText, accepted.Select(a => a.Text).ToList(), -1, points,
                    QuestionType.Numerical, accepted.Select(a => a.Fraction).ToList(),
                    Tolerances: accepted.Select(a => (decimal?)Math.Abs(a.Tolerance)).ToList()));
                continue;
            }

            var options = answers.Select(a => CleanText(a.Element("text"))).ToList();
            if (options.Count(o => o.Length > 0) < 2)
            {
                errors.Add($"{label}: нужни са поне 2 отговора.");
                continue;
            }

            if (isMultiple)
            {
                // Keep each answer's share as given in the file; validation checks that one is positive
                var fractions = answers.Select(Fraction).ToList();
                if (!fractions.Any(f => f > 0))
                {
                    errors.Add($"{label}: няма посочен верен отговор.");
                    continue;
                }

                Add(new QuestionInput(questionText, options, -1, points, QuestionType.MultipleChoice, fractions));
                continue;
            }

            var correctIndex = BestAnswerIndex(answers);
            if (correctIndex < 0)
            {
                errors.Add($"{label}: няма посочен верен отговор.");
                continue;
            }

            Add(new QuestionInput(questionText, options, correctIndex, points));
        }

        return new ImportParseResult(questions, errors);
    }

    // The element that holds the choices of a gap question type; null for the other types
    private static string? GapChoiceElement(string? type)
        => type?.ToLowerInvariant() switch
        {
            "gapselect" => "selectoption",
            "ddwtos" => "dragbox",
            _ => null
        };

    // The matching pairs of a gap question: gap N → the N-th choice, then the unused choices as extra wrong
    // answers; null with the reason when a gap has no choice or there are fewer than 2 gaps
    private static List<(string Item, string Answer)>? GapPairs(XElement question, string choiceElement,
        IReadOnlyList<int> gaps, out string? error)
    {
        var choices = question.Elements(choiceElement).Select(c => CleanText(c.Element("text"))).ToList();
        error = null;

        if (gaps.Count < 2)
        {
            error = "нужни са поне 2 празнини [[N]] в текста.";
            return null;
        }

        var missing = gaps.Where(g => g < 1 || g > choices.Count || choices[g - 1].Length == 0).ToList();
        if (missing.Count > 0)
        {
            error = $"няма отговор за празнина {string.Join(", ", missing.Select(g => $"[[{g}]]"))}.";
            return null;
        }

        var pairs = gaps.Select(g => ($"Празнина [{g}]", choices[g - 1])).ToList();
        pairs.AddRange(choices
            .Where((choice, index) => choice.Length > 0 && !gaps.Contains(index + 1))
            .Select(choice => (string.Empty, choice)));
        return pairs;
    }

    // A CodeRunner question that runs a whole program with each test's input; null with the reason otherwise
    private static (QuestionInput? Question, string? Error) ParseCodeRunner(XElement question, string questionHtml)
    {
        if (CleanText(question.Element("prototypetype")) is var prototype && prototype.Length > 0 && prototype != "0")
        {
            return (null, "прототипите на CodeRunner не се внасят.");
        }

        var codeRunnerType = CleanText(question.Element("coderunnertype"));
        var language = CodeRunnerLanguage(codeRunnerType);
        if (language == null)
        {
            return (null, $"CodeRunner типът „{codeRunnerType}“ не се поддържа (поддържат се {string.Join(", ", CodeRunnerTypes.Keys)} и собствени прототипи за C# или Python 3 с вход и изход).");
        }

        if (RawText(question.Element("template")).Trim().Length > 0)
        {
            return (null, "въпроси със собствен шаблон (template) не се поддържат.");
        }

        // The question text is the task; the question's name is its short title (or the task's first line)
        var statement = MultilineHtml(questionHtml);
        var name = CleanText(question.Element("name")?.Element("text"));
        var text = name.Length > 0 ? name : statement.Split('\n')[0];
        if (text.Length == 0)
        {
            return (null, "липсва текст на въпроса.");
        }

        if (text.Length > QuizService.QuestionMaxLength)
        {
            text = text[..QuizService.QuestionMaxLength];
        }

        var tests = new List<CodeTestInput>();
        foreach (var test in question.Element("testcases")?.Elements("testcase") ?? Enumerable.Empty<XElement>())
        {
            // testtype 1 = run only when the student asks for a precheck; such tests do not count for the grade
            if ((string?)test.Attribute("testtype") == "1")
            {
                continue;
            }

            if (RawText(test.Element("testcode")?.Element("text")).Trim().Length > 0)
            {
                return (null, "тестове с код (testcode) не се поддържат, а само вход (stdin) и очакван изход.");
            }

            var display = CleanText(test.Element("display")?.Element("text")).ToUpperInvariant();
            var weight = decimal.TryParse((string?)test.Attribute("mark"), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var mark) ? Math.Round(mark, 2, MidpointRounding.AwayFromZero) : 1m;
            tests.Add(new CodeTestInput(
                RawText(test.Element("stdin")?.Element("text")),
                RawText(test.Element("expected")?.Element("text")),
                IsExample: (string?)test.Attribute("useasexample") == "1",
                IsHidden: display is "HIDE" or "HIDE_IF_FAIL",
                Weight: weight));
        }

        var preload = RawText(question.Element("answerpreload"));
        var allOrNothing = CleanText(question.Element("allornothing")) != "0";
        return (QuestionInput.CodeQuestion(text, language, tests, preload.Trim().Length > 0 ? preload : null,
            allOrNothing, Points(question), statement == text ? null : statement, CheckPenalty(question)), null);
    }

    // The penalty per wrong check in percent: the first step of CodeRunner's regime ("10, 20, ..." → 10), else the
    // question's <penalty> as a fraction (0.1 → 10), else CodeRunner's default of 10
    private static int CheckPenalty(XElement question)
    {
        var regime = Regex.Match(CleanText(question.Element("penaltyregime")), @"^\s*(\d+(\.\d+)?)");
        if (regime.Success && decimal.TryParse(regime.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var step))
        {
            return (int)Math.Clamp(Math.Round(step, MidpointRounding.AwayFromZero), 0m, 100m);
        }

        if (decimal.TryParse(CleanText(question.Element("penalty")), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var fraction))
        {
            return (int)Math.Clamp(Math.Round(fraction * 100m, MidpointRounding.AwayFromZero), 0m, 100m);
        }

        return 10;
    }

    // Text as written (inputs, outputs and code): only the line endings unified
    private static string RawText(XElement? element)
        => (element?.Value ?? string.Empty).Replace("\r\n", "\n");

    // HTML question text with its line breaks kept: tags removed, entities decoded, spaces collapsed per line
    private static string MultilineHtml(string html)
    {
        var withBreaks = HtmlLineBreak.Replace(html.Replace("\r\n", "\n"), "\n");
        var decoded = WebUtility.HtmlDecode(StripTags(withBreaks));
        var lines = decoded.Split('\n')
            .Select(line => Regex.Replace(line, @"[ \t\u00A0]+", " ").Trim())
            .ToList();
        return Regex.Replace(string.Join('\n', lines), @"\n{3,}", "\n\n").Trim();
    }

    // The question's points from <defaultgrade> ("1.0000000" in Moodle's own files), rounded and kept within
    // 1..100; a missing or unreadable value means 1
    private static int Points(XElement question)
    {
        var raw = CleanText(question.Element("defaultgrade"));
        if (!decimal.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var grade))
        {
            return 1;
        }

        var rounded = Math.Round(grade, MidpointRounding.AwayFromZero);
        return (int)Math.Clamp(rounded, 1m, QuizService.MaxQuestionPoints);
    }

    // The correct option is the answer with the highest positive fraction (100 for a single-choice key)
    private static int BestAnswerIndex(List<XElement> answers)
    {
        var best = -1;
        var bestFraction = 0d;
        for (var i = 0; i < answers.Count; i++)
        {
            var raw = (string?)answers[i].Attribute("fraction");
            if (double.TryParse(raw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var fraction)
                && fraction > bestFraction)
            {
                bestFraction = fraction;
                best = i;
            }
        }

        return best;
    }

    // An answer's share in percent, rounded to the two decimals that are stored (33.33333 → 33.33);
    // a missing or unreadable value counts as 0
    private static decimal Fraction(XElement answer)
    {
        var raw = (string?)answer.Attribute("fraction");
        return decimal.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var fraction)
            ? Math.Round(fraction, 2, MidpointRounding.AwayFromZero)
            : 0m;
    }

    // Moodle writes the two answers as "true" and "false"; the correct value is the one with the best
    // fraction. When the texts are something else, the first answer is taken to mean "true".
    private static bool? TrueFalseAnswer(List<XElement> answers)
    {
        var best = BestAnswerIndex(answers);
        if (best < 0)
        {
            return null;
        }

        var text = CleanText(answers[best].Element("text"));
        if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return best == 0;
    }

    private static string Describe(XElement question, int number)
    {
        var name = CleanText(question.Element("name")?.Element("text"));
        return name.Length > 0 ? $"„{name}“" : $"Въпрос {number}";
    }

    // Moodle wraps text in <text> (often CDATA with HTML); strip tags and decode entities
    private static string CleanText(XElement? element)
        => element == null ? string.Empty : CleanHtml(element.Value);

    // Block tags become a space, the inline ones disappear
    private static string StripTags(string html)
        => HtmlTag.Replace(HtmlBlockTag.Replace(html, " "), string.Empty);

    private static string CleanHtml(string html)
    {
        var stripped = StripTags(html);
        var decoded = WebUtility.HtmlDecode(stripped);
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }
}
