using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// Parses the Moodle XML quiz format. Supports <c>multichoice</c> (with <c>&lt;single&gt;true&lt;/single&gt;</c>,
/// read as single-choice) and <c>truefalse</c> (read as true/false with the fixed options "Вярно"/"Невярно");
/// the correct option is the answer whose <c>fraction</c> is the highest positive value. Other question types (and the
/// <c>category</c> marker) are skipped, each with a reported reason.
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
                && !string.Equals(type, "truefalse", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{label}: типът „{type}“ не се поддържа (само един верен отговор).");
                continue;
            }

            if (string.Equals(type, "multichoice", StringComparison.OrdinalIgnoreCase)
                && string.Equals(CleanText(question.Element("single")), "false", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{label}: многоотговорен въпрос не се поддържа.");
                continue;
            }

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

            var options = answers.Select(a => CleanText(a.Element("text"))).ToList();
            if (options.Count(o => o.Length > 0) < 2)
            {
                errors.Add($"{label}: нужни са поне 2 отговора.");
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
