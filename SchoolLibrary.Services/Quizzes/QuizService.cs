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
    public const int MaxClassNumber = 99;

    private readonly ApplicationDbContext _context;
    private readonly IQuizItemService _items;

    public QuizService(ApplicationDbContext context, IQuizItemService items)
    {
        _context = context;
        _items = items;
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

        var questionCounts = await CountQuestionsAsync(ids);

        var attemptCounts = await _context.QuizAttempts
            .Where(x => ids.Contains(x.QuizId) && x.SubmittedOn != null && x.ExamAssignmentId == null)
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
            ShuffleQuestions = input.ShuffleQuestions,
            ShuffleOptions = input.ShuffleOptions,
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
        quiz.ShuffleQuestions = input.ShuffleQuestions;
        quiz.ShuffleOptions = input.ShuffleOptions;

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

        // Content can be questions attached directly (legacy) or items that pull from the bank
        if (published
            && !await _context.Questions.AnyAsync(q => q.QuizId == id)
            && !await _context.QuizItems.AnyAsync(i => i.QuizId == id))
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
            Order = maxOrder + 1
        };
        QuestionContent.Apply(question, input);

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

        // Replace the options wholesale; the question keeps its place in the quiz
        _context.AnswerOptions.RemoveRange(question.Options);
        QuestionContent.Apply(question, input);

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
        var questionCounts = await CountQuestionsAsync(ids);

        var items = quizzes
            .Select(q => new QuizListItem(q, questionCounts.GetValueOrDefault(q.Id), 0))
            .ToList();

        return new PagedResult<QuizListItem>(items, totalCount, page, pageSize);
    }

    public async Task<ServiceResult<PracticeSet>> GetPracticeSetAsync(int id)
    {
        var quiz = await _context.Quizzes.AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == id && q.Mode == QuizMode.Practice && q.IsPublished);
        if (quiz == null)
        {
            return ServiceResult<PracticeSet>.Failure(ServiceError.NotFound, "Упражнението не е намерено.");
        }

        // Resolve the items into this run's questions (random draws vary each time practice is opened)
        var questions = await _items.ResolveAsync(id);
        return ServiceResult<PracticeSet>.Success(new PracticeSet(quiz, questions));
    }

    public async Task<ServiceResult<QuizResult>> GradePracticeAsync(int quizId, IReadOnlyCollection<int> questionIds, IReadOnlyDictionary<int, SubmittedAnswer> answers)
    {
        var quiz = await _context.Quizzes.AsNoTracking()
            .FirstOrDefaultAsync(q => q.Id == quizId && q.Mode == QuizMode.Practice && q.IsPublished);
        if (quiz == null)
        {
            return ServiceResult<QuizResult>.Failure(ServiceError.NotFound, "Упражнението не е намерено.");
        }

        // Grade exactly the questions the student was shown (carried back from the form), in that order
        var loaded = await _context.Questions.AsNoTracking()
            .Where(q => questionIds.Contains(q.Id))
            .Include(q => q.Options.OrderBy(o => o.Order))
            .ToListAsync();
        var ordered = questionIds
            .Select(qid => loaded.FirstOrDefault(q => q.Id == qid))
            .Where(q => q != null)
            .Select(q => q!)
            .ToList();

        var result = QuizGrading.Grade(quiz, ordered, answers);
        return ServiceResult<QuizResult>.Success(result);
    }

    public async Task<ServiceResult<ExamEntry>> FindExamAsync(string code)
    {
        var normalizedCode = NormalizeCode(code);

        var assignment = await _context.ExamAssignments.AsNoTracking()
            .Include(e => e.Quiz)
            .Include(e => e.SchoolClass)
            .FirstOrDefaultAsync(e => e.Code == normalizedCode);
        if (assignment != null)
        {
            return ServiceResult<ExamEntry>.Success(new ExamEntry(
                normalizedCode, assignment.Quiz.Title, assignment.SchoolClass.Name, assignment.TimeLimitMinutes, assignment.IsOpen));
        }

        var quiz = await _context.Quizzes.AsNoTracking()
            .FirstOrDefaultAsync(q => q.Code == normalizedCode && q.Mode == QuizMode.Exam && q.IsPublished);
        if (quiz != null)
        {
            return ServiceResult<ExamEntry>.Success(new ExamEntry(normalizedCode, quiz.Title, null, quiz.TimeLimitMinutes, true));
        }

        return ServiceResult<ExamEntry>.Failure(ServiceError.NotFound, "Няма активен изпит с този код.");
    }

    public async Task<ServiceResult<QuizAttempt>> StartExamAsync(string code, string studentName, string? studentNumber)
    {
        var normalizedCode = NormalizeCode(code);
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

        var assignment = await _context.ExamAssignments
            .Include(e => e.Quiz)
            .FirstOrDefaultAsync(e => e.Code == normalizedCode);
        if (assignment != null)
        {
            return await StartClassExamAsync(assignment, name, number);
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

        // Resolve the quiz's items into this student's own set of questions (random draws fixed now)
        var served = await _items.ResolveAsync(quiz.Id);

        var attempt = new QuizAttempt
        {
            Token = Guid.NewGuid(),
            QuizId = quiz.Id,
            StudentName = name,
            StudentNumber = number,
            StartedOn = DateTime.UtcNow,
            Score = 0,
            MaxScore = served.Sum(q => q.Points)
        };

        var order = 0;
        foreach (var question in served)
        {
            attempt.Questions.Add(new AttemptQuestion { QuestionId = question.Id, Order = ++order });
        }

        _context.QuizAttempts.Add(attempt);
        await _context.SaveChangesAsync();

        return ServiceResult<QuizAttempt>.Success(attempt);
    }

    public async Task<QuizAttempt?> GetExamAttemptAsync(Guid token)
    {
        var attempt = await _context.QuizAttempts
            .AsNoTracking()
            .Include(a => a.Quiz)
            .Include(a => a.ExamAssignment!)
                .ThenInclude(e => e.SchoolClass)
            .Include(a => a.Questions.OrderBy(x => x.Order))
                .ThenInclude(aq => aq.Question)
                    .ThenInclude(q => q.Options.OrderBy(o => o.Order))
            .FirstOrDefaultAsync(a => a.Token == token);

        QuizAttemptResults.ApplyOptionOrder(attempt);
        return attempt;
    }

    public async Task<ServiceResult<QuizResult>> SubmitExamAsync(Guid token, IReadOnlyDictionary<int, SubmittedAnswer> answers)
    {
        var attempt = await _context.QuizAttempts
            .Include(a => a.Quiz)
            .Include(a => a.ExamAssignment!)
                .ThenInclude(e => e.SchoolClass)
            .Include(a => a.Questions.OrderBy(x => x.Order))
                .ThenInclude(aq => aq.Question)
                    .ThenInclude(q => q.Options)
            .Include(a => a.Answers).ThenInclude(answer => answer.Choices)
            .FirstOrDefaultAsync(a => a.Token == token);
        if (attempt == null)
        {
            return ServiceResult<QuizResult>.Failure(ServiceError.NotFound, "Опитът не е намерен.");
        }

        // Resubmitting (e.g. a double click or the timer firing after a manual submit) must not regrade
        if (attempt.SubmittedOn != null)
        {
            return ServiceResult<QuizResult>.Success(QuizAttemptResults.Build(attempt));
        }

        var result = QuizGrading.Grade(attempt.Quiz, QuizAttemptResults.SnapshotQuestions(attempt), answers);

        foreach (var questionResult in result.Questions)
        {
            var answer = new AttemptAnswer
            {
                QuestionId = questionResult.Question.Id,
                SelectedOptionId = questionResult.SelectedOptionId,
                ResponseText = questionResult.TypedText,
                IsCorrect = questionResult.IsCorrect,
                AwardedPoints = questionResult.AwardedPoints
            };

            // The ticked options of a multiple-choice question (already limited to this question's options)
            foreach (var optionId in questionResult.Answer.TickedOptionIds)
            {
                answer.Choices.Add(new AttemptAnswerChoice { OptionId = optionId });
            }

            attempt.Answers.Add(answer);
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
            .Include(a => a.ExamAssignment!)
                .ThenInclude(e => e.SchoolClass)
            .Include(a => a.Questions.OrderBy(x => x.Order))
                .ThenInclude(aq => aq.Question)
                    .ThenInclude(q => q.Options.OrderBy(o => o.Order))
            .Include(a => a.Answers).ThenInclude(answer => answer.Choices)
            .FirstOrDefaultAsync(a => a.Token == token);
        if (attempt == null)
        {
            return ServiceResult<QuizResult>.Failure(ServiceError.NotFound, "Опитът не е намерен.");
        }

        if (attempt.SubmittedOn == null)
        {
            return ServiceResult<QuizResult>.Failure(ServiceError.Invalid, "Изпитът още не е предаден.");
        }

        if (!attempt.ResultVisible)
        {
            return ServiceResult<QuizResult>.Failure(ServiceError.Forbidden, "Резултатът още не е активиран от учителя.");
        }

        QuizAttemptResults.ApplyOptionOrder(attempt);
        return ServiceResult<QuizResult>.Success(QuizAttemptResults.Build(attempt));
    }

    public async Task<ServiceResult<ProctorStatus>> RecordLeaveAsync(Guid token)
    {
        var attempt = await _context.QuizAttempts
            .Include(a => a.Quiz)
            .Include(a => a.ExamAssignment)
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
        if (attempt.MaxLeaves is int max && attempt.LeaveCount > max)
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
            .Where(a => a.QuizId == quizId && a.SubmittedOn != null && a.ExamAssignmentId == null);

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

    // A class exam: the number in the class identifies the student (one attempt per number). The same
    // number with the same name resumes the attempt or opens its result; with another name it is refused.
    private async Task<ServiceResult<QuizAttempt>> StartClassExamAsync(ExamAssignment assignment, string name, string? rawNumber)
    {
        if (!int.TryParse(rawNumber, out var parsed) || parsed < 1 || parsed > MaxClassNumber)
        {
            return ServiceResult<QuizAttempt>.Failure(ServiceError.Invalid,
                $"Въведете номер в класа (цяло число от 1 до {MaxClassNumber}).");
        }

        var number = parsed.ToString();
        var existing = await _context.QuizAttempts
            .FirstOrDefaultAsync(a => a.ExamAssignmentId == assignment.Id && a.StudentNumber == number);
        if (existing != null)
        {
            if (!SameName(existing.StudentName, name))
            {
                return ServiceResult<QuizAttempt>.Failure(ServiceError.Conflict,
                    $"Номер {number} вече е използван с друго име. Провери номера или се обърни към учителя.");
            }

            return ServiceResult<QuizAttempt>.Success(existing);
        }

        if (!assignment.IsOpen)
        {
            return ServiceResult<QuizAttempt>.Failure(ServiceError.Conflict, "Изпитът е затворен.");
        }

        var served = await _items.ResolveAsync(assignment.QuizId);
        if (served.Count == 0)
        {
            return ServiceResult<QuizAttempt>.Failure(ServiceError.Invalid, "Изпитът още няма въпроси.");
        }

        var attempt = new QuizAttempt
        {
            Token = Guid.NewGuid(),
            QuizId = assignment.QuizId,
            ExamAssignmentId = assignment.Id,
            StudentName = name,
            StudentNumber = number,
            StartedOn = DateTime.UtcNow,
            Score = 0,
            MaxScore = served.Sum(q => q.Points)
        };

        var order = 0;
        foreach (var question in served)
        {
            attempt.Questions.Add(new AttemptQuestion { QuestionId = question.Id, Order = ++order });
        }

        _context.QuizAttempts.Add(attempt);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Two starts with the same number at once: the unique index lets only one through
            return ServiceResult<QuizAttempt>.Failure(ServiceError.Conflict, "Опитай отново.");
        }

        return ServiceResult<QuizAttempt>.Success(attempt);
    }

    // Names typed by hand: ignore case and repeated spaces
    private static bool SameName(string a, string b)
        => string.Equals(CollapseSpaces(a), CollapseSpaces(b), StringComparison.OrdinalIgnoreCase);

    private static string CollapseSpaces(string value)
        => string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string NormalizeCode(string? code)
        => (code ?? string.Empty).Trim().ToUpperInvariant();

    // How many questions each quiz serves: its own questions, the pinned bank questions, and each random
    // draw (the count asked for, or the whole section when it holds fewer — as ResolveAsync draws)
    private async Task<Dictionary<int, int>> CountQuestionsAsync(IReadOnlyCollection<int> quizIds)
    {
        var counts = await _context.Questions
            .Where(x => x.QuizId != null && quizIds.Contains(x.QuizId.Value))
            .GroupBy(x => x.QuizId!.Value)
            .Select(g => new { QuizId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.QuizId, g => g.Count);

        var items = await _context.QuizItems.AsNoTracking()
            .Where(i => quizIds.Contains(i.QuizId))
            .Select(i => new { i.QuizId, i.Kind, i.QuestionId, i.SectionId, i.PickCount })
            .ToListAsync();
        if (items.Count == 0)
        {
            return counts;
        }

        var sectionIds = items.Where(i => i.SectionId != null).Select(i => i.SectionId!.Value).Distinct().ToList();
        var poolSizes = await _context.Questions
            .Where(q => q.SectionId != null && sectionIds.Contains(q.SectionId.Value))
            .GroupBy(q => q.SectionId!.Value)
            .Select(g => new { SectionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.SectionId, g => g.Count);

        foreach (var item in items)
        {
            var served = item.Kind switch
            {
                QuizItemKind.FixedQuestion when item.QuestionId != null => 1,
                QuizItemKind.RandomFromSection when item.SectionId != null =>
                    Math.Min(item.PickCount ?? int.MaxValue, poolSizes.GetValueOrDefault(item.SectionId.Value)),
                _ => 0
            };
            counts[item.QuizId] = counts.GetValueOrDefault(item.QuizId) + served;
        }

        return counts;
    }

    private async Task<bool> HasAttemptsAsync(int? quizId)
        => quizId != null && await _context.QuizAttempts.AnyAsync(a => a.QuizId == quizId.Value);

    private Task<string> GenerateUniqueCodeAsync() => QuizCode.GenerateUniqueAsync(_context, CodeLength);

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

    /// <summary>Validates a question's type, text, options, correct index and points.</summary>
    public static string? ValidateQuestion(QuestionInput input)
    {
        // The type comes from the form, so an unknown value is rejected rather than stored
        if (input.Type is not (QuestionType.SingleChoice or QuestionType.TrueFalse or QuestionType.MultipleChoice
            or QuestionType.ShortAnswer))
        {
            return "Неподдържан вид въпрос.";
        }

        // A true/false question always has the two fixed options; only which one is correct varies
        if (input.Type == QuestionType.TrueFalse
            && !(input.Options ?? new List<string>()).SequenceEqual(QuestionInput.TrueFalseOptions))
        {
            return "Въпросът „Вярно/Невярно“ има точно два отговора: „Вярно“ и „Невярно“.";
        }

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

        // A short answer needs only one accepted answer; the choice types need something to choose from
        if (input.Type == QuestionType.ShortAnswer && options.Count == 0)
        {
            return "Добавете поне един приемлив отговор.";
        }

        if (input.Type != QuestionType.ShortAnswer && options.Count < MinOptions)
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

        if (input.Type is QuestionType.MultipleChoice or QuestionType.ShortAnswer)
        {
            var fractionError = ValidateFractions(input.Fractions, options.Count);
            if (fractionError != null)
            {
                return fractionError;
            }

            // Some typed answer must be able to earn all the points
            if (input.Type == QuestionType.ShortAnswer && !input.Fractions!.Any(f => f >= AnswerOption.FullCredit))
            {
                return "Поне един приемлив отговор трябва да носи всички точки (100%).";
            }
        }
        else if (input.Fractions != null)
        {
            // Only multiple choice and short answer carry per-option shares; the others mark one correct option
            return "Неподдържан вид въпрос.";
        }
        else if (input.CorrectIndex < 0 || input.CorrectIndex >= options.Count)
        {
            return "Посочете кой е верният отговор.";
        }

        if (input.Points < 1 || input.Points > MaxQuestionPoints)
        {
            return $"Точките за въпрос трябва да са между 1 и {MaxQuestionPoints}.";
        }

        return null;
    }

    // A share per option, each from -100 to 100, with at least one correct (positive) option
    private static string? ValidateFractions(IReadOnlyList<decimal>? fractions, int optionCount)
    {
        if (fractions == null || fractions.Count != optionCount)
        {
            return "Посочете дела на всеки отговор.";
        }

        if (fractions.Any(f => f < -AnswerOption.FullCredit || f > AnswerOption.FullCredit))
        {
            return "Делът на отговор трябва да е между -100% и 100%.";
        }

        if (!fractions.Any(f => f > 0))
        {
            return "Отбележете поне един верен отговор.";
        }

        return null;
    }
}
