namespace SchoolLibrary.Services.Quizzes;

/// <summary>Checks the code answers of submitted exams that wait in the background.</summary>
public interface ICodeGradingService
{
    /// <summary>
    /// Runs up to <paramref name="maxAnswers"/> waiting programs (the oldest first), stores their results and
    /// updates the scores of their attempts. Programs the server could not check stay waiting.
    /// </summary>
    Task<CodeGradingRun> GradePendingAsync(int maxAnswers, CancellationToken cancellationToken = default);
}

/// <summary>What one round of checking did.</summary>
/// <param name="Graded">Programs checked and stored.</param>
/// <param name="Remaining">Programs still waiting after the round.</param>
/// <param name="ServerUnavailable">The server could not check some program (or none is configured); try again later.</param>
public record CodeGradingRun(int Graded, int Remaining, bool ServerUnavailable);
