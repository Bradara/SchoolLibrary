using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Tests.Services;

/// <summary>Deleting questions or a quiz removes the pictures and their stored files.</summary>
public class QuestionImageCleanupTests
{
    private const string Owner = "teacher-1";

    private readonly ApplicationDbContext _context = TestDbContextFactory.Create();
    private readonly InMemoryFileStorage _storage = new();

    private QuizService Quizzes() => new(_context, new QuizItemService(_context), storage: _storage);
    private QuestionBankService Bank() => new(_context, _storage);

    private static Question NewQuestion(string text = "Въпрос") => new()
    {
        Text = text,
        Type = QuestionType.SingleChoice,
        Options = { new AnswerOption { Text = "Да", Fraction = 100 }, new AnswerOption { Text = "Не" } }
    };

    // Adds a picture row and its stored file; the key is given so copies can share it
    private string AddImage(Question question, string? key = null, int number = 1)
    {
        key ??= QuestionImageFiles.NewKey(".png");
        _context.QuestionImages.Add(new QuestionImage
        {
            QuestionId = question.Id, Number = number, StorageKey = key,
            FileName = "image.png", ContentType = "image/png", SizeBytes = 8
        });
        _context.SaveChanges();
        _storage.Files[key] = new byte[8];
        return key;
    }

    private Question BankQuestion()
    {
        var section = new Section { Name = "Раздел", OwnerId = Owner, CreatedOn = DateTime.UtcNow };
        var question = NewQuestion();
        question.Section = section;
        _context.Questions.Add(question);
        _context.SaveChanges();
        return question;
    }

    private (Quiz Quiz, Question Question) QuizWithQuestion()
    {
        var quiz = new Quiz { Title = "Тест", OwnerId = Owner, CreatedOn = DateTime.UtcNow };
        var question = NewQuestion();
        quiz.Questions.Add(question);
        _context.Quizzes.Add(quiz);
        _context.SaveChanges();
        return (quiz, question);
    }

    [Fact]
    public async Task BankDelete_RemovesPicturesAndFiles()
    {
        var question = BankQuestion();
        var first = AddImage(question);
        var second = AddImage(question, number: 2);

        var result = await Bank().DeleteAsync(question.Id, Owner, false);

        Assert.True(result.Succeeded);
        Assert.Empty(_context.QuestionImages);
        Assert.DoesNotContain(first, _storage.Files.Keys);
        Assert.DoesNotContain(second, _storage.Files.Keys);
    }

    [Fact]
    public async Task BankDeleteMany_RemovesFilesOfDeletedOnly()
    {
        var deleted = BankQuestion();
        var kept = BankQuestion();
        var deletedKey = AddImage(deleted);
        var keptKey = AddImage(kept);

        await Bank().DeleteQuestionsAsync(new[] { deleted.Id }, Owner, false);

        Assert.DoesNotContain(deletedKey, _storage.Files.Keys);
        Assert.Contains(keptKey, _storage.Files.Keys);
        Assert.Single(_context.QuestionImages);
    }

    [Fact]
    public async Task BankDelete_FileSharedWithAnotherQuestion_IsKept()
    {
        var question = BankQuestion();
        var copy = BankQuestion();
        var key = AddImage(question);
        AddImage(copy, key);

        await Bank().DeleteAsync(question.Id, Owner, false);

        Assert.Contains(key, _storage.Files.Keys);
        Assert.Equal(copy.Id, _context.QuestionImages.Single().QuestionId);
    }

    [Fact]
    public async Task BankDelete_Refused_KeepsPictures()
    {
        var question = BankQuestion();
        var key = AddImage(question);

        var result = await Bank().DeleteAsync(question.Id, "someone-else", false);

        Assert.False(result.Succeeded);
        Assert.Contains(key, _storage.Files.Keys);
        Assert.Single(_context.QuestionImages);
    }

    [Fact]
    public async Task QuizQuestionDelete_RemovesPicturesAndFiles()
    {
        var (_, question) = QuizWithQuestion();
        var key = AddImage(question);

        var result = await Quizzes().DeleteQuestionAsync(question.Id, Owner, false);

        Assert.True(result.Succeeded);
        Assert.Empty(_context.QuestionImages);
        Assert.DoesNotContain(key, _storage.Files.Keys);
    }

    [Fact]
    public async Task QuizDelete_RemovesPicturesOfDirectQuestions_NotOfBankQuestions()
    {
        var (quiz, question) = QuizWithQuestion();
        var quizKey = AddImage(question);
        var bankKey = AddImage(BankQuestion());

        var result = await Quizzes().DeleteAsync(quiz.Id, Owner, false);

        Assert.True(result.Succeeded);
        Assert.DoesNotContain(quizKey, _storage.Files.Keys);
        Assert.Contains(bankKey, _storage.Files.Keys);
        Assert.Single(_context.QuestionImages);
    }

    [Fact]
    public async Task QuizDelete_WithoutStorage_StillRemovesRows()
    {
        var (quiz, question) = QuizWithQuestion();
        var key = AddImage(question);

        await new QuizService(_context, new QuizItemService(_context)).DeleteAsync(quiz.Id, Owner, false);

        Assert.Empty(_context.QuestionImages);
        Assert.Contains(key, _storage.Files.Keys);
    }
}
