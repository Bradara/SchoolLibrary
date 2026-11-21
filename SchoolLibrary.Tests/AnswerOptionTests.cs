using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Tests;

public class AnswerOptionTests
{
    [Fact]
    public void SettingIsCorrect_StoresFullCreditOrZero()
    {
        var option = new AnswerOption { IsCorrect = true };
        Assert.Equal(100m, option.Fraction);

        option.IsCorrect = false;
        Assert.Equal(0m, option.Fraction);
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(50, false)]
    [InlineData(33.33, false)]
    [InlineData(0, false)]
    [InlineData(-100, false)]
    public void IsCorrect_OnlyForFullCredit(double fraction, bool expected)
    {
        var option = new AnswerOption { Fraction = (decimal)fraction };

        Assert.Equal(expected, option.IsCorrect);
    }

    [Fact]
    public void NewOption_IsNotCorrect()
    {
        Assert.False(new AnswerOption().IsCorrect);
    }
}
