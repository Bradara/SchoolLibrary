using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Quizzes;

public class QuizItemService : IQuizItemService
{
    public const int MaxPickCount = 100;

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

        var items = await _context.QuizItems
            .AsNoTracking()
            .Where(i => i.QuizId == quizId)
            .OrderBy(i => i.Order)
            .ToListAsync();

        // A quiz with no items still carries questions attached directly (legacy quizzes); use those
        if (items.Count == 0)
        {
            return await _context.Questions
                .AsNoTracking()
                .Where(q => q.QuizId == quizId)
                .OrderBy(q => q.Order)
                .Include(q => q.Options.OrderBy(o => o.Order))
                .ToListAsync();
        }

        var resolved = new List<Question>();

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

        return resolved;
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
