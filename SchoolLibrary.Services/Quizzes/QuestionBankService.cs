using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Quizzes;

public class QuestionBankService : IQuestionBankService
{
    private readonly ApplicationDbContext _context;

    public QuestionBankService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Question>> GetBySectionAsync(int sectionId)
        => await _context.Questions
            .AsNoTracking()
            .Where(q => q.SectionId == sectionId)
            .OrderBy(q => q.Order)
            .Include(q => q.Options.OrderBy(o => o.Order))
            .ToListAsync();

    public async Task<IReadOnlyList<Question>> GetForOwnerAsync(string? ownerId)
        => await _context.Questions
            .AsNoTracking()
            .Where(q => q.SectionId != null && (ownerId == null || q.Section!.OwnerId == ownerId))
            .Include(q => q.Section)
            .OrderBy(q => q.Section!.Name)
            .ThenBy(q => q.Order)
            .ToListAsync();

    public async Task<Question?> GetQuestionAsync(int questionId)
        => await _context.Questions
            .AsNoTracking()
            .Include(q => q.Section)
            .Include(q => q.Options.OrderBy(o => o.Order))
            .FirstOrDefaultAsync(q => q.Id == questionId);

    public async Task<ServiceResult<Question>> CreateAsync(int sectionId, QuestionInput input, string userId, bool isAdministrator)
    {
        var section = await _context.Sections.FirstOrDefaultAsync(s => s.Id == sectionId);
        if (section == null)
        {
            return ServiceResult<Question>.Failure(ServiceError.NotFound, "Разделът не е намерен.");
        }

        if (!SectionPermissions.CanManage(section, userId, isAdministrator))
        {
            return ServiceResult<Question>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        var validation = QuizService.ValidateQuestion(input);
        if (validation != null)
        {
            return ServiceResult<Question>.Failure(ServiceError.Invalid, validation);
        }

        var maxOrder = await _context.Questions
            .Where(q => q.SectionId == sectionId)
            .Select(q => (int?)q.Order)
            .MaxAsync() ?? 0;

        var question = new Question
        {
            SectionId = sectionId,
            Text = input.Text.Trim(),
            Type = QuestionType.SingleChoice,
            Points = input.Points,
            Order = maxOrder + 1
        };
        AddOptions(question, input);

        _context.Questions.Add(question);
        await _context.SaveChangesAsync();

        return ServiceResult<Question>.Success(question);
    }

    public async Task<ServiceResult<Question>> UpdateAsync(int questionId, QuestionInput input, string userId, bool isAdministrator)
    {
        var question = await _context.Questions
            .Include(q => q.Section)
            .Include(q => q.Options)
            .FirstOrDefaultAsync(q => q.Id == questionId && q.SectionId != null);
        if (question == null)
        {
            return ServiceResult<Question>.Failure(ServiceError.NotFound, "Въпросът не е намерен.");
        }

        if (!SectionPermissions.CanManage(question.Section!, userId, isAdministrator))
        {
            return ServiceResult<Question>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        var validation = QuizService.ValidateQuestion(input);
        if (validation != null)
        {
            return ServiceResult<Question>.Failure(ServiceError.Invalid, validation);
        }

        question.Text = input.Text.Trim();
        question.Points = input.Points;

        _context.AnswerOptions.RemoveRange(question.Options);
        question.Options.Clear();
        AddOptions(question, input);

        await _context.SaveChangesAsync();

        return ServiceResult<Question>.Success(question);
    }

    public async Task<ServiceResult<Question>> DeleteAsync(int questionId, string userId, bool isAdministrator)
    {
        var question = await _context.Questions
            .Include(q => q.Section)
            .FirstOrDefaultAsync(q => q.Id == questionId && q.SectionId != null);
        if (question == null)
        {
            return ServiceResult<Question>.Failure(ServiceError.NotFound, "Въпросът не е намерен.");
        }

        if (!SectionPermissions.CanManage(question.Section!, userId, isAdministrator))
        {
            return ServiceResult<Question>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        // Cascade removes the question's options
        _context.Questions.Remove(question);
        await _context.SaveChangesAsync();

        return ServiceResult<Question>.Success(question);
    }

    public async Task<ServiceResult<ImportSummary>> ImportAikenAsync(int sectionId, string text, string userId, bool isAdministrator)
    {
        var section = await _context.Sections.FirstOrDefaultAsync(s => s.Id == sectionId);
        if (section == null)
        {
            return ServiceResult<ImportSummary>.Failure(ServiceError.NotFound, "Разделът не е намерен.");
        }

        if (!SectionPermissions.CanManage(section, userId, isAdministrator))
        {
            return ServiceResult<ImportSummary>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        var parsed = AikenParser.Parse(text);
        var errors = parsed.Errors.ToList();

        var maxOrder = await _context.Questions
            .Where(q => q.SectionId == sectionId)
            .Select(q => (int?)q.Order)
            .MaxAsync() ?? 0;

        var added = 0;
        foreach (var input in parsed.Questions)
        {
            var validation = QuizService.ValidateQuestion(input);
            if (validation != null)
            {
                errors.Add($"„{Shorten(input.Text)}“: {validation}");
                continue;
            }

            var question = new Question
            {
                SectionId = sectionId,
                Text = input.Text.Trim(),
                Type = QuestionType.SingleChoice,
                Points = input.Points,
                Order = ++maxOrder
            };
            AddOptions(question, input);
            _context.Questions.Add(question);
            added++;
        }

        await _context.SaveChangesAsync();
        return ServiceResult<ImportSummary>.Success(new ImportSummary(added, errors));
    }

    private static string Shorten(string text)
        => text.Length <= 50 ? text : text[..50] + "…";

    private static void AddOptions(Question question, QuestionInput input)
    {
        for (var i = 0; i < input.Options.Count; i++)
        {
            question.Options.Add(new AnswerOption
            {
                Text = input.Options[i].Trim(),
                IsCorrect = i == input.CorrectIndex,
                Order = i + 1
            });
        }
    }
}
