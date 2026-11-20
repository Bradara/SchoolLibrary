using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Quizzes;

public class QuizItemService : IQuizItemService
{
    public const int MaxPickCount = 100;

    /// <summary>The root bank section new quiz questions land in when they are not filed elsewhere.</summary>
    public const string DefaultSectionName = "Некласифицирани";

    private readonly ApplicationDbContext _context;

    public QuizItemService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<QuizItem>> GetItemsAsync(int quizId)
        => await _context.QuizItems
            .AsNoTracking()
            .Where(i => i.QuizId == quizId)
            .OrderBy(i => i.Order)
            .Include(i => i.Question)
            .Include(i => i.Section)
            .ToListAsync();

    public async Task<ServiceResult<QuizItem>> AddFixedQuestionAsync(int quizId, int questionId, string userId, bool isAdministrator)
    {
        var guard = await EnsureManageableAsync(quizId, userId, isAdministrator);
        if (guard != null)
        {
            return guard;
        }

        if (!await _context.Questions.AnyAsync(q => q.Id == questionId))
        {
            return ServiceResult<QuizItem>.Failure(ServiceError.NotFound, "Въпросът не е намерен.");
        }

        var item = new QuizItem
        {
            QuizId = quizId,
            Kind = QuizItemKind.FixedQuestion,
            QuestionId = questionId,
            Order = await NextOrderAsync(quizId)
        };

        _context.QuizItems.Add(item);
        await _context.SaveChangesAsync();
        return ServiceResult<QuizItem>.Success(item);
    }

    public async Task<ServiceResult<QuizItem>> AddNewQuestionAsync(int quizId, QuestionInput input, string userId, bool isAdministrator)
    {
        var quiz = await _context.Quizzes.FirstOrDefaultAsync(q => q.Id == quizId);
        if (quiz == null)
        {
            return ServiceResult<QuizItem>.Failure(ServiceError.NotFound, "Тестът не е намерен.");
        }

        if (!QuizPermissions.CanManage(quiz, userId, isAdministrator))
        {
            return ServiceResult<QuizItem>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        var validation = QuizService.ValidateQuestion(input);
        if (validation != null)
        {
            return ServiceResult<QuizItem>.Failure(ServiceError.Invalid, validation);
        }

        // The question lives in the bank; use (or create) the owner's root "unclassified" section
        var bankOwner = quiz.OwnerId ?? userId;
        var section = await _context.Sections
            .FirstOrDefaultAsync(s => s.OwnerId == bankOwner && s.ParentId == null && s.Name == DefaultSectionName);
        if (section == null)
        {
            section = new Section { Name = DefaultSectionName, OwnerId = bankOwner, CreatedOn = DateTime.UtcNow };
            _context.Sections.Add(section);
            await _context.SaveChangesAsync();
        }

        var maxQuestionOrder = await _context.Questions
            .Where(q => q.SectionId == section.Id)
            .Select(q => (int?)q.Order)
            .MaxAsync() ?? 0;

        var question = new Question
        {
            SectionId = section.Id,
            Text = input.Text.Trim(),
            Type = QuestionType.SingleChoice,
            Points = input.Points,
            Order = maxQuestionOrder + 1
        };
        for (var i = 0; i < input.Options.Count; i++)
        {
            question.Options.Add(new AnswerOption
            {
                Text = input.Options[i].Trim(),
                IsCorrect = i == input.CorrectIndex,
                Order = i + 1
            });
        }
        _context.Questions.Add(question);
        await _context.SaveChangesAsync();

        var item = new QuizItem
        {
            QuizId = quizId,
            Kind = QuizItemKind.FixedQuestion,
            QuestionId = question.Id,
            Order = await NextOrderAsync(quizId)
        };
        _context.QuizItems.Add(item);
        await _context.SaveChangesAsync();

        return ServiceResult<QuizItem>.Success(item);
    }

    public async Task<ServiceResult<QuizItem>> AddRandomSectionAsync(int quizId, int sectionId, int pickCount, string userId, bool isAdministrator)
    {
        var guard = await EnsureManageableAsync(quizId, userId, isAdministrator);
        if (guard != null)
        {
            return guard;
        }

        if (!await _context.Sections.AnyAsync(s => s.Id == sectionId))
        {
            return ServiceResult<QuizItem>.Failure(ServiceError.NotFound, "Разделът не е намерен.");
        }

        if (pickCount < 1 || pickCount > MaxPickCount)
        {
            return ServiceResult<QuizItem>.Failure(ServiceError.Invalid,
                $"Броят въпроси трябва да е между 1 и {MaxPickCount}.");
        }

        var item = new QuizItem
        {
            QuizId = quizId,
            Kind = QuizItemKind.RandomFromSection,
            SectionId = sectionId,
            PickCount = pickCount,
            Order = await NextOrderAsync(quizId)
        };

        _context.QuizItems.Add(item);
        await _context.SaveChangesAsync();
        return ServiceResult<QuizItem>.Success(item);
    }

    public async Task<ServiceResult<QuizItem>> DeleteItemAsync(int itemId, string userId, bool isAdministrator)
    {
        var item = await _context.QuizItems.Include(i => i.Quiz).FirstOrDefaultAsync(i => i.Id == itemId);
        if (item == null)
        {
            return ServiceResult<QuizItem>.Failure(ServiceError.NotFound, "Елементът не е намерен.");
        }

        if (!QuizPermissions.CanManage(item.Quiz, userId, isAdministrator))
        {
            return ServiceResult<QuizItem>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        _context.QuizItems.Remove(item);
        await _context.SaveChangesAsync();
        return ServiceResult<QuizItem>.Success(item);
    }

    public async Task<ServiceResult<QuizItem>> MoveItemAsync(int itemId, bool up, string userId, bool isAdministrator)
    {
        var item = await _context.QuizItems.Include(i => i.Quiz).FirstOrDefaultAsync(i => i.Id == itemId);
        if (item == null)
        {
            return ServiceResult<QuizItem>.Failure(ServiceError.NotFound, "Елементът не е намерен.");
        }

        if (!QuizPermissions.CanManage(item.Quiz, userId, isAdministrator))
        {
            return ServiceResult<QuizItem>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        var siblings = await _context.QuizItems
            .Where(i => i.QuizId == item.QuizId)
            .OrderBy(i => i.Order)
            .ToListAsync();

        var index = siblings.FindIndex(i => i.Id == itemId);
        var swapWith = up ? index - 1 : index + 1;
        if (swapWith >= 0 && swapWith < siblings.Count)
        {
            var other = siblings[swapWith];
            (item.Order, other.Order) = (other.Order, item.Order);
            await _context.SaveChangesAsync();
        }

        return ServiceResult<QuizItem>.Success(item);
    }

    public async Task<IReadOnlyList<Question>> ResolveAsync(int quizId, Random? rng = null)
    {
        rng ??= Random.Shared;

        // Questions attached directly to the quiz come first, then the bank items
        var resolved = await _context.Questions
            .AsNoTracking()
            .Where(q => q.QuizId == quizId)
            .OrderBy(q => q.Order)
            .Include(q => q.Options.OrderBy(o => o.Order))
            .ToListAsync();

        var items = await _context.QuizItems
            .AsNoTracking()
            .Where(i => i.QuizId == quizId)
            .OrderBy(i => i.Order)
            .ToListAsync();

        foreach (var item in items)
        {
            if (item.Kind == QuizItemKind.FixedQuestion && item.QuestionId != null)
            {
                var question = await _context.Questions
                    .AsNoTracking()
                    .Include(q => q.Options.OrderBy(o => o.Order))
                    .FirstOrDefaultAsync(q => q.Id == item.QuestionId);
                if (question != null)
                {
                    resolved.Add(question);
                }
            }
            else if (item.Kind == QuizItemKind.RandomFromSection && item.SectionId != null)
            {
                var pool = await _context.Questions
                    .AsNoTracking()
                    .Where(q => q.SectionId == item.SectionId)
                    .Include(q => q.Options.OrderBy(o => o.Order))
                    .ToListAsync();

                // Shuffle and take the requested count (or the whole pool if it is smaller)
                var picked = pool
                    .OrderBy(_ => rng.Next())
                    .Take(item.PickCount ?? pool.Count);
                resolved.AddRange(picked);
            }
        }

        var shuffle = await _context.Quizzes
            .AsNoTracking()
            .Where(q => q.Id == quizId)
            .Select(q => new { q.ShuffleQuestions, q.ShuffleOptions })
            .FirstOrDefaultAsync();

        if (shuffle?.ShuffleOptions == true)
        {
            foreach (var question in resolved)
            {
                QuizShuffle.ShuffleOptions(question, rng);
            }
        }

        return shuffle?.ShuffleQuestions == true
            ? QuizShuffle.Shuffle(resolved, rng)
            : resolved;
    }

    private async Task<int> NextOrderAsync(int quizId)
        => (await _context.QuizItems.Where(i => i.QuizId == quizId).Select(i => (int?)i.Order).MaxAsync() ?? 0) + 1;

    private async Task<ServiceResult<QuizItem>?> EnsureManageableAsync(int quizId, string userId, bool isAdministrator)
    {
        var quiz = await _context.Quizzes.FirstOrDefaultAsync(q => q.Id == quizId);
        if (quiz == null)
        {
            return ServiceResult<QuizItem>.Failure(ServiceError.NotFound, "Тестът не е намерен.");
        }

        if (!QuizPermissions.CanManage(quiz, userId, isAdministrator))
        {
            return ServiceResult<QuizItem>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        return null;
    }
}
