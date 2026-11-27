using System.Globalization;

namespace SchoolLibrary.Extensions;

/// <summary>File sizes as people read them: "820 KB", "3,4 МБ".</summary>
public static class FileSizes
{
    private static readonly CultureInfo Bulgarian = CultureInfo.GetCultureInfo("bg-BG");

    public static string Format(long bytes)
    {
        if (bytes < 1024 * 1024)
        {
            return Math.Max(1, (long)Math.Ceiling(bytes / 1024.0)).ToString(Bulgarian) + " KB";
        }

        return (bytes / (1024.0 * 1024.0)).ToString("0.#", Bulgarian) + " МБ";
    }
}
