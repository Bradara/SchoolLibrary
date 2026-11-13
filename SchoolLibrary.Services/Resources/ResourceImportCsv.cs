using System.Text;

namespace SchoolLibrary.Services.Resources;

/// <summary>File-level helpers for the CSV import: reading the uploaded bytes and building the template.</summary>
public static class ResourceImportCsv
{
    public const string TemplateFileName = "free-resources-template.csv";

    private const string Header = "title;author;url;license;subjects;grades;source;description";

    /// <summary>
    /// Turns the uploaded bytes into text. UTF-8 (with or without a byte order mark) is expected;
    /// a file that is not valid UTF-8 is read as Windows-1251, which is what Excel in Bulgarian
    /// writes for "CSV" (not "CSV UTF-8").
    /// </summary>
    public static string Decode(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1251).GetString(bytes);
        }
    }

    /// <summary>
    /// A header and one example row built from real names, so the example passes the import checks.
    /// Without any license or subject in the database the placeholders are used.
    /// </summary>
    public static string BuildTemplate(IEnumerable<string> licenses, IEnumerable<string> subjects)
    {
        var license = licenses.FirstOrDefault() ?? "CC BY 4.0";
        var subject = subjects.FirstOrDefault() ?? "Математика";

        return Header + "\r\n"
            + $"Примерно заглавие;Име на автора;https://example.com/material.pdf;{license};{subject};5|6;Име на сайта;Кратко описание\r\n";
    }

    /// <summary>The template as bytes with a UTF-8 byte order mark, so Excel shows Cyrillic correctly.</summary>
    public static byte[] BuildTemplateFile(IEnumerable<string> licenses, IEnumerable<string> subjects)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        return utf8.GetPreamble().Concat(utf8.GetBytes(BuildTemplate(licenses, subjects))).ToArray();
    }
}
