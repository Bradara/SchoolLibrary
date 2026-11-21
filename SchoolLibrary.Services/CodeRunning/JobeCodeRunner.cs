using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SchoolLibrary.Services.CodeRunning;

/// <summary>
/// Runs code on a Jobe server (the sandbox used by CodeRunner in Moodle) through its REST API.
/// Every language is sent as a Python 3 job (<see cref="CodeRunnerScript"/>), so C# only needs mcs and mono on the server.
/// </summary>
public class JobeCodeRunner : ICodeRunner
{
    public const string HttpClientName = "Jobe";
    public const string RunsPath = "jobe/index.php/restapi/runs";

    // Jobe outcome codes (restapi documentation)
    private const int OutcomeOk = 15;
    private const int OutcomeTimeLimit = 13;
    private const int OutcomeMemoryLimit = 17;

    private readonly HttpClient _http;
    private readonly CodeRunnerOptions _options;

    public JobeCodeRunner(HttpClient http, CodeRunnerOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<CodeRunResult> RunAsync(string language, string sourceCode, IReadOnlyList<string> stdins, CancellationToken cancellationToken = default)
    {
        if (!CodeLanguages.IsSupported(language))
        {
            throw new ArgumentException($"Unsupported language '{language}'.", nameof(language));
        }

        var request = new
        {
            run_spec = new
            {
                language_id = "python3",
                sourcefilename = CodeRunnerScript.FileName,
                sourcecode = CodeRunnerScript.Build(language, sourceCode, stdins, _options.TestTimeoutSeconds, _options.MaxOutputChars),
                parameters = new
                {
                    cputime = _options.CpuTimeSeconds,
                    memorylimit = 0, // mono reserves a lot of virtual memory; 0 = no limit inside the sandbox
                    numprocs = 100,  // mono starts many threads
                },
            },
        };

        JobeResponse? response;
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, RunsPath) { Content = JsonContent.Create(request) };
            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                message.Headers.Add("X-API-KEY", _options.ApiKey);
            }

            using var answer = await _http.SendAsync(message, cancellationToken);
            if (!answer.IsSuccessStatusCode)
            {
                return CodeRunResult.Failed(CodeRunStatus.Unavailable, $"HTTP {(int)answer.StatusCode}");
            }

            response = await answer.Content.ReadFromJsonAsync<JobeResponse>(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return CodeRunResult.Failed(CodeRunStatus.Unavailable, ex.Message);
        }

        return response?.Outcome switch
        {
            OutcomeOk => ReadScriptOutput(response.Stdout),
            OutcomeTimeLimit => CodeRunResult.Failed(CodeRunStatus.TimeLimitExceeded),
            OutcomeMemoryLimit => CodeRunResult.Failed(CodeRunStatus.MemoryLimitExceeded),
            null => CodeRunResult.Failed(CodeRunStatus.Unavailable, "Empty answer"),
            var other => CodeRunResult.Failed(CodeRunStatus.Unavailable, $"Jobe outcome {other}: {response.Stderr}".Trim()),
        };
    }

    private static CodeRunResult ReadScriptOutput(string? stdout)
    {
        ScriptOutput? output;
        try
        {
            output = string.IsNullOrWhiteSpace(stdout) ? null : JsonSerializer.Deserialize<ScriptOutput>(stdout);
        }
        catch (JsonException)
        {
            output = null;
        }

        if (output is null)
        {
            return CodeRunResult.Failed(CodeRunStatus.Unavailable, "Unexpected output from the runner");
        }

        if (output.CompileError is not null)
        {
            return CodeRunResult.Failed(CodeRunStatus.CompileError, output.CompileError);
        }

        var tests = (output.Tests ?? [])
            .Select(t => new CodeTestOutput(t.Stdout ?? "", t.Stderr ?? "", t.ExitCode, t.TimedOut))
            .ToList();
        return new CodeRunResult(CodeRunStatus.Completed, tests);
    }

    private sealed record JobeResponse(
        [property: JsonPropertyName("outcome")] int Outcome,
        [property: JsonPropertyName("stdout")] string? Stdout,
        [property: JsonPropertyName("stderr")] string? Stderr);

    private sealed record ScriptOutput(
        [property: JsonPropertyName("compileError")] string? CompileError,
        [property: JsonPropertyName("tests")] List<ScriptTest>? Tests);

    private sealed record ScriptTest(
        [property: JsonPropertyName("stdout")] string? Stdout,
        [property: JsonPropertyName("stderr")] string? Stderr,
        [property: JsonPropertyName("exitCode")] int ExitCode,
        [property: JsonPropertyName("timedOut")] bool TimedOut);
}
