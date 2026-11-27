using SchoolLibrary.Data.Enums;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

/// <summary>The three ways an exam's points become a 2–6 grade.</summary>
public class ExamGradingTests
{
    private static readonly ExamGrading TimesSix = ExamGrading.Formula(GradingMethod.TimesSix);
    private static readonly ExamGrading TwoPlusFour = ExamGrading.Formula(GradingMethod.TwoPlusFour);

    // ---------- Thresholds ----------

    [Theory]
    [InlineData(0, 2)]
    [InlineData(29.99, 2)]
    [InlineData(30, 3)]
    [InlineData(49.99, 3)]
    [InlineData(50, 4)]
    [InlineData(70, 5)]
    [InlineData(84.99, 5)]
    [InlineData(85, 6)]
    [InlineData(100, 6)]
    public void Default_UsesThe30_50_70_85Thresholds(double score, int expected)
    {
        Assert.Equal(expected, ExamGrading.Default.Mark((decimal)score, 100m));
        Assert.Equal(expected, ExamGrading.Default.Grade((decimal)score, 100m));
    }

    [Fact]
    public void Thresholds_CompareTheExactShare_NotARoundedPercentage()
    {
        // 2 of 3 is 66,67%: not yet the 66,67 needed for a 5
        var scale = new ExamGrading(GradingMethod.Thresholds, 33.33m, 50m, 66.67m, 90m);

        Assert.Equal(4m, scale.Mark(2m, 3m));
        Assert.Equal(3m, scale.Mark(1m, 3m));   // 33,333…% ≥ 33,33
    }

    [Fact]
    public void Thresholds_AreShownAsAWholeGrade()
    {
        Assert.Equal("5", ExamGrading.Default.Format(5m));
        Assert.Equal("Много добър 5", ExamGrading.Default.Describe(5m));
        Assert.False(ExamGrading.Default.HasDecimals);
    }

    [Fact]
    public void QuizGradeScale_IsTheDefaultScale()
    {
        Assert.Equal(ExamGrading.Default.Grade(7m, 10m), QuizGradeScale.Grade(7m, 10m));
        Assert.Equal(5, QuizGradeScale.Grade(7m, 10m));
    }

    // ---------- Formulas ----------

    [Theory]
    [InlineData(10, 6.00)]
    [InlineData(9, 5.40)]
    [InlineData(7.5, 4.50)]
    [InlineData(5.5, 3.30)]
    [InlineData(5, 3.00)]    // exactly 3,00
    [InlineData(4.99, 2)]    // 2,994 → 2,99, below 3 → a 2
    [InlineData(0, 2)]
    public void TimesSix_IsPointsOverMaximumTimesSix(double score, double expected)
    {
        Assert.Equal((decimal)expected, TimesSix.Mark((decimal)score, 10m));
    }

    [Theory]
    [InlineData(10, 6.00)]
    [InlineData(5, 4.00)]
    [InlineData(2.5, 3.00)]
    [InlineData(2.49, 3.00)] // 2,996 rounds to 3,00 before the "below 3" check
    [InlineData(2.4, 2)]     // 2,96, below 3 → a 2
    [InlineData(0, 2)]
    public void TwoPlusFour_IsTwoPlusFourTimesTheShare(double score, double expected)
    {
        Assert.Equal((decimal)expected, TwoPlusFour.Mark((decimal)score, 10m));
    }

    [Theory]
    [InlineData(3.00, 3, "Среден 3,00")]
    [InlineData(3.49, 3, "Среден 3,49")]
    [InlineData(3.50, 4, "Добър 3,50")]
    [InlineData(4.49, 4, "Добър 4,49")]
    [InlineData(4.50, 5, "Много добър 4,50")]
    [InlineData(5.49, 5, "Много добър 5,49")]
    [InlineData(5.50, 6, "Отличен 5,50")]
    [InlineData(6.00, 6, "Отличен 6,00")]
    [InlineData(2, 2, "Слаб 2")]
    public void Formula_WholeGradeAndName_FollowTheSchoolBands(double mark, int whole, string described)
    {
        Assert.Equal(whole, ExamGrading.Whole((decimal)mark));
        Assert.Equal(described, TimesSix.Describe((decimal)mark));
    }

    [Fact]
    public void Formula_HasDecimals_AndGradeRoundsHalfUp()
    {
        Assert.True(TimesSix.HasDecimals);
        Assert.Equal(4, TimesSix.Grade(5.85m, 10m));    // 3,51 → 4
        Assert.Equal(3, TimesSix.Grade(5.8m, 10m));     // 3,48 → 3
    }

    // ---------- Edge cases ----------

    [Theory]
    [InlineData(GradingMethod.Thresholds)]
    [InlineData(GradingMethod.TimesSix)]
    [InlineData(GradingMethod.TwoPlusFour)]
    public void NothingToGrade_IsATwo(GradingMethod method)
    {
        Assert.Equal(2m, ExamGrading.Formula(method).Mark(0m, 0m));
    }

    [Fact]
    public void ScoreAboveTheMaximum_IsAtMostSix()
    {
        Assert.Equal(6m, TimesSix.Mark(12m, 10m));
        Assert.Equal(6m, TwoPlusFour.Mark(12m, 10m));
    }

    // ---------- ValidateThresholds ----------

    [Fact]
    public void ValidateThresholds_RisingWithinRange_IsValid()
    {
        Assert.Null(ExamGrading.ValidateThresholds(0m, 33.33m, 66.67m, 100m));
    }

    [Theory]
    [InlineData(-1, 50, 70, 85, "Праговете трябва да са между 0 и 100%.")]
    [InlineData(30, 50, 70, 100.5, "Праговете трябва да са между 0 и 100%.")]
    [InlineData(30.125, 50, 70, 85, "Праговете могат да имат най-много 2 знака след запетаята.")]
    [InlineData(30, 30, 70, 85, "Всеки праг трябва да е по-висок от предишния (за 3 < за 4 < за 5 < за 6).")]
    [InlineData(30, 70, 50, 85, "Всеки праг трябва да е по-висок от предишния (за 3 < за 4 < за 5 < за 6).")]
    public void ValidateThresholds_Rejects(double three, double four, double five, double six, string message)
    {
        Assert.Equal(message, ExamGrading.ValidateThresholds((decimal)three, (decimal)four, (decimal)five, (decimal)six));
    }
}
