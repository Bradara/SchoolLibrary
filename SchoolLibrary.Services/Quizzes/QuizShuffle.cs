using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>Random ordering of quiz questions and answer options.</summary>
public static class QuizShuffle
{
    /// <summary>Returns the items in random order (Fisher–Yates), leaving the source untouched.</summary>
    public static List<T> Shuffle<T>(IEnumerable<T> items, Random rng)
    {
        var list = items.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }

    /// <summary>Replaces the question's options with the same options in random order.</summary>
    public static void ShuffleOptions(Question question, Random rng)
        => question.Options = Shuffle(question.Options, rng);

    /// <summary>
    /// Shuffles the options of an exam attempt's question with a seed taken from the attempt token and
    /// the question id, so the student sees the same order every time the exam page is reloaded.
    /// </summary>
    public static void ShuffleOptionsForAttempt(Question question, Guid attemptToken)
        => ShuffleOptions(question, new Random(SeedFor(attemptToken, question.Id)));

    /// <summary>A stable seed for one question within one attempt.</summary>
    public static int SeedFor(Guid attemptToken, int questionId)
    {
        var bytes = attemptToken.ToByteArray();
        var seed = 17;
        for (var i = 0; i < bytes.Length; i += 4)
        {
            seed = unchecked(seed * 31 + BitConverter.ToInt32(bytes, i));
        }

        return unchecked(seed * 31 + questionId);
    }
}
