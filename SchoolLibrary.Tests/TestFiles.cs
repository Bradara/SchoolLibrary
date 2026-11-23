namespace SchoolLibrary.Tests;

/// <summary>Finds files of the solution that tests read (found by walking up from the test output folder).</summary>
internal static class TestFiles
{
    public static string BootstrapIconsCss() =>
        Find(Path.Combine("SchoolLibrary.Web", "wwwroot", "lib", "bootstrap-icons", "bootstrap-icons.min.css"));

    private static string Find(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("A file of the solution was not found.", relative);
    }
}
