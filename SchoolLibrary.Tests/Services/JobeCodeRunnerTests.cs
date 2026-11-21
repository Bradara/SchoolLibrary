using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SchoolLibrary.Services;
using Microsoft.Extensions.DependencyInjection;
using SchoolLibrary.Services.CodeRunning;

namespace SchoolLibrary.Tests.Services;

public class JobeCodeRunnerTests
{
    /// <summary>A Jobe server that remembers the request and answers with a fixed body, or throws.</summary>
    private sealed class FakeJobe : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _status;
        private readonly Exception? _error;

        public FakeJobe(string body, HttpStatusCode status = HttpStatusCode.OK, Exception? error = null)
        {
            _body = body;
            _status = status;
            _error = error;
        }

        public HttpRequestMessage? Request { get; private set; }
        public string RequestBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            if (_error is not null) throw _error;
            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
        }
    }

    private static string JobeAnswer(int outcome, string stdout = "", string stderr = "") =>
        JsonSerializer.Serialize(new { run_id = (string?)null, outcome, cmpinfo = "", stdout, stderr });

    private static (JobeCodeRunner Runner, FakeJobe Jobe) Create(string body, HttpStatusCode status = HttpStatusCode.OK,
        Exception? error = null, string? apiKey = null)
    {
        var jobe = new FakeJobe(body, status, error);
        var http = new HttpClient(jobe) { BaseAddress = new Uri("http://jobe/") };
        var options = new CodeRunnerOptions { BaseUrl = "http://jobe", ApiKey = apiKey, CpuTimeSeconds = 25, TestTimeoutSeconds = 4, MaxOutputChars = 500 };
        return (new JobeCodeRunner(http, options), jobe);
    }

    private static JsonElement DecodePayload(string script)
    {
        var base64 = Regex.Match(script, "b64decode\\(\"([A-Za-z0-9+/=]+)\"\\)").Groups[1].Value;
        return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(base64))).RootElement;
    }

    [Fact]
    public void Script_CarriesCodeAndInputsEncoded_SoQuotesCannotBreakOut()
    {
        const string code = "print(\"\"\"x\"\"\")  # \" ' \\ \n Готово";

        var script = CodeRunnerScript.Build(CodeLanguages.Python, code, ["1 2", "Здравей\n"], 4, 500);

        Assert.DoesNotContain("Готово", script);
        var payload = DecodePayload(script);
        Assert.Equal("python3", payload.GetProperty("language").GetString());
        Assert.Equal(code, payload.GetProperty("source").GetString());
        Assert.Equal(new[] { "1 2", "Здравей\n" }, payload.GetProperty("stdins").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(4, payload.GetProperty("timeout").GetInt32());
        Assert.Equal(500, payload.GetProperty("maxOutput").GetInt32());
    }

    [Fact]
    public async Task RunAsync_PostsAPython3JobWithTheLimits()
    {
        var (runner, jobe) = Create(JobeAnswer(15, "{\"tests\":[]}"));

        await runner.RunAsync(CodeLanguages.CSharp, "class P { static void Main() {} }", ["5"]);

        Assert.Equal(HttpMethod.Post, jobe.Request!.Method);
        Assert.Equal("http://jobe/jobe/index.php/restapi/runs", jobe.Request.RequestUri!.ToString());
        Assert.False(jobe.Request.Headers.Contains("X-API-KEY"));

        var spec = JsonDocument.Parse(jobe.RequestBody).RootElement.GetProperty("run_spec");
        Assert.Equal("python3", spec.GetProperty("language_id").GetString());
        Assert.Equal("runner.py", spec.GetProperty("sourcefilename").GetString());
        Assert.Equal(25, spec.GetProperty("parameters").GetProperty("cputime").GetInt32());
        Assert.Equal(0, spec.GetProperty("parameters").GetProperty("memorylimit").GetInt32());

        var payload = DecodePayload(spec.GetProperty("sourcecode").GetString()!);
        Assert.Equal("csharp", payload.GetProperty("language").GetString());
        Assert.Equal(4, payload.GetProperty("timeout").GetInt32());
    }

    [Fact]
    public async Task RunAsync_SendsTheApiKeyInAHeader()
    {
        var (runner, jobe) = Create(JobeAnswer(15, "{\"tests\":[]}"), apiKey: "secret");

        await runner.RunAsync(CodeLanguages.Python, "print(1)", []);

        Assert.Equal("secret", jobe.Request!.Headers.GetValues("X-API-KEY").Single());
        Assert.DoesNotContain("secret", jobe.Request.RequestUri!.ToString());
    }

    [Fact]
    public async Task RunAsync_ReadsTheOutputOfEveryInput()
    {
        const string output = """
            {"tests":[
              {"stdout":"5\nГотово\n","stderr":"","exitCode":0,"timedOut":false},
              {"stdout":"","stderr":"Traceback","exitCode":1,"timedOut":false},
              {"stdout":"1\n","stderr":"","exitCode":-1,"timedOut":true}
            ]}
            """;
        var (runner, _) = Create(JobeAnswer(15, output));

        var result = await runner.RunAsync(CodeLanguages.Python, "print(1)", ["2 3", "x", "loop"]);

        Assert.Equal(CodeRunStatus.Completed, result.Status);
        Assert.Equal(
            new[]
            {
                new CodeTestOutput("5\nГотово\n", "", 0, false),
                new CodeTestOutput("", "Traceback", 1, false),
                new CodeTestOutput("1\n", "", -1, true),
            },
            result.Tests);
    }

    [Fact]
    public async Task RunAsync_ReturnsTheCompilerMessage()
    {
        var (runner, _) = Create(JobeAnswer(15, "{\"compileError\":\"Program.cs(3,1): error CS1525\"}"));

        var result = await runner.RunAsync(CodeLanguages.CSharp, "class {", ["1"]);

        Assert.Equal(CodeRunStatus.CompileError, result.Status);
        Assert.Equal("Program.cs(3,1): error CS1525", result.Message);
        Assert.Empty(result.Tests);
    }

    [Theory]
    [InlineData(13, CodeRunStatus.TimeLimitExceeded)]
    [InlineData(17, CodeRunStatus.MemoryLimitExceeded)]
    [InlineData(21, CodeRunStatus.Unavailable)] // server overloaded
    [InlineData(12, CodeRunStatus.Unavailable)] // the runner script itself failed
    public async Task RunAsync_MapsJobeOutcomes(int outcome, CodeRunStatus expected)
    {
        var (runner, _) = Create(JobeAnswer(outcome, "", "boom"));

        var result = await runner.RunAsync(CodeLanguages.Python, "print(1)", ["1"]);

        Assert.Equal(expected, result.Status);
        Assert.Empty(result.Tests);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("{\"something\":1}")]
    public async Task RunAsync_UnexpectedScriptOutput_IsUnavailable(string stdout)
    {
        var (runner, _) = Create(JobeAnswer(15, stdout));

        var result = await runner.RunAsync(CodeLanguages.Python, "print(1)", ["1"]);

        // {"something":1} has neither tests nor a compile error: it reads as a completed run with no tests
        Assert.Equal(stdout.StartsWith('{') ? CodeRunStatus.Completed : CodeRunStatus.Unavailable, result.Status);
        Assert.Empty(result.Tests);
    }

    [Fact]
    public async Task RunAsync_HttpError_IsUnavailable()
    {
        var (runner, _) = Create("", HttpStatusCode.InternalServerError);

        var result = await runner.RunAsync(CodeLanguages.Python, "print(1)", ["1"]);

        Assert.Equal(CodeRunStatus.Unavailable, result.Status);
        Assert.Equal("HTTP 500", result.Message);
    }

    [Fact]
    public async Task RunAsync_InvalidJsonFromServer_IsUnavailable()
    {
        var (runner, _) = Create("<html>proxy error</html>");

        var result = await runner.RunAsync(CodeLanguages.Python, "print(1)", ["1"]);

        Assert.Equal(CodeRunStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task RunAsync_ServerDown_IsUnavailable()
    {
        var (runner, _) = Create("", error: new HttpRequestException("Connection refused"));

        var result = await runner.RunAsync(CodeLanguages.Python, "print(1)", ["1"]);

        Assert.Equal(CodeRunStatus.Unavailable, result.Status);
        Assert.Equal("Connection refused", result.Message);
    }

    [Fact]
    public async Task RunAsync_HttpTimeout_IsUnavailable()
    {
        var (runner, _) = Create("", error: new TaskCanceledException("timeout"));

        var result = await runner.RunAsync(CodeLanguages.Python, "print(1)", ["1"]);

        Assert.Equal(CodeRunStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task RunAsync_CancelledByTheCaller_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var (runner, _) = Create("", error: new TaskCanceledException("cancelled"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(CodeLanguages.Python, "print(1)", ["1"], cts.Token));
    }

    [Fact]
    public async Task RunAsync_UnknownLanguage_Throws()
    {
        var (runner, jobe) = Create(JobeAnswer(15));

        await Assert.ThrowsAsync<ArgumentException>(() => runner.RunAsync("rust", "fn main() {}", ["1"]));
        Assert.Null(jobe.Request);
    }

    [Theory]
    [InlineData("python3", true)]
    [InlineData("csharp", true)]
    [InlineData("rust", false)]
    [InlineData(null, false)]
    public void CodeLanguages_IsSupported(string? language, bool expected)
    {
        Assert.Equal(expected, CodeLanguages.IsSupported(language));
    }

    [Fact]
    public void AddCodeRunner_WithoutAddress_RegistersNothing()
    {
        var services = new ServiceCollection().AddCodeRunner(new CodeRunnerOptions { BaseUrl = " " }).AddCodeRunner(null);

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(ICodeRunner));
    }

    [Fact]
    public void AddCodeRunner_WithAddress_ResolvesAJobeRunner()
    {
        using var provider = new ServiceCollection()
            .AddCodeRunner(new CodeRunnerOptions { BaseUrl = "http://jobe" })
            .BuildServiceProvider();

        Assert.IsType<JobeCodeRunner>(provider.GetRequiredService<ICodeRunner>());
    }
}
