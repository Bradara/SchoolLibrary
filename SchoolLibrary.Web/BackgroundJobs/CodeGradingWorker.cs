using SchoolLibrary.Services.CodeRunning;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.BackgroundJobs
{
    /// <summary>
    /// Checks the programs of submitted exams in the background. It works through the waiting answers a round at
    /// a time, then sleeps until an exam is submitted (or for a while, to pick up anything missed). When the sandbox
    /// server cannot check a program, it waits longer before trying again, so a server that is down is not hammered.
    /// </summary>
    public class CodeGradingWorker : BackgroundService
    {
        /// <summary>Programs per round: as many as may run on the server at once.</summary>
        public const int BatchSize = 4;

        public static readonly TimeSpan IdleWait = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan RetryWait = TimeSpan.FromSeconds(60);

        private readonly IServiceScopeFactory _scopes;
        private readonly CodeGradingSignal _signal;
        private readonly ILogger<CodeGradingWorker> _logger;

        public CodeGradingWorker(IServiceScopeFactory scopes, CodeGradingSignal signal, ILogger<CodeGradingWorker> logger)
        {
            _scopes = scopes;
            _signal = signal;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _signal.WaitAsync(await RunRoundAsync(stoppingToken), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        /// <summary>Checks one round of waiting programs and says how long to wait before the next round.</summary>
        public async Task<TimeSpan> RunRoundAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var grading = scope.ServiceProvider.GetRequiredService<ICodeGradingService>();
                var run = await grading.GradePendingAsync(BatchSize, cancellationToken);
                if (run.ServerUnavailable)
                {
                    _logger.LogWarning("The code server could not check {Remaining} waiting program(s); retrying later.", run.Remaining);
                    return RetryWait;
                }

                // More waiting: go on at once
                return run.Remaining > 0 ? TimeSpan.Zero : IdleWait;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Checking the waiting programs failed.");
                return RetryWait;
            }
        }
    }
}
