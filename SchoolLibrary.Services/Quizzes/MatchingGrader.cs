using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// Grades a matching question. The points are shared equally between the items; an item earns its share when
/// the answer picked for it is the text that goes with it (two items may share the same answer). Picks for
/// unknown items and answers that are not in the list are dropped.
/// </summary>
public sealed class MatchingGrader : IQuestionGrader
{
    public QuestionType Type => QuestionType.Matching;

    public QuestionGrade Grade(Question question, SubmittedAnswer answer)
    {
        var items = MatchingAnswers.Items(question);
        var choices = MatchingAnswers.Choices(question).ToHashSet(StringComparer.Ordinal);

        // Keep only picks for this question's items that name an answer from its list
        var picked = answer.MatchedPairs
            .Where(p => items.Any(i => i.Id == p.Key) && choices.Contains(p.Value))
            .ToDictionary(p => p.Key, p => p.Value);
        if (items.Count == 0 || picked.Count == 0)
        {
            return new QuestionGrade(0m, false, SubmittedAnswer.Empty);
        }

        var correct = items.Count(i => picked.TryGetValue(i.Id, out var text) && string.Equals(text, i.MatchText, StringComparison.Ordinal));
        var share = (decimal)correct / items.Count;
        var points = Math.Round(question.Points * share, 2, MidpointRounding.AwayFromZero);
        return new QuestionGrade(points, correct == items.Count, new SubmittedAnswer(Pairs: picked));
    }
}
