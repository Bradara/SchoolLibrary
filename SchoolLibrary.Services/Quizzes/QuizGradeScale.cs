namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// The system scale: a score becomes a 2–6 grade by fixed percentage thresholds (<see cref="ExamGrading.Default"/>).
/// It grades practice quizzes and the exams that have no scale of their own.
/// </summary>
public static class QuizGradeScale
{
    /// <summary>Lowest percentage (0–100) that earns each grade; below <see cref="ThresholdThree"/> is a 2.</summary>
    public const int ThresholdThree = 30;
    public const int ThresholdFour = 50;
    public const int ThresholdFive = 70;
    public const int ThresholdSix = 85;

    /// <summary>The score as a whole-number percentage of the maximum (0 when there is nothing to grade).</summary>
    public static int Percent(decimal score, decimal maxScore)
        => maxScore <= 0 ? 0 : (int)Math.Round(score * 100m / maxScore, MidpointRounding.AwayFromZero);

    /// <summary>The 2–6 grade for a score, compared on the exact ratio (not the rounded percentage).</summary>
    public static int Grade(decimal score, decimal maxScore) => ExamGrading.Default.Grade(score, maxScore);

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
