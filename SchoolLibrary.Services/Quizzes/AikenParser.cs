using System.Text.RegularExpressions;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// Parses the Aiken quiz format into questions. Aiken is a plain-text, single-choice format: the
/// question on its own line(s), then options like "A. ...", "B) ...", and a final "ANSWER: B" line.
/// Questions are separated by a blank line.
/// </summary>
public static class AikenParser
{
    private static readonly Regex OptionLine = new(@"^([A-Za-z])[.)]\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex AnswerLine = new(@"^ANSWER:\s*([A-Za-z])\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static ImportParseResult Parse(string? text)
    {
        var questions = new List<QuestionInput>();
        var errors = new List<string>();

        var lines = (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var block = new List<string>();
        var blockNumber = 0;

        void Flush()
        {
            if (block.Count == 0)
            {
                return;
            }

            blockNumber++;
            ParseBlock(block, blockNumber, questions, errors);
            block.Clear();
        }

        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                Flush();
            }
            else
            {
                block.Add(raw.Trim());
            }
        }
        Flush();

        return new ImportParseResult(questions, errors);
    }

    private static void ParseBlock(List<string> lines, int number, List<QuestionInput> questions, List<string> errors)
    {
        var questionLines = new List<string>();
        var options = new List<(char Letter, string Text)>();
        char? answer = null;

        foreach (var line in lines)
        {
            var answerMatch = AnswerLine.Match(line);
            if (answerMatch.Success)
            {
                answer = char.ToUpperInvariant(answerMatch.Groups[1].Value[0]);
                continue;
            }

            var optionMatch = OptionLine.Match(line);
            if (optionMatch.Success)
            {
                options.Add((char.ToUpperInvariant(optionMatch.Groups[1].Value[0]), optionMatch.Groups[2].Value.Trim()));
                continue;
            }

            // Lines before the first option make up the question text; stray text afterwards is ignored
            if (options.Count == 0)
            {
                questionLines.Add(line);
            }
        }

        var questionText = string.Join(" ", questionLines).Trim();
        if (questionText.Length == 0)
        {
            errors.Add($"Блок {number}: липсва текст на въпроса.");
            return;
        }

        if (options.Count < 2)
        {
            errors.Add($"Блок {number}: нужни са поне 2 отговора.");
            return;
        }

        if (answer == null)
        {
            errors.Add($"Блок {number}: липсва ред „ANSWER:\".");
            return;
        }

        var correctIndex = options.FindIndex(o => o.Letter == answer.Value);
        if (correctIndex < 0)
        {
            errors.Add($"Блок {number}: „ANSWER: {answer}\" сочи несъществуващ отговор.");
            return;
        }

        questions.Add(new QuestionInput(questionText, options.Select(o => o.Text).ToList(), correctIndex));
    }
}
