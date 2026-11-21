using System.Text;
using System.Text.Json;

namespace SchoolLibrary.Services.CodeRunning;

/// <summary>
/// Builds the Python 3 program that Jobe runs. It writes the student's code to a file, compiles it once
/// (mcs for C#, a syntax check for Python), runs it for every input and prints one JSON line with the results.
/// The code and the inputs travel base64-encoded inside the script, so nothing in them can break out of it.
/// </summary>
public static class CodeRunnerScript
{
    public const string FileName = "runner.py";

    private const string Template = """
        import base64, json, os, subprocess, sys

        p = json.loads(base64.b64decode("__PAYLOAD__").decode("utf-8"))
        env = dict(os.environ, LANG="C.UTF-8", LC_ALL="C.UTF-8", HOME=os.getcwd())
        limit = p["maxOutput"]

        def text(value):
            if value is None:
                return ""
            if isinstance(value, bytes):
                value = value.decode("utf-8", errors="replace")
            return value if len(value) <= limit else value[:limit] + "\n..."

        def build(command):
            done = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                  encoding="utf-8", errors="replace", env=env)
            if done.returncode != 0:
                print(json.dumps({"compileError": text(done.stdout)}))
                sys.exit(0)

        if p["language"] == "csharp":
            with open("Program.cs", "w", encoding="utf-8") as f:
                f.write(p["source"])
            build(["mcs", "-nologo", "-out:Program.exe", "Program.cs"])
            command = ["mono", "Program.exe"]
        else:
            with open("program.py", "w", encoding="utf-8") as f:
                f.write(p["source"])
            build(["python3", "-m", "py_compile", "program.py"])
            command = ["python3", "program.py"]

        tests = []
        for stdin in p["stdins"]:
            try:
                r = subprocess.run(command, input=stdin, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                   encoding="utf-8", errors="replace", env=env, timeout=p["timeout"])
                tests.append({"stdout": text(r.stdout), "stderr": text(r.stderr), "exitCode": r.returncode, "timedOut": False})
            except subprocess.TimeoutExpired as e:
                tests.append({"stdout": text(e.stdout), "stderr": text(e.stderr), "exitCode": -1, "timedOut": True})

        print(json.dumps({"tests": tests}))
        """;

    public static string Build(string language, string sourceCode, IReadOnlyList<string> stdins, int testTimeoutSeconds, int maxOutputChars)
    {
        var payload = JsonSerializer.Serialize(new
        {
            language,
            source = sourceCode,
            stdins,
            timeout = testTimeoutSeconds,
            maxOutput = maxOutputChars,
        });

        return Template.Replace("__PAYLOAD__", Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)));
    }
}
