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
/// <item><c>matching</c>: each <c>subquestion</c> with its answer; one with empty text is an extra wrong answer.</item>
/// </list>
/// Other question types (and the <c>category</c> marker) are skipped, each with a reported reason.
/// </summary>
public static class MoodleXmlParser
{
    private static readonly Regex HtmlTag = new("<[^>]+>", RegexOptions.Compiled);

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

            if (!string.Equals(type, "multichoice", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(type, "truefalse", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(type, "shortanswer", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(type, "numerical", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(type, "matching", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{label}: типът „{type}“ не се поддържа.");
                continue;
            }

            var isMultiple = string.Equals(type, "multichoice", StringComparison.OrdinalIgnoreCase)
                && string.Equals(CleanText(question.Element("single")), "false", StringComparison.OrdinalIgnoreCase);

            var questionText = CleanText(question.Element("questiontext")?.Element("text"));
            if (questionText.Length == 0)
            {
                errors.Add($"{label}: липсва текст на въпроса.");
                continue;
            }

            var answers = question.Elements("answer").ToList();

            if (string.Equals(type, "truefalse", StringComparison.OrdinalIgnoreCase))
            {
                var answer = TrueFalseAnswer(answers);
                if (answer == null)
                {
                    errors.Add($"{label}: няма посочен верен отговор.");
                    continue;
                }

                questions.Add(QuestionInput.TrueFalse(questionText, answer));
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
                questions.Add(new QuestionInput(questionText, accepted.Select(a => a.Text).ToList(), -1, 1,
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

                questions.Add(QuestionInput.Matching(questionText, pairs));
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

                questions.Add(new QuestionInput(questionText, accepted.Select(a => a.Text).ToList(), -1, 1,
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

                questions.Add(new QuestionInput(questionText, options, -1, 1, QuestionType.MultipleChoice, fractions));
                continue;
            }

            var correctIndex = BestAnswerIndex(answers);
            if (correctIndex < 0)
            {
                errors.Add($"{label}: няма посочен верен отговор.");
                continue;
            }

            questions.Add(new QuestionInput(questionText, options, correctIndex));
        }

        return new ImportParseResult(questions, errors);
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
    {
        if (element == null)
        {
            return string.Empty;
        }

        var stripped = HtmlTag.Replace(element.Value, " ");
        var decoded = WebUtility.HtmlDecode(stripped);
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }
}
