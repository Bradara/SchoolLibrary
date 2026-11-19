using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Quizzes;

public class QuizService : IQuizService
{
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 1000;
    public const int QuestionMaxLength = 1000;
    public const int OptionMaxLength = 500;
    public const int MinOptions = 2;
    public const int MaxOptions = 10;
    public const int MaxQuestionPoints = 100;
    public const int MaxTimeLimitMinutes = 600;
    public const int CodeLength = 6;
    public const int MaxStudentNameLength = 100;
    public const int MaxStudentNumberLength = 20;
    public const int MaxProctorLeaves = 100;
    public const int MaxPageSize = 100;

    private readonly ApplicationDbContext _context;

    public QuizService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Quiz?> GetByIdAsync(int id)
    {
        return await _context.Quizzes
            .AsNoTracking()
            .Include(q => q.Resource)
            .Include(q => q.Questions.OrderBy(x => x.Order))
                .ThenInclude(q => q.Options.OrderBy(o => o.Order))
            .FirstOrDefaultAsync(q => q.Id == id);
    }

    public async Task<PagedResult<QuizListItem>> GetManagedAsync(string? ownerId, int page, int pageSize = 10)
    {
        var query = _context.Quizzes.AsNoTracking();
        if (ownerId != null)
        {
            query = query.Where(q => q.OwnerId == ownerId);
        }

        var totalCount = await query.CountAsync();

        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        page = Math.Clamp(page, 1, Math.Max(totalPages, 1));

        var quizzes = await query
            .OrderByDescending(q => q.CreatedOn)
            .ThenByDescending(q => q.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Counts are loaded separately (grouped), so the projection stays simple and the in-memory
        // provider used by the tests can run it
        var ids = quizzes.Select(q => q.Id).ToList();

        var questionCounts = await _context.Questions
            .Where(x => x.QuizId != null && ids.Contains(x.QuizId.Value))
            .GroupBy(x => x.QuizId!.Value)
            .Select(g => new { QuizId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.QuizId, g => g.Count);

        var attemptCounts = await _context.QuizAttempts
            .Where(x => ids.Contains(x.QuizId) && x.SubmittedOn != null)
            .GroupBy(x => x.QuizId)
            .Select(g => new { QuizId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.QuizId, g => g.Count);

        var items = quizzes
            .Select(q => new QuizListItem(q, questionCounts.GetValueOrDefault(q.Id), attemptCounts.GetValueOrDefault(q.Id)))
            .ToList();

        return new PagedResult<QuizListItem>(items, totalCount, page, pageSize);
    }

    public async Task<ServiceResult<Quiz>> CreateAsync(QuizInput input, string ownerId)
    {
        var validation = ValidateQuiz(input);
        if (validation != null)
        {
            return ServiceResult<Quiz>.Failure(ServiceError.Invalid, validation);
        }

        if (input.ResourceId is int resourceId && !await _context.Resources.AnyAsync(r => r.Id == resourceId))
        {
            return ServiceResult<Quiz>.Failure(ServiceError.NotFound, "Избраният урок не е намерен.");
        }

        var quiz = new Quiz
        {
            Title = input.Title.Trim(),
            Description = Clean(input.Description),
            Mode = input.Mode,
            TimeLimitMinutes = input.Mode == QuizMode.Exam ? input.TimeLimitMinutes : null,
            MaxLeaves = input.Mode == QuizMode.Exam ? input.MaxLeaves : null,
            ResourceId = input.ResourceId,
            OwnerId = ownerId,
            IsPublished = false,
            CreatedOn = DateTime.UtcNow,
            Code = input.Mode == QuizMode.Exam ? await GenerateUniqueCodeAsync() : null
        };

        _context.Quizzes.Add(quiz);
        await _context.SaveChangesAsync();

        return ServiceResult<Quiz>.Success(quiz);
    }

    public async Task<ServiceResult<Quiz>> UpdateAsync(int id, QuizInput input, string userId, bool isAdministrator)
    {
        var quiz = await _context.Quizzes.FirstOrDefaultAsync(q => q.Id == id);
        if (quiz == null)
        {
            return ServiceResult<Quiz>.Failure(ServiceError.NotFound, "Тестът не е намерен.");
        }

        if (!QuizPermissions.CanManage(quiz, userId, isAdministrator))
        {
            return ServiceResult<Quiz>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        var validation = ValidateQuiz(input);
        if (validation != null)
        {
            return ServiceResult<Quiz>.Failure(ServiceError.Invalid, validation);
        }

        if (input.ResourceId is int resourceId && !await _context.Resources.AnyAsync(r => r.Id == resourceId))
        {
            return ServiceResult<Quiz>.Failure(ServiceError.NotFound, "Избраният урок не е намерен.");
        }

        // Changing the mode once students have solved it would clear the code or the timer under them
        if (input.Mode != quiz.Mode && await HasAttemptsAsync(quiz.Id))
        {
            return ServiceResult<Quiz>.Failure(ServiceError.Conflict,
                "Тестът вече има решения; режимът не може да се сменя.");
        }

        quiz.Title = input.Title.Trim();
        quiz.Description = Clean(input.Description);
        quiz.ResourceId = input.ResourceId;
        quiz.Mode = input.Mode;

        if (input.Mode == QuizMode.Exam)
        {
            quiz.TimeLimitMinutes = input.TimeLimitMinutes;
            quiz.MaxLeaves = input.MaxLeaves;
            quiz.Code ??= await GenerateUniqueCodeAsync();
        }
        else
        {
            quiz.TimeLimitMinutes = null;
            quiz.MaxLeaves = null;
            quiz.Code = null;
        }

        await _context.SaveChangesAsync();

        return ServiceResult<Quiz>.Success(quiz);
    }

    public async Task<ServiceResult<Quiz>> DeleteAsync(int id, string userId, bool isAdministrator)
    {
        var quiz = await _context.Quizzes.FirstOrDefaultAsync(q => q.Id == id);
        if (quiz == null)
        {
            return ServiceResult<Quiz>.Failure(ServiceError.NotFound, "Тестът не е намерен.");
        }

        if (!QuizPermissions.CanManage(quiz, userId, isAdministrator))
        {
            return ServiceResult<Quiz>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        // Cascade removes the questions, options, attempts and answers
        _context.Quizzes.Remove(quiz);
        await _context.SaveChangesAsync();

        return ServiceResult<Quiz>.Success(quiz);
    }

    public async Task<ServiceResult<Quiz>> SetPublishedAsync(int id, bool published, string userId, bool isAdministrator)
    {
        var quiz = await _context.Quizzes.FirstOrDefaultAsync(q => q.Id == id);
        if (quiz == null)
        {
            return ServiceResult<Quiz>.Failure(ServiceError.NotFound, "Тестът не е намерен.");
        }

        if (!QuizPermissions.CanManage(quiz, userId, isAdministrator))
        {
            return ServiceResult<Quiz>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        if (published && !await _context.Questions.AnyAsync(q => q.QuizId == id))
        {
            return ServiceResult<Quiz>.Failure(ServiceError.Invalid,
                "Добавете поне един въпрос, преди да публикувате теста.");
        }

        quiz.IsPublished = published;
        await _context.SaveChangesAsync();

        return ServiceResult<Quiz>.Success(quiz);
    }

    public async Task<ServiceResult<Question>> AddQuestionAsync(int quizId, QuestionInput input, string userId, bool isAdministrator)
    {
        var quiz = await _context.Quizzes.FirstOrDefaultAsync(q => q.Id == quizId);
        if (quiz == null)
        {
            return ServiceResult<Question>.Failure(ServiceError.NotFound, "Тестът не е намерен.");
        }

        if (!QuizPermissions.CanManage(quiz, userId, isAdministrator))
        {
            return ServiceResult<Question>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        if (await HasAttemptsAsync(quizId))
        {
            return ServiceResult<Question>.Failure(ServiceError.Conflict,
                "Тестът вече има решения; въпросите не могат да се променят.");
        }

        var validation = ValidateQuestion(input);
        if (validation != null)
        {
            return ServiceResult<Question>.Failure(ServiceError.Invalid, validation);
        }

        var maxOrder = await _context.Questions
            .Where(q => q.QuizId == quizId)
            .Select(q => (int?)q.Order)
            .MaxAsync() ?? 0;

        var question = new Question
        {
            QuizId = quizId,
            Text = input.Text.Trim(),
            Type = QuestionType.SingleChoice,
            Points = input.Points,
            Order = maxOrder + 1
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

        return ServiceResult<Question>.Success(question);
    }

    public async Task<Question?> GetQuestionAsync(int questionId)
    {
        return await _context.Questions
            .AsNoTracking()
            .Include(q => q.Quiz)
            .Include(q => q.Options.OrderBy(o => o.Order))
            .FirstOrDefaultAsync(q => q.Id == questionId);
    }

    public async Task<ServiceResult<Question>> UpdateQuestionAsync(int questionId, QuestionInput input, string userId, bool isAdministrator)
    {
        var question = await _context.Questions
            .Include(q => q.Quiz)
            .Include(q => q.Options)
            .FirstOrDefaultAsync(q => q.Id == questionId);
        if (question == null)
        {
            return ServiceResult<Question>.Failure(ServiceError.NotFound, "Въпросът не е намерен.");
        }

        if (!QuizPermissions.CanManage(question.Quiz, userId, isAdministrator))
        {
            return ServiceResult<Question>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        if (await HasAttemptsAsync(question.QuizId))
        {
            return ServiceResult<Question>.Failure(ServiceError.Conflict,
                "Тестът вече има решения; въпросите не могат да се променят.");
        }

        var validation = ValidateQuestion(input);
        if (validation != null)
        {
            return ServiceResult<Question>.Failure(ServiceError.Invalid, validation);
        }

        question.Text = input.Text.Trim();
        question.Points = input.Points;

        // Replace the options wholesale; the question keeps its place in the quiz
        _context.AnswerOptions.RemoveRange(question.Options);
        question.Options.Clear();
        for (var i = 0; i < input.Options.Count; i++)
        {
            question.Options.Add(new AnswerOption
            {
                Text = input.Options[i].Trim(),
                IsCorrect = i == input.CorrectIndex,
                Order = i + 1
            });
        }

        await _context.SaveChangesAsync();

        return ServiceResult<Question>.Success(question);
    }

    public async Task<ServiceResult<Question>> DeleteQuestionAsync(int questionId, string userId, bool isAdministrator)
    {
        var question = await _context.Questions
            .Include(q => q.Quiz)
            .FirstOrDefaultAsync(q => q.Id == questionId);
        if (question == null)
        {
            return ServiceResult<Question>.Failure(ServiceError.NotFound, "Въпросът не е намерен.");
        }

        if (!QuizPermissions.CanManage(question.Quiz, userId, isAdministrator))
        {
            return ServiceResult<Question>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        if (await HasAttemptsAsync(question.QuizId))
        {
            return ServiceResult<Question>.Failure(ServiceError.Conflict,
                "Тестът вече има решения; въпросите не могат да се променят.");
        }

        // Cascade removes the question's options
        _context.Questions.Remove(question);
        await _context.SaveChangesAsync();

        return ServiceResult<Question>.Success(question);
    }

    public async Task<PagedResult<QuizListItem>> GetPublishedPracticeAsync(int page, int pageSize = 10)
    {
        var query = _context.Quizzes.AsNoTracking()
            .Where(q => q.Mode == QuizMode.Practice && q.IsPublished);

        var totalCount = await query.CountAsync();

        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        page = Math.Clamp(page, 1, Math.Max(totalPages, 1));

        var quizzes = await query
            .OrderBy(q => q.Title)
            .ThenBy(q => q.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var ids = quizzes.Select(q => q.Id).ToList();
        var questionCounts = await _context.Questions
            .Where(x => x.QuizId != null && ids.Contains(x.QuizId.Value))
            .GroupBy(x => x.QuizId!.Value)
            .Select(g => new { QuizId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.QuizId, g => g.Count);

        var items = quizzes
            .Select(q => new QuizListItem(q, questionCounts.GetValueOrDefault(q.Id), 0))
            .ToList();

        return new PagedResult<QuizListItem>(items, totalCount, page, pageSize);
    }

    public async Task<Quiz?> GetPracticeForSolvingAsync(int id)
    {
        return await _context.Quizzes
            .AsNoTracking()
            .Include(q => q.Questions.OrderBy(x => x.Order))
                .ThenInclude(q => q.Options.OrderBy(o => o.Order))
            .FirstOrDefaultAsync(q => q.Id == id && q.Mode == QuizMode.Practice && q.IsPublished);
    }

    public async Task<ServiceResult<QuizResult>> GradePracticeAsync(int quizId, IReadOnlyDictionary<int, int?> answers)
    {
        var quiz = await GetPracticeForSolvingAsync(quizId);
        if (quiz == null)
        {
            return ServiceResult<QuizResult>.Failure(ServiceError.NotFound, "Упражнението не е намерено.");
        }

        var result = QuizGrading.Grade(quiz, answers);
        return ServiceResult<QuizResult>.Success(result);
    }

    public async Task<ServiceResult<QuizAttempt>> StartExamAsync(string code, string studentName, string? studentNumber)
    {
        var normalizedCode = (code ?? string.Empty).Trim().ToUpperInvariant();
        var name = (studentName ?? string.Empty).Trim();
        var number = string.IsNullOrWhiteSpace(studentNumber) ? null : studentNumber.Trim();

        if (name.Length == 0)
        {
            return ServiceResult<QuizAttempt>.Failure(ServiceError.Invalid, "Въведете име.");
        }

        if (name.Length > MaxStudentNameLength)
        {
            return ServiceResult<QuizAttempt>.Failure(ServiceError.Invalid,
                $"Името не може да бъде по-дълго от {MaxStudentNameLength} символа.");
        }

        if (number != null && number.Length > MaxStudentNumberLength)
        {
            return ServiceResult<QuizAttempt>.Failure(ServiceError.Invalid,
                $"Номерът не може да бъде по-дълъг от {MaxStudentNumberLength} символа.");
        }

        var quiz = await _context.Quizzes
            .FirstOrDefaultAsync(q => q.Code == normalizedCode && q.Mode == QuizMode.Exam && q.IsPublished);
        if (quiz == null)
        {
            return ServiceResult<QuizAttempt>.Failure(ServiceError.NotFound, "Няма активен изпит с този код.");
        }

        // One attempt per student (by name and number): block a second try, resume an unfinished one
        var lowered = name.ToLower();
        var mine = await _context.QuizAttempts
            .Where(a => a.QuizId == quiz.Id && a.StudentName.ToLower() == lowered && a.StudentNumber == number)
            .ToListAsync();

        if (mine.Any(a => a.SubmittedOn != null))
        {
            return ServiceResult<QuizAttempt>.Failure(ServiceError.Conflict, "Вече си решил този изпит.");
        }

        var inProgress = mine.FirstOrDefault(a => a.SubmittedOn == null);
        if (inProgress != null)
        {
            return ServiceResult<QuizAttempt>.Success(inProgress);
        }

        var questionCount = await _context.Questions.CountAsync(q => q.QuizId == quiz.Id);

        var attempt = new QuizAttempt
        {
            Token = Guid.NewGuid(),
            QuizId = quiz.Id,
            StudentName = name,
            StudentNumber = number,
            StartedOn = DateTime.UtcNow,
            Score = 0,
            MaxScore = questionCount
        };

        _context.QuizAttempts.Add(attempt);
        await _context.SaveChangesAsync();

        return ServiceResult<QuizAttempt>.Success(attempt);
    }

    public async Task<QuizAttempt?> GetExamAttemptAsync(Guid token)
    {
        return await _context.QuizAttempts
            .AsNoTracking()
            .Include(a => a.Quiz)
                .ThenInclude(q => q.Questions.OrderBy(x => x.Order))
                    .ThenInclude(q => q.Options.OrderBy(o => o.Order))
            .FirstOrDefaultAsync(a => a.Token == token);
    }

    public async Task<ServiceResult<QuizResult>> SubmitExamAsync(Guid token, IReadOnlyDictionary<int, int?> answers)
    {
        var attempt = await _context.QuizAttempts
            .Include(a => a.Quiz)
                .ThenInclude(q => q.Questions)
                    .ThenInclude(q => q.Options)
            .Include(a => a.Answers)
            .FirstOrDefaultAsync(a => a.Token == token);
        if (attempt == null)
        {
            return ServiceResult<QuizResult>.Failure(ServiceError.NotFound, "Опитът не е намерен.");
        }

        // Resubmitting (e.g. a double click or the timer firing after a manual submit) must not regrade
        if (attempt.SubmittedOn != null)
        {
            return ServiceResult<QuizResult>.Success(BuildResult(attempt));
        }

        var result = QuizGrading.Grade(attempt.Quiz, answers);

        foreach (var questionResult in result.Questions)
        {
            attempt.Answers.Add(new AttemptAnswer
            {
                QuestionId = questionResult.Question.Id,
                SelectedOptionId = questionResult.SelectedOptionId,
                IsCorrect = questionResult.IsCorrect
            });
        }

        attempt.Score = result.Score;
        attempt.MaxScore = result.MaxScore;
        attempt.SubmittedOn = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return ServiceResult<QuizResult>.Success(result);
    }

    public async Task<ServiceResult<QuizResult>> GetExamResultAsync(Guid token)
    {
        var attempt = await _context.QuizAttempts
            .AsNoTracking()
            .Include(a => a.Quiz)
                .ThenInclude(q => q.Questions.OrderBy(x => x.Order))
                    .ThenInclude(q => q.Options.OrderBy(o => o.Order))
            .Include(a => a.Answers)
            .FirstOrDefaultAsync(a => a.Token == token);
        if (attempt == null)
        {
            return ServiceResult<QuizResult>.Failure(ServiceError.NotFound, "Опитът не е намерен.");
        }

        if (attempt.SubmittedOn == null)
        {
            return ServiceResult<QuizResult>.Failure(ServiceError.Invalid, "Изпитът още не е предаден.");
        }

        return ServiceResult<QuizResult>.Success(BuildResult(attempt));
    }

    public async Task<ServiceResult<ProctorStatus>> RecordLeaveAsync(Guid token)
    {
        var attempt = await _context.QuizAttempts
            .Include(a => a.Quiz)
            .FirstOrDefaultAsync(a => a.Token == token);
        if (attempt == null)
        {
            return ServiceResult<ProctorStatus>.Failure(ServiceError.NotFound, "Опитът не е намерен.");
        }

        // A submitted attempt no longer counts leaves
        if (attempt.SubmittedOn != null)
        {
            return ServiceResult<ProctorStatus>.Success(new ProctorStatus(attempt.LeaveCount, attempt.Flagged, false));
        }

        attempt.LeaveCount++;

        var shouldSubmit = false;
        if (attempt.Quiz.MaxLeaves is int max && attempt.LeaveCount > max)
        {
            attempt.Flagged = true;
            shouldSubmit = true;
        }

        await _context.SaveChangesAsync();

        return ServiceResult<ProctorStatus>.Success(new ProctorStatus(attempt.LeaveCount, attempt.Flagged, shouldSubmit));
    }

    public async Task<ServiceResult<QuizReview>> GetReviewAsync(int quizId, string userId, bool isAdministrator, int page, int pageSize = 20)
    {
        var quiz = await _context.Quizzes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == quizId);
        if (quiz == null)
        {
            return ServiceResult<QuizReview>.Failure(ServiceError.NotFound, "Тестът не е намерен.");
        }

        if (!QuizPermissions.CanManage(quiz, userId, isAdministrator))
        {
            return ServiceResult<QuizReview>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        var query = _context.QuizAttempts.AsNoTracking()
            .Where(a => a.QuizId == quizId && a.SubmittedOn != null);

        var totalCount = await query.CountAsync();

        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        page = Math.Clamp(page, 1, Math.Max(totalPages, 1));

        var items = await query
            .OrderByDescending(a => a.SubmittedOn)
            .ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var attempts = new PagedResult<QuizAttempt>(items, totalCount, page, pageSize);
        return ServiceResult<QuizReview>.Success(new QuizReview(quiz, attempts));
    }

    // Rebuilds the graded result from the options the student picked (stored on the attempt)
    private static QuizResult BuildResult(QuizAttempt attempt)
    {
        var answers = attempt.Answers.ToDictionary(a => a.QuestionId, a => a.SelectedOptionId);
        return QuizGrading.Grade(attempt.Quiz, answers);
    }

    private async Task<bool> HasAttemptsAsync(int? quizId)
        => quizId != null && await _context.QuizAttempts.AnyAsync(a => a.QuizId == quizId.Value);

    private async Task<string> GenerateUniqueCodeAsync()
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var code = QuizCode.Generate(CodeLength);
            if (!await _context.Quizzes.AnyAsync(q => q.Code == code))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Could not generate a unique quiz code.");
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ValidateQuiz(QuizInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
        {
            return "Заглавието е задължително.";
        }

        if (input.Title.Trim().Length > MaxTitleLength)
        {
            return $"Заглавието не може да бъде по-дълго от {MaxTitleLength} символа.";
        }

        if (input.Description != null && input.Description.Trim().Length > MaxDescriptionLength)
        {
            return $"Описанието не може да бъде по-дълго от {MaxDescriptionLength} символа.";
        }

        if (!Enum.IsDefined(input.Mode))
        {
            return "Невалиден режим на теста.";
        }

        if (input.Mode == QuizMode.Exam && input.TimeLimitMinutes is int minutes
            && (minutes < 1 || minutes > MaxTimeLimitMinutes))
        {
            return $"Времето за изпит трябва да е между 1 и {MaxTimeLimitMinutes} минути.";
        }

        if (input.Mode == QuizMode.Exam && input.MaxLeaves is int leaves
            && (leaves < 0 || leaves > MaxProctorLeaves))
        {
            return $"Допустимите напускания трябва да са между 0 и {MaxProctorLeaves}.";
        }

        return null;
    }

    /// <summary>Validates a single-choice question's text, options, correct index and points.</summary>
    public static string? ValidateQuestion(QuestionInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Text))
        {
            return "Текстът на въпроса е задължителен.";
        }

        if (input.Text.Trim().Length > QuestionMaxLength)
        {
            return $"Въпросът не може да бъде по-дълъг от {QuestionMaxLength} символа.";
        }

        var options = (input.Options ?? new List<string>())
            .Select(o => (o ?? string.Empty).Trim())
            .ToList();

        if (options.Count < MinOptions)
        {
            return $"Добавете поне {MinOptions} отговора.";
        }

        if (options.Count > MaxOptions)
        {
            return $"Отговорите не може да са повече от {MaxOptions}.";
        }

        if (options.Any(string.IsNullOrEmpty))
        {
            return "Отговорите не може да са празни.";
        }

        if (options.Any(o => o.Length > OptionMaxLength))
        {
            return $"Отговорът не може да бъде по-дълъг от {OptionMaxLength} символа.";
        }

        if (input.CorrectIndex < 0 || input.CorrectIndex >= options.Count)
        {
            return "Посочете кой е верният отговор.";
        }

        if (input.Points < 1 || input.Points > MaxQuestionPoints)
        {
            return $"Точките за въпрос трябва да са между 1 и {MaxQuestionPoints}.";
        }

        return null;
    }
}
