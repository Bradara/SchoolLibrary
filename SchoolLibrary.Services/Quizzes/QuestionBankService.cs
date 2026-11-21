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
            Order = maxOrder + 1
        };
        QuestionContent.Apply(question, input);

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

        if ((await SolvedAmongAsync(new[] { question.Id })).Count > 0)
        {
            return await UpdateSolvedAsync(question, input);
        }

        _context.AnswerOptions.RemoveRange(question.Options);
        QuestionContent.Apply(question, input);

        await _context.SaveChangesAsync();

        return ServiceResult<Question>.Success(question);
    }

    public async Task<int> CountAnsweredAttemptsAsync(int questionId)
        => await _context.AttemptAnswers
            .Where(a => a.QuestionId == questionId && a.Attempt.SubmittedOn != null)
            .Select(a => a.AttemptId)
            .Distinct()
            .CountAsync();

    // An answered question: edit the options in place, keep what students chose valid, then regrade.
    // In a relational database both saves run in one transaction (through the retrying execution strategy).
    private async Task<ServiceResult<Question>> UpdateSolvedAsync(Question question, QuestionInput input)
    {
        if (input.Type != question.Type)
        {
            return ServiceResult<Question>.Failure(ServiceError.Conflict,
                "Въпросът вече е решаван в тест, затова видът му не може да се променя.");
        }

        if (!_context.Database.IsRelational())
        {
            return await EditAndRegradeAsync(question, input);
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            var result = await EditAndRegradeAsync(question, input);
            if (result.Succeeded)
            {
                await transaction.CommitAsync();
            }

            return result;
        });
    }

    private async Task<ServiceResult<Question>> EditAndRegradeAsync(Question question, QuestionInput input)
    {
        // An option a student picked or ticked (or a matching item they answered) cannot go away
        var kept = QuestionContent.KeptOptionIds(question, input).OfType<int>().ToHashSet();
        var removedIds = question.Options.Where(o => !kept.Contains(o.Id)).Select(o => o.Id).ToList();
        var chosen = await _context.AttemptAnswers
            .Where(a => a.SelectedOptionId != null && removedIds.Contains(a.SelectedOptionId.Value))
            .Select(a => a.SelectedOptionId!.Value)
            .Concat(_context.AttemptAnswerChoices
                .Where(c => removedIds.Contains(c.OptionId))
                .Select(c => c.OptionId))
            .ToListAsync();
        if (chosen.Count > 0)
        {
            var text = question.Options.First(o => o.Id == chosen[0]).Text;
            return ServiceResult<Question>.Failure(ServiceError.Conflict,
                $"Отговорът „{text}“ е избиран от ученици и не може да се премахне. Можете да промените текста му или да го направите грешен.");
        }

        // A matching answer points at the first option with the picked text; remember the texts the students
        // picked, so that after the edit they point at an option with that text again (or at none)
        var picks = await _context.AttemptAnswerChoices
            .Where(c => c.AttemptAnswer.QuestionId == question.Id && c.MatchedOptionId != null)
            .ToListAsync();
        var oldTexts = question.Options.ToDictionary(o => o.Id, o => o.MatchText);
        var pickedTexts = picks.ToDictionary(c => c, c => oldTexts.GetValueOrDefault(c.MatchedOptionId!.Value));

        var removed = QuestionContent.ApplyInPlace(question, input);

        foreach (var (pick, pickedText) in pickedTexts)
        {
            var target = MatchingAnswers.OptionFor(question, pickedText);
            if (target == null)
            {
                // That answer is no longer offered: the item counts as unanswered
                _context.AttemptAnswerChoices.Remove(pick);
            }
            else
            {
                // Through the navigation, so a newly added option gets its id on save
                pick.MatchedOption = target;
            }
        }

        _context.AnswerOptions.RemoveRange(removed);
        await _context.SaveChangesAsync();

        await RegradeAsync(question.Id);
        return ServiceResult<Question>.Success(question);
    }

    // Regrades every submitted attempt that answered the question, from the answers it stored
    private async Task RegradeAsync(int questionId)
    {
        var attempts = await _context.QuizAttempts
            .Where(a => a.SubmittedOn != null && a.Answers.Any(answer => answer.QuestionId == questionId))
            .Include(a => a.Quiz)
                .ThenInclude(q => q.Questions)
                    .ThenInclude(q => q.Options)
            .Include(a => a.Questions)
                .ThenInclude(aq => aq.Question)
                    .ThenInclude(q => q.Options)
            .Include(a => a.Answers)
                .ThenInclude(answer => answer.Choices)
            .ToListAsync();

        foreach (var attempt in attempts)
        {
            var result = QuizAttemptResults.Build(attempt);
            var byQuestion = result.Questions.ToDictionary(r => r.Question.Id);
            foreach (var answer in attempt.Answers)
            {
                if (byQuestion.TryGetValue(answer.QuestionId, out var graded))
                {
                    answer.AwardedPoints = graded.AwardedPoints;
                    answer.IsCorrect = graded.IsCorrect;
                }
            }

            attempt.Score = result.Score;
            attempt.MaxScore = result.MaxScore;
        }

        await _context.SaveChangesAsync();
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

        if ((await SolvedAmongAsync(new[] { question.Id })).Count > 0)
        {
            return ServiceResult<Question>.Failure(ServiceError.Conflict,
                "Въпросът вече е решаван в тест и не може да се изтрие, за да се запазят резултатите на учениците.");
        }

        await RemoveAsync(new[] { question });
        return ServiceResult<Question>.Success(question);
    }

    public async Task<ServiceResult<QuestionDeleteSummary>> DeleteQuestionsAsync(IReadOnlyCollection<int> questionIds, string userId, bool isAdministrator)
    {
        var ids = questionIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return ServiceResult<QuestionDeleteSummary>.Success(new QuestionDeleteSummary(0, 0));
        }

        // A teacher can only delete questions from sections they own; skip the rest
        var questions = (await _context.Questions
                .Include(q => q.Section)
                .Where(q => ids.Contains(q.Id) && q.SectionId != null)
                .ToListAsync())
            .Where(q => SectionPermissions.CanManage(q.Section!, userId, isAdministrator))
            .ToList();

        var solved = await SolvedAmongAsync(questions.Select(q => q.Id).ToList());
        var deletable = questions.Where(q => !solved.Contains(q.Id)).ToList();

        await RemoveAsync(deletable);
        return ServiceResult<QuestionDeleteSummary>.Success(new QuestionDeleteSummary(deletable.Count, questions.Count - deletable.Count));
    }

    // The ids among the given ones that students have already been served or have answered
    private async Task<HashSet<int>> SolvedAmongAsync(IReadOnlyCollection<int> ids)
    {
        var served = await _context.AttemptQuestions
            .Where(a => ids.Contains(a.QuestionId))
            .Select(a => a.QuestionId)
            .ToListAsync();
        var answered = await _context.AttemptAnswers
            .Where(a => ids.Contains(a.QuestionId))
            .Select(a => a.QuestionId)
            .ToListAsync();
        return served.Concat(answered).ToHashSet();
    }

    // Takes the questions out of the quizzes they are pinned to (the database restricts deleting a pinned
    // question), then deletes them; cascade removes their options
    private async Task RemoveAsync(IReadOnlyCollection<Question> questions)
    {
        if (questions.Count == 0)
        {
            return;
        }

        var ids = questions.Select(q => q.Id).ToList();
        var pins = await _context.QuizItems
            .Where(i => i.QuestionId != null && ids.Contains(i.QuestionId.Value))
            .ToListAsync();
        _context.QuizItems.RemoveRange(pins);
        _context.Questions.RemoveRange(questions);
        await _context.SaveChangesAsync();
    }

    public async Task<ServiceResult<int>> MoveQuestionsAsync(IReadOnlyCollection<int> questionIds, int targetSectionId, string userId, bool isAdministrator)
    {
        var target = await _context.Sections.FirstOrDefaultAsync(s => s.Id == targetSectionId);
        if (target == null)
        {
            return ServiceResult<int>.Failure(ServiceError.NotFound, "Разделът не е намерен.");
        }

        if (!SectionPermissions.CanManage(target, userId, isAdministrator))
        {
            return ServiceResult<int>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        if (questionIds.Count == 0)
        {
            return ServiceResult<int>.Success(0);
        }

        var ids = questionIds.Distinct().ToList();
        var questions = await _context.Questions
            .Include(q => q.Section)
            .Where(q => ids.Contains(q.Id) && q.SectionId != null)
            .ToListAsync();

        var order = await _context.Questions
            .Where(q => q.SectionId == targetSectionId)
            .Select(q => (int?)q.Order)
            .MaxAsync() ?? 0;

        var moved = 0;
        foreach (var question in questions)
        {
            // A teacher can only move questions out of sections they own; skip the rest
            if (question.SectionId == targetSectionId || !SectionPermissions.CanManage(question.Section!, userId, isAdministrator))
            {
                continue;
            }

            question.SectionId = targetSectionId;
            question.Order = ++order;
            moved++;
        }

        if (moved > 0)
        {
            await _context.SaveChangesAsync();
        }

        return ServiceResult<int>.Success(moved);
    }

    public async Task<ServiceResult<ImportSummary>> ImportAsync(int sectionId, string text, string userId, bool isAdministrator)
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

        // Moodle XML starts with a tag (optionally after a BOM/whitespace); anything else is Aiken text
        var parsed = text.TrimStart('﻿', ' ', '\t', '\r', '\n').StartsWith('<')
            ? MoodleXmlParser.Parse(text)
            : AikenParser.Parse(text);
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
                Order = ++maxOrder
            };
            QuestionContent.Apply(question, input);
            _context.Questions.Add(question);
            added++;
        }

        await _context.SaveChangesAsync();
        return ServiceResult<ImportSummary>.Success(new ImportSummary(added, errors));
    }

    private static string Shorten(string text)
        => text.Length <= 50 ? text : text[..50] + "…";

}
