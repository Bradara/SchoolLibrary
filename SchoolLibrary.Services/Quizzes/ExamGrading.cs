using System.Globalization;
using SchoolLibrary.Data.Enums;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// How an exam's points become a grade on the 2–6 scale: a lowest percentage for each grade (a whole grade), or a
/// formula with two decimals (points / maximum × 6, or 2 + 4 × points / maximum). With a formula, anything below
/// 3,00 is a 2; the whole grade rounds half up (3,50 → 4), as in the school grades Среден 3,00–3,49,
/// Добър 3,50–4,49, Много добър 4,50–5,49 and Отличен 5,50–6,00. Pure logic.
/// </summary>
/// <param name="Three">The lowest percentage (0–100) for a 3; below it the grade is a 2. Used by thresholds only.</param>
public record ExamGrading(GradingMethod Method, decimal Three, decimal Four, decimal Five, decimal Six)
{
    /// <summary>The scale every quiz had before scales could be chosen: 30, 50, 70 and 85%.</summary>
    public static ExamGrading Default { get; } = new(GradingMethod.Thresholds, QuizGradeScale.ThresholdThree,
        QuizGradeScale.ThresholdFour, QuizGradeScale.ThresholdFive, QuizGradeScale.ThresholdSix);

    /// <summary>A formula (the thresholds of <see cref="Default"/> are kept but not used).</summary>
    public static ExamGrading Formula(GradingMethod method)
        => Default with { Method = method };

    /// <summary>
    /// The grading of a class exam (its method and, with thresholds, its scale or the system one); the system scale
    /// for anything else (practice, older exams). The exam's scale is loaded with it (auto-included).
    /// </summary>
    public static ExamGrading For(SchoolLibrary.Data.Models.ExamAssignment? exam)
    {
        if (exam == null)
        {
            return Default;
        }

        if (exam.GradingMethod != GradingMethod.Thresholds)
        {
            return Formula(exam.GradingMethod);
        }

        var scale = exam.GradeScale;
        return scale == null ? Default : new ExamGrading(GradingMethod.Thresholds, scale.Three, scale.Four, scale.Five, scale.Six);
    }

    /// <summary>The name of a method as the teacher sees it.</summary>
    public static string MethodName(GradingMethod method) => method switch
    {
        GradingMethod.TimesSix => "точки / максимум × 6",
        GradingMethod.TwoPlusFour => "2 + 4 × точки / максимум",
        _ => "по скала"
    };

    /// <summary>Whether the grade has decimals (the formulas) rather than being a whole number.</summary>
    public bool HasDecimals => Method != GradingMethod.Thresholds;

    /// <summary>
    /// The grade for a score: a whole number with thresholds; with a formula two decimals, 2 below 3,00 and at
    /// most 6. Nothing to grade (maximum 0) is a 2.
    /// </summary>
    public decimal Mark(decimal score, decimal maxScore)
    {
        if (maxScore <= 0)
        {
            return 2m;
        }

        var share = Math.Clamp(score / maxScore, 0m, 1m);
        if (Method == GradingMethod.Thresholds)
        {
            var percent = share * 100m;
            if (percent >= Six) return 6m;
            if (percent >= Five) return 5m;
            if (percent >= Four) return 4m;
            if (percent >= Three) return 3m;
            return 2m;
        }

        var raw = Method == GradingMethod.TimesSix ? share * 6m : 2m + 4m * share;
        var mark = Math.Round(raw, 2, MidpointRounding.AwayFromZero);
        return mark < 3m ? 2m : Math.Min(mark, 6m);
    }

    /// <summary>The whole grade 2–6 for a score (a formula's grade rounded half up).</summary>
    public int Grade(decimal score, decimal maxScore) => Whole(Mark(score, maxScore));

    /// <summary>The whole grade of a mark: 3,49 → 3, 3,50 → 4.</summary>
    public static int Whole(decimal mark) => (int)Math.Round(mark, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The grade as shown: "5" with thresholds, "4,37" with a formula (a 2 is always "2"). The comma is fixed, so
    /// the output does not depend on the server culture.
    /// </summary>
    public string Format(decimal mark)
        => !HasDecimals || mark < 3m
            ? Whole(mark).ToString(CultureInfo.InvariantCulture)
            : mark.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>The grade with its name, e.g. "Много добър 4,50" or "Отличен 6".</summary>
    public string Describe(decimal mark) => $"{QuizGradeScale.Label(Whole(mark))} {Format(mark)}";

    /// <summary>
    /// Why the thresholds cannot be used, or null when they can: each between 0 and 100, with at most two decimals,
    /// and each higher than the one before (3 &lt; 4 &lt; 5 &lt; 6). Formulas do not use them.
    /// </summary>
    public static string? ValidateThresholds(decimal three, decimal four, decimal five, decimal six)
    {
        var all = new[] { three, four, five, six };
        if (all.Any(t => t < 0m || t > 100m))
        {
            return "Праговете трябва да са между 0 и 100%.";
        }

        if (all.Any(t => Math.Round(t, 2) != t))
        {
            return "Праговете могат да имат най-много 2 знака след запетаята.";
        }

        return three < four && four < five && five < six
            ? null
            : "Всеки праг трябва да е по-висок от предишния (за 3 < за 4 < за 5 < за 6).";
    }
}
