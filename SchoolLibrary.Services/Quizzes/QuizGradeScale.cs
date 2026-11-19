namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// Turns a score into a 2–6 grade using fixed percentage thresholds. The thresholds live here as
/// constants so the scale is defined in one place and easy to adjust.
/// </summary>
public static class QuizGradeScale
{
    /// <summary>Lowest percentage (0–100) that earns each grade; below <see cref="ThresholdThree"/> is a 2.</summary>
    public const int ThresholdThree = 30;
    public const int ThresholdFour = 50;
    public const int ThresholdFive = 70;
    public const int ThresholdSix = 85;

    /// <summary>The score as a whole-number percentage of the maximum (0 when there is nothing to grade).</summary>
    public static int Percent(int score, int maxScore)
        => maxScore <= 0 ? 0 : (int)Math.Round(score * 100.0 / maxScore, MidpointRounding.AwayFromZero);

    /// <summary>The 2–6 grade for a score, compared on the exact ratio (not the rounded percentage).</summary>
    public static int Grade(int score, int maxScore)
    {
        if (maxScore <= 0)
        {
            return 2;
        }

        var percent = score * 100.0 / maxScore;
        if (percent >= ThresholdSix) return 6;
        if (percent >= ThresholdFive) return 5;
        if (percent >= ThresholdFour) return 4;
        if (percent >= ThresholdThree) return 3;
        return 2;
    }

    /// <summary>The Bulgarian label for a 2–6 grade.</summary>
    public static string Label(int grade) => grade switch
    {
        6 => "Отличен",
        5 => "Много добър",
        4 => "Добър",
        3 => "Среден",
        _ => "Слаб"
    };
}
