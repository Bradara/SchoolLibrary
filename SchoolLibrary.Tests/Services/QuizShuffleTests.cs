using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

public class QuizShuffleTests
{
    [Fact]
    public void Shuffle_KeepsEveryItemAndLeavesTheSourceUntouched()
    {
        var source = Enumerable.Range(1, 20).ToList();

        var shuffled = QuizShuffle.Shuffle(source, new Random(3));

        Assert.Equal(Enumerable.Range(1, 20), source);
        Assert.NotEqual(source, shuffled);
        Assert.Equal(source, shuffled.Order());
    }

    [Fact]
    public void Shuffle_EmptyOrSingle_ReturnsTheSame()
    {
        Assert.Empty(QuizShuffle.Shuffle(Array.Empty<int>(), new Random(1)));
        Assert.Equal(new[] { 5 }, QuizShuffle.Shuffle(new[] { 5 }, new Random(1)));
    }

    [Fact]
    public void SeedFor_IsStableAndDependsOnTokenAndQuestion()
    {
        var token = Guid.NewGuid();

        Assert.Equal(QuizShuffle.SeedFor(token, 1), QuizShuffle.SeedFor(token, 1));
        Assert.NotEqual(QuizShuffle.SeedFor(token, 1), QuizShuffle.SeedFor(token, 2));
        Assert.NotEqual(QuizShuffle.SeedFor(token, 1), QuizShuffle.SeedFor(Guid.NewGuid(), 1));
    }

    [Fact]
    public void ShuffleOptionsForAttempt_SameTokenAndQuestion_GivesTheSameOrder()
    {
        var token = Guid.NewGuid();
        Question Make() => new()
        {
            Id = 42,
            Options = Enumerable.Range(1, 8).Select(i => new AnswerOption { Id = i, Order = i }).ToList()
        };
        var a = Make();
        var b = Make();

        QuizShuffle.ShuffleOptionsForAttempt(a, token);
        QuizShuffle.ShuffleOptionsForAttempt(b, token);

        Assert.Equal(a.Options.Select(o => o.Id), b.Options.Select(o => o.Id));
        Assert.Equal(Enumerable.Range(1, 8), a.Options.Select(o => o.Id).Order());
    }
}
