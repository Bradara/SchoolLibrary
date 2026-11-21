using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.Services.Exams;

public class ExamAssignmentService : IExamAssignmentService
{
    public const int MaxPageSize = 100;

    private readonly ApplicationDbContext _context;

    public ExamAssignmentService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ExamAssignmentListItem>> GetForOwnerAsync(string? ownerId, int? schoolClassId = null)
    {
        var query = _context.ExamAssignments.AsNoTracking()
            .Include(e => e.Quiz)
            .Include(e => e.SchoolClass)
            .Where(e => ownerId == null || e.SchoolClass.OwnerId == ownerId);
        if (schoolClassId != null)
        {
            query = query.Where(e => e.SchoolClassId == schoolClassId);
        }

        var exams = await query
            .OrderByDescending(e => e.CreatedOn)
            .ThenByDescending(e => e.Id)
            .ToListAsync();

        // Counts are loaded separately (grouped), so the in-memory provider used by the tests can run them
        var ids = exams.Select(e => e.Id).ToList();
        var counts = await _context.QuizAttempts
            .Where(a => a.ExamAssignmentId != null && ids.Contains(a.ExamAssignmentId.Value))
            .GroupBy(a => new { Id = a.ExamAssignmentId!.Value, Submitted = a.SubmittedOn != null })
            .Select(g => new { g.Key.Id, g.Key.Submitted, Count = g.Count() })
            .ToListAsync();

        return exams
            .Select(e => new ExamAssignmentListItem(
                e,
                counts.Where(c => c.Id == e.Id && c.Submitted).Sum(c => c.Count),
                counts.Where(c => c.Id == e.Id && !c.Submitted).Sum(c => c.Count)))
            .ToList();
    }

    public async Task<IReadOnlyList<Quiz>> GetAssignableQuizzesAsync(string? ownerId)
        => await _context.Quizzes.AsNoTracking()
            .Where(q => q.Mode == QuizMode.Practice && (ownerId == null || q.OwnerId == ownerId))
            .Where(q => q.Questions.Any() || q.Items.Any())
            .OrderBy(q => q.Title)
            .ThenBy(q => q.Id)
            .ToListAsync();

    public async Task<ExamAssignment?> GetByIdAsync(int id)
        => await _context.ExamAssignments.AsNoTracking()
            .Include(e => e.Quiz)
            .Include(e => e.SchoolClass)
            .FirstOrDefaultAsync(e => e.Id == id);

    public async Task<ServiceResult<ExamAssignment>> CreateAsync(ExamAssignmentInput input, string userId, bool isAdministrator)
    {
        var schoolClass = await _context.SchoolClasses.FirstOrDefaultAsync(c => c.Id == input.SchoolClassId);
        if (schoolClass == null)
        {
            return ServiceResult<ExamAssignment>.Failure(ServiceError.NotFound, "Класът не е намерен.");
        }

        var quiz = await _context.Quizzes.FirstOrDefaultAsync(q => q.Id == input.QuizId);
        if (quiz == null)
        {
            return ServiceResult<ExamAssignment>.Failure(ServiceError.NotFound, "Тестът не е намерен.");
        }

        if (!ExamPermissions.CanManage(schoolClass, userId, isAdministrator)
            || !QuizPermissions.CanManage(quiz, userId, isAdministrator))
        {
            return ServiceResult<ExamAssignment>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        if (quiz.Mode != QuizMode.Practice)
        {
            return ServiceResult<ExamAssignment>.Failure(ServiceError.Invalid,
                "Като изпит се назначава тест за упражнение.");
        }

        var hasQuestions = await _context.Questions.AnyAsync(q => q.QuizId == quiz.Id)
                           || await _context.QuizItems.AnyAsync(i => i.QuizId == quiz.Id);
        if (!hasQuestions)
        {
            return ServiceResult<ExamAssignment>.Failure(ServiceError.Invalid, "Тестът още няма въпроси.");
        }

        var validation = Validate(input.TimeLimitMinutes, input.MaxLeaves);
        if (validation != null)
        {
            return ServiceResult<ExamAssignment>.Failure(ServiceError.Invalid, validation);
        }

        var exam = new ExamAssignment
        {
            QuizId = quiz.Id,
            SchoolClassId = schoolClass.Id,
            Code = await QuizCode.GenerateUniqueAsync(_context, QuizService.CodeLength),
            TimeLimitMinutes = input.TimeLimitMinutes,
            MaxLeaves = input.MaxLeaves,
            IsOpen = true,
            ResultsReleased = false,
            CreatedOn = DateTime.UtcNow
        };

        _context.ExamAssignments.Add(exam);
        await _context.SaveChangesAsync();

        return ServiceResult<ExamAssignment>.Success(exam);
    }

    public async Task<ServiceResult<ExamAssignment>> UpdateSettingsAsync(int id, ExamSettingsInput input, string userId, bool isAdministrator)
    {
        var (exam, failure) = await FindManageableAsync(id, userId, isAdministrator);
        if (failure != null)
        {
            return failure;
        }

        var validation = Validate(input.TimeLimitMinutes, input.MaxLeaves);
        if (validation != null)
        {
            return ServiceResult<ExamAssignment>.Failure(ServiceError.Invalid, validation);
        }

        exam!.TimeLimitMinutes = input.TimeLimitMinutes;
        exam.MaxLeaves = input.MaxLeaves;
        await _context.SaveChangesAsync();

        return ServiceResult<ExamAssignment>.Success(exam);
    }

    public async Task<ServiceResult<ExamAssignment>> SetOpenAsync(int id, bool open, string userId, bool isAdministrator)
    {
        var (exam, failure) = await FindManageableAsync(id, userId, isAdministrator);
        if (failure != null)
        {
            return failure;
        }

        exam!.IsOpen = open;
        await _context.SaveChangesAsync();
        return ServiceResult<ExamAssignment>.Success(exam);
    }

    public async Task<ServiceResult<ExamAssignment>> SetResultsReleasedAsync(int id, bool released, string userId, bool isAdministrator)
    {
        var (exam, failure) = await FindManageableAsync(id, userId, isAdministrator);
        if (failure != null)
        {
            return failure;
        }

        exam!.ResultsReleased = released;
        await _context.SaveChangesAsync();
        return ServiceResult<ExamAssignment>.Success(exam);
    }

    public async Task<ServiceResult<ExamAssignment>> DeleteAsync(int id, string userId, bool isAdministrator)
    {
        var (exam, failure) = await FindManageableAsync(id, userId, isAdministrator);
        if (failure != null)
        {
            return failure;
        }

        // Cascade removes the exam's attempts with their answers
        _context.ExamAssignments.Remove(exam!);
        await _context.SaveChangesAsync();
        return ServiceResult<ExamAssignment>.Success(exam!);
    }

    public async Task<ServiceResult<ExamResults>> GetResultsAsync(int id, string userId, bool isAdministrator, int page, int pageSize = 50)
    {
        var exam = await GetByIdAsync(id);
        if (exam == null)
        {
            return ServiceResult<ExamResults>.Failure(ServiceError.NotFound, "Изпитът не е намерен.");
        }

        if (!ExamPermissions.CanManage(exam.SchoolClass, userId, isAdministrator))
        {
            return ServiceResult<ExamResults>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        var query = _context.QuizAttempts.AsNoTracking().Where(a => a.ExamAssignmentId == id);
        var totalCount = await query.CountAsync();

        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        page = Math.Clamp(page, 1, Math.Max(totalPages, 1));

        // Numbers are stored without leading zeros, so ordering by length then text is numeric order
        var attempts = await query
            .OrderBy(a => a.StudentNumber!.Length)
            .ThenBy(a => a.StudentNumber)
            .ThenBy(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return ServiceResult<ExamResults>.Success(
            new ExamResults(exam, new PagedResult<QuizAttempt>(attempts, totalCount, page, pageSize)));
    }

    public async Task<ServiceResult<ExamAttemptReview>> GetAttemptReviewAsync(int attemptId, string userId, bool isAdministrator)
    {
        var attempt = await _context.QuizAttempts.AsNoTracking()
            .Include(a => a.Quiz)
            .Include(a => a.ExamAssignment!)
                .ThenInclude(e => e.SchoolClass)
            .Include(a => a.Questions.OrderBy(x => x.Order))
                .ThenInclude(aq => aq.Question)
                    .ThenInclude(q => q.Options.OrderBy(o => o.Order))
            .Include(a => a.Answers).ThenInclude(answer => answer.Choices)
            .FirstOrDefaultAsync(a => a.Id == attemptId);
        if (attempt?.ExamAssignment == null)
        {
            return ServiceResult<ExamAttemptReview>.Failure(ServiceError.NotFound, "Опитът не е намерен.");
        }

        if (!ExamPermissions.CanManage(attempt.ExamAssignment.SchoolClass, userId, isAdministrator))
        {
            return ServiceResult<ExamAttemptReview>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        if (attempt.SubmittedOn == null)
        {
            return ServiceResult<ExamAttemptReview>.Failure(ServiceError.Invalid, "Опитът още не е предаден.");
        }

        QuizAttemptResults.ApplyOptionOrder(attempt);
        return ServiceResult<ExamAttemptReview>.Success(new ExamAttemptReview(attempt, QuizAttemptResults.Build(attempt)));
    }

    public async Task<ServiceResult<QuizAttempt>> DeleteAttemptAsync(int attemptId, string userId, bool isAdministrator)
    {
        var attempt = await _context.QuizAttempts
            .Include(a => a.ExamAssignment!)
                .ThenInclude(e => e.SchoolClass)
            .Include(a => a.Questions)
            .Include(a => a.Answers).ThenInclude(answer => answer.Choices)
            .FirstOrDefaultAsync(a => a.Id == attemptId);
        if (attempt?.ExamAssignment == null)
        {
            return ServiceResult<QuizAttempt>.Failure(ServiceError.NotFound, "Опитът не е намерен.");
        }

        if (!ExamPermissions.CanManage(attempt.ExamAssignment.SchoolClass, userId, isAdministrator))
        {
            return ServiceResult<QuizAttempt>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        // Cascade removes the attempt's question snapshot and answers; the number is free again
        _context.QuizAttempts.Remove(attempt);
        await _context.SaveChangesAsync();

        return ServiceResult<QuizAttempt>.Success(attempt);
    }

    private async Task<(ExamAssignment? Exam, ServiceResult<ExamAssignment>? Failure)> FindManageableAsync(int id, string userId, bool isAdministrator)
    {
        var exam = await _context.ExamAssignments
            .Include(e => e.SchoolClass)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (exam == null)
        {
            return (null, ServiceResult<ExamAssignment>.Failure(ServiceError.NotFound, "Изпитът не е намерен."));
        }

        if (!ExamPermissions.CanManage(exam.SchoolClass, userId, isAdministrator))
        {
            return (null, ServiceResult<ExamAssignment>.Failure(ServiceError.Forbidden, "Нямате права за това действие."));
        }

        return (exam, null);
    }

    private static string? Validate(int? timeLimitMinutes, int? maxLeaves)
    {
        if (timeLimitMinutes is int minutes && (minutes < 1 || minutes > QuizService.MaxTimeLimitMinutes))
        {
            return $"Времето трябва да е между 1 и {QuizService.MaxTimeLimitMinutes} минути.";
        }

        if (maxLeaves is int leaves && (leaves < 0 || leaves > QuizService.MaxProctorLeaves))
        {
            return $"Допустимите напускания трябва да са между 0 и {QuizService.MaxProctorLeaves}.";
        }

        return null;
    }
}
