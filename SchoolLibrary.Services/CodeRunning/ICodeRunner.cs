namespace SchoolLibrary.Services.CodeRunning;

/// <summary>
/// Builds a program and runs it with several inputs on a separate sandbox server.
/// The web application itself never executes submitted code.
/// </summary>
public interface ICodeRunner
{
    /// <summary>Compiles <paramref name="sourceCode"/> once, then runs it once for every input in <paramref name="stdins"/>.</summary>
    /// <exception cref="ArgumentException">The language is not one of <see cref="CodeLanguages.All"/>.</exception>
    Task<CodeRunResult> RunAsync(string language, string sourceCode, IReadOnlyList<string> stdins, CancellationToken cancellationToken = default);
}
