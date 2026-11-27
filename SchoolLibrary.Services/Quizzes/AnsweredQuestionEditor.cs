using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Quizzes;

// Edits a question that students have already been served or have answered: the options are updated in place,
// what the students chose stays valid, and every submitted attempt that contains the question is regraded.
// Shared by the question bank and by the questions written directly in a quiz.
internal class AnsweredQuestionEditor
{
    private readonly ApplicationDbContext _context;

    public AnsweredQuestionEditor(ApplicationDbContext context)
    {
        _context = context;
    }

    // The ids among the given ones that students have already been served or have answered
    public async Task<HashSet<int>> SolvedAmongAsync(IReadOnlyCollection<int> ids)
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

    // Submitted attempts that answered the question, were served it, or (older attempts without a list of
    // served questions) belong to the quiz the question is written in
    public async Task<int> CountAttemptsAsync(int questionId)
        => await AttemptsWith(questionId).CountAsync();

    // In a relational database both saves run in one transaction (through the retrying execution strategy)
    public async Task<ServiceResult<Question>> UpdateAsync(Question question, QuestionInput input)
    {
        if (input.Type != question.Type)
        {
            return ServiceResult<Question>.Failure(ServiceError.Conflict,
                "Въпросът вече е решаван в тест, затова видът му не може да се променя.");
        }

        // A program has to be run again on the sandbox server to regrade it; that comes with the grading queue
        if (question.Type == QuestionType.Code)
        {
            return ServiceResult<Question>.Failure(ServiceError.Conflict,
                "Въпросът с код вече е решаван в тест и засега не може да се редактира.");
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

    // Regrades every submitted attempt that contains the question, from the answers it stored
    // (a skipped question still counts towards the maximum, so its points matter too)
    private async Task RegradeAsync(int questionId)
    {
        var attempts = await AttemptsWith(questionId)
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
            // The automatic points are stored; the teacher's points stay as they are and count in the score
            var automatic = QuizAttemptResults.BuildAutomatic(attempt);
            var byQuestion = automatic.Questions.ToDictionary(r => r.Question.Id);
            foreach (var answer in attempt.Answers)
            {
                if (byQuestion.TryGetValue(answer.QuestionId, out var graded))
                {
                    answer.AwardedPoints = graded.AwardedPoints;
                    answer.IsCorrect = graded.IsCorrect;
                }
            }

            var result = QuizAttemptResults.Build(attempt);
            attempt.Score = result.Score;
            attempt.MaxScore = result.MaxScore;
        }

        await _context.SaveChangesAsync();
    }

    private IQueryable<QuizAttempt> AttemptsWith(int questionId)
        => _context.QuizAttempts.Where(a => a.SubmittedOn != null
            && (a.Answers.Any(answer => answer.QuestionId == questionId)
                || a.Questions.Any(aq => aq.QuestionId == questionId)
                || (!a.Questions.Any() && a.Quiz.Questions.Any(q => q.Id == questionId))));
}
