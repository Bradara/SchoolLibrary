using SchoolLibrary.Services.Discovery;

namespace SchoolLibrary.Tests.Services;

public class SearchTermsTests
{
    [Theory]
    [InlineData("фотосинтеза", "фотосинтеза")]
    [InlineData("Древен Египет", "древен|египет")]
    [InlineData("религия и култура на Древен Египет", "религия|култура|древен|египет")]
    [InlineData("Втората световна война: причини и последици!", "втората|световна|война|причини|последици")]
    [InlineData("дроби, дроби, ДРОБИ", "дроби")]
    [InlineData("математика 5 клас", "математика|клас")]
    [InlineData("the history of Rome and Greece", "history|rome|greece")]
    public void Extract_KeepsTheWordsThatNameTheTopic(string text, string expected)
    {
        Assert.Equal(expected.Split('|'), SearchTerms.Extract(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("и")]
    [InlineData("на и или")]
    [InlineData("!!! ??? ...")]
    [InlineData("a b c")]
    public void Extract_WithoutAnyWordWorthSearching_ReturnsNothing(string? text)
    {
        Assert.Empty(SearchTerms.Extract(text));
    }

    [Fact]
    public void Extract_TakesAtMostSixWords()
    {
        var terms = SearchTerms.Extract("едно две три четири пет шест седем осем");

        Assert.Equal(SearchTerms.MaxTerms, terms.Count);
        Assert.Equal("шест", terms[^1]);
    }

    [Fact]
    public void Extract_RemovesSearchOperatorsAndPunctuation()
    {
        Assert.Equal(new[] { "физика", "химия" }, SearchTerms.Extract("\"физика\" AND (химия) OR -NOT *"));
    }

    [Theory]
    [InlineData("ток", "ток")]
    [InlineData("дроб", "дроб")]
    [InlineData("дроби", "дроб")]
    [InlineData("Египет", "Египе")]
    [InlineData("войната", "война")]
    [InlineData("фотосинтеза", "фотосинте")]
    public void Stem_KeepsTheBeginningOfTheWord(string word, string expected)
    {
        Assert.Equal(expected, SearchTerms.Stem(word));
    }

    [Fact]
    public void CountMatches_CountsTermsThatStartAWordOfTheText_IgnoringCase()
    {
        var terms = SearchTerms.Extract("дроби египет ракети");

        Assert.Equal(2, SearchTerms.CountMatches("Дроби: Въведение в ЕГИПЕТСКАТА култура", terms));
        Assert.Equal(0, SearchTerms.CountMatches("Нещо съвсем друго", terms));
    }

    [Fact]
    public void CountMatches_AWordInsideAnotherWordDoesNotMatch()
    {
        // "ток" (current) must not match "поток" (stream) or the letter in the middle of words
        Assert.Equal(0, SearchTerms.CountMatches("поток от данни", new[] { "ток" }));
        Assert.Equal(1, SearchTerms.CountMatches("Постоянен ток", new[] { "ток" }));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 2)]
    [InlineData(5, 3)]
    [InlineData(6, 3)]
    public void RelaxedRequirement_IsHalfTheWords_AtLeastOne(int count, int expected)
    {
        Assert.Equal(expected, SearchTerms.RelaxedRequirement(count));
    }
}
