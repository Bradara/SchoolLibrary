namespace SchoolLibrary.Services.CodeRunning;

/// <summary>The programming languages a code question can be written in.</summary>
public static class CodeLanguages
{
    public const string Python = "python3";
    public const string CSharp = "csharp";

    /// <summary>Language id → name shown in the interface.</summary>
    public static readonly IReadOnlyDictionary<string, string> All = new Dictionary<string, string>
    {
        [Python] = "Python 3",
        [CSharp] = "C#",
    };

    public static bool IsSupported(string? language) => language is not null && All.ContainsKey(language);
}

/// <summary>How a run on the sandbox server ended.</summary>
public enum CodeRunStatus
{
    /// <summary>The program was built and every input was run; see <see cref="CodeRunResult.Tests"/>.</summary>
    Completed,

    /// <summary>The program did not compile (C#) or has a syntax error (Python); see <see cref="CodeRunResult.Message"/>.</summary>
    CompileError,

    /// <summary>The whole run used up its CPU time on the server.</summary>
    TimeLimitExceeded,

    /// <summary>The whole run used up its memory on the server.</summary>
    MemoryLimitExceeded,

    /// <summary>The server could not be reached, is overloaded or answered with an error. The answer can be checked again later.</summary>
    Unavailable,
}

/// <summary>The output of one run of the program with one input.</summary>
public record CodeTestOutput(string Stdout, string Stderr, int ExitCode, bool TimedOut);

/// <summary>The result of building a program and running it with a list of inputs.</summary>
public record CodeRunResult(CodeRunStatus Status, IReadOnlyList<CodeTestOutput> Tests, string? Message = null)
{
    public static CodeRunResult Failed(CodeRunStatus status, string? message = null) => new(status, [], message);
}

/// <summary>Settings of the sandbox server (section "CodeRunner").</summary>
public class CodeRunnerOptions
{
    /// <summary>Address of the Jobe server, e.g. "http://jobe" inside the Docker network. Empty = code questions are off.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Optional API key, sent in the X-API-KEY header when the server asks for one.</summary>
    public string? ApiKey { get; set; }

    /// <summary>CPU seconds for the whole run (compiling + all inputs).</summary>
    public int CpuTimeSeconds { get; set; } = 20;

    /// <summary>Wall-clock seconds for one input; an endless loop is stopped after this.</summary>
    public int TestTimeoutSeconds { get; set; } = 5;

    /// <summary>Longest output kept from one run (characters); the rest is cut.</summary>
    public int MaxOutputChars { get; set; } = 10_000;

    /// <summary>
    /// How many programs this application sends to the server at the same time; the others wait in line.
    /// Practice quizzes are open to anyone, so this keeps the server (shared with Moodle) from being flooded.
    /// </summary>
    public int MaxConcurrentRuns { get; set; } = 4;

    /// <summary>How long a program waits for its turn before the run is given up as unavailable.</summary>
    public int QueueTimeoutSeconds { get; set; } = 60;
}

/// <summary>The line in front of the sandbox server: at most <see cref="CodeRunnerOptions.MaxConcurrentRuns"/> runs at a time.</summary>
public sealed class CodeRunGate
{
    private readonly SemaphoreSlim _slots;
    private readonly TimeSpan _wait;

    public CodeRunGate(CodeRunnerOptions options)
    {
        _slots = new SemaphoreSlim(Math.Max(1, options.MaxConcurrentRuns));
        _wait = TimeSpan.FromSeconds(Math.Max(1, options.QueueTimeoutSeconds));
    }

    /// <summary>Waits for a free slot; null when none came free in time. Dispose the result to free the slot.</summary>
    public async Task<IDisposable?> EnterAsync(CancellationToken cancellationToken)
        => await _slots.WaitAsync(_wait, cancellationToken) ? new Slot(_slots) : null;

    private sealed class Slot(SemaphoreSlim slots) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                slots.Release();
            }
        }
    }
}
