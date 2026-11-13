using System.Globalization;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Common;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Resources;

public class ResourceImportService : IResourceImportService
{
    private const char ListSeparator = '|';

    private static readonly string[] RequiredColumns = { "title", "author", "url", "license", "subjects", "grades" };

    private static readonly Regex LeadingNumber = new(@"^\d+", RegexOptions.Compiled);

    private readonly ApplicationDbContext _context;
    private readonly IResourceService _resources;

    public ResourceImportService(ApplicationDbContext context, IResourceService resources)
    {
        _context = context;
        _resources = resources;
    }

    public Task<ResourceImportPreview> PreviewAsync(string csvText) => AnalyzeAsync(csvText);

    public async Task<ResourceImportPreview> ImportAsync(string csvText)
    {
        var preview = await AnalyzeAsync(csvText);
        if (preview.HasFileError)
        {
            return preview;
        }

        foreach (var row in preview.Rows.Where(r => r.Status == ResourceImportRowStatus.New))
        {
            // The resource service validates again and checks the link once more, so a resource
            // added since the preview is reported as a duplicate instead of being saved twice
            var result = await _resources.CreateAsync(row.Input!, ownerId: null);
            if (result.Succeeded)
            {
                row.Status = ResourceImportRowStatus.Imported;
            }
            else
            {
                row.Status = result.Error == ServiceError.Conflict
                    ? ResourceImportRowStatus.Duplicate
                    : ResourceImportRowStatus.Invalid;
                row.Message = result.Message;
            }
        }

        return preview;
    }

    // ---------- reading the file ----------

    private sealed record RawRow(int LineNumber, Dictionary<string, string> Fields)
    {
        public string Get(string column) => Fields.TryGetValue(column, out var value) ? value : string.Empty;
    }

    private static ResourceImportPreview? TryRead(string csvText, out List<RawRow> rows)
    {
        rows = new List<RawRow>();

        if (string.IsNullOrWhiteSpace(csvText))
        {
            return ResourceImportPreview.Failed("Файлът е празен.");
        }

        if (csvText.Length > IResourceImportService.MaxFileLength)
        {
            return ResourceImportPreview.Failed("Файлът е твърде голям (най-много 512 KB).");
        }

        // Excel saves "CSV UTF-8" with a byte order mark in front of the first column name
        csvText = csvText.TrimStart('﻿');

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ";",
            HasHeaderRecord = true,
            TrimOptions = TrimOptions.Trim,
            IgnoreBlankLines = true,
            MissingFieldFound = null,
            BadDataFound = null,
            HeaderValidated = null
        };

        try
        {
            using var reader = new StringReader(csvText);
            using var csv = new CsvReader(reader, config);

            if (!csv.Read() || !csv.ReadHeader() || csv.HeaderRecord == null)
            {
                return ResourceImportPreview.Failed("Файлът е празен.");
            }

            var columns = csv.HeaderRecord.Select(h => h.Trim().ToLowerInvariant()).ToList();

            var missing = RequiredColumns.Where(c => !columns.Contains(c)).ToList();
            if (missing.Count > 0)
            {
                var hint = columns.Count == 1 && columns[0].Contains(',')
                    ? " Колоните трябва да са разделени с точка и запетая (;)."
                    : string.Empty;

                return ResourceImportPreview.Failed($"Липсват колони: {string.Join(", ", missing)}.{hint}");
            }

            while (csv.Read())
            {
                if (rows.Count >= IResourceImportService.MaxRows)
                {
                    return ResourceImportPreview.Failed($"Файлът има повече от {IResourceImportService.MaxRows} реда. Разделете го на части.");
                }

                var fields = new Dictionary<string, string>();
                for (var i = 0; i < columns.Count; i++)
                {
                    fields[columns[i]] = csv.TryGetField(i, out string? value) ? value ?? string.Empty : string.Empty;
                }

                rows.Add(new RawRow(csv.Parser.RawRow, fields));
            }
        }
        catch (CsvHelperException)
        {
            return ResourceImportPreview.Failed("Файлът не може да бъде прочетен. Проверете кавичките и разделителите.");
        }

        return rows.Count == 0 ? ResourceImportPreview.Failed("Файлът няма редове с данни.") : null;
    }

    // ---------- checking the rows ----------

    private async Task<ResourceImportPreview> AnalyzeAsync(string csvText)
    {
        var failure = TryRead(csvText, out var rawRows);
        if (failure != null)
        {
            return failure;
        }

        var categories = await LoadByNameAsync(_context.Categories.Select(c => new KeyValuePair<string, int>(c.Name, c.Id)));
        var licenses = await LoadByNameAsync(_context.Licenses.Select(l => new KeyValuePair<string, int>(l.Name, l.Id)));
        var grades = await _context.GradeLevels.ToDictionaryAsync(g => g.Number, g => g.Id);

        // First pass: map names to ids and apply the same rules as the resource form
        var checkedRows = rawRows
            .Select(raw => (Raw: raw, Result: BuildInput(raw, categories, licenses, grades)))
            .ToList();

        var existing = await FindExistingAsync(
            checkedRows.Where(r => r.Result.Input != null).Select(r => UrlNormalizer.Normalize(r.Result.Input!.Url)!));

        // Second pass: the same link twice in the file, or a link the library already has
        var firstLineOfLink = new Dictionary<string, int>();
        var rows = new List<ResourceImportRow>();

        foreach (var (raw, result) in checkedRows)
        {
            var status = ResourceImportRowStatus.New;
            var message = result.Error;
            int? duplicateOfId = null;

            if (result.Input == null)
            {
                status = ResourceImportRowStatus.Invalid;
            }
            else
            {
                var normalized = UrlNormalizer.Normalize(result.Input.Url)!;

                if (firstLineOfLink.TryGetValue(normalized, out var firstLine))
                {
                    status = ResourceImportRowStatus.Duplicate;
                    message = $"Същият линк е в ред {firstLine} на файла.";
                }
                else if (existing.TryGetValue(normalized, out var found))
                {
                    status = ResourceImportRowStatus.Duplicate;
                    message = $"Вече съществува в библиотеката: {found.Title}.";
                    duplicateOfId = found.Id;
                    firstLineOfLink[normalized] = raw.LineNumber;
                }
                else
                {
                    firstLineOfLink[normalized] = raw.LineNumber;
                }
            }

            rows.Add(new ResourceImportRow
            {
                LineNumber = raw.LineNumber,
                Title = raw.Get("title"),
                Url = raw.Get("url"),
                Status = status,
                Message = message,
                DuplicateOfId = duplicateOfId,
                Input = status == ResourceImportRowStatus.New ? result.Input : null
            });
        }

        return new ResourceImportPreview { Rows = rows };
    }

    private static async Task<Dictionary<string, int>> LoadByNameAsync(IQueryable<KeyValuePair<string, int>> pairs)
    {
        var list = await pairs.ToListAsync();
        return list.ToDictionary(p => p.Key.Trim(), p => p.Value, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, (int Id, string Title)>> FindExistingAsync(IEnumerable<string> normalizedUrls)
    {
        var urls = normalizedUrls.Distinct().ToList();

        var found = await _context.Resources
            .AsNoTracking()
            .Where(r => r.NormalizedUrl != null && urls.Contains(r.NormalizedUrl))
            .OrderBy(r => r.Id)
            .Select(r => new { r.NormalizedUrl, r.Id, r.Title })
            .ToListAsync();

        return found
            .GroupBy(r => r.NormalizedUrl!)
            .ToDictionary(g => g.Key, g => (g.First().Id, g.First().Title));
    }

    // Either the input to save or the reason the row cannot be imported
    private static (ResourceInput? Input, string? Error) BuildInput(
        RawRow raw,
        Dictionary<string, int> categories,
        Dictionary<string, int> licenses,
        Dictionary<int, int> grades)
    {
        var input = new ResourceInput
        {
            Title = raw.Get("title"),
            Author = raw.Get("author"),
            Source = raw.Get("source"),
            Description = raw.Get("description"),
            Url = raw.Get("url"),
            Kind = ResourceKind.External
        };

        var errors = new List<string>();

        var licenseName = raw.Get("license");
        if (licenseName.Length > 0)
        {
            if (licenses.TryGetValue(licenseName, out var licenseId))
            {
                input.LicenseId = licenseId;
            }
            else
            {
                errors.Add($"Непознат лиценз: {licenseName}.");
            }
        }

        foreach (var name in SplitList(raw.Get("subjects")))
        {
            if (categories.TryGetValue(name, out var categoryId))
            {
                input.CategoryIds.Add(categoryId);
            }
            else
            {
                errors.Add($"Непознат предмет: {name}.");
            }
        }

        foreach (var text in SplitList(raw.Get("grades")))
        {
            var match = LeadingNumber.Match(text);
            if (match.Success && int.TryParse(match.Value, out var number) && grades.TryGetValue(number, out var gradeId))
            {
                input.GradeLevelIds.Add(gradeId);
            }
            else
            {
                errors.Add($"Непознат клас: {text}.");
            }
        }

        if (errors.Count > 0)
        {
            return (null, string.Join(" ", errors));
        }

        // The remaining rules (required fields, lengths, link scheme, at least one subject and grade)
        // are the ones the resource form uses
        var validation = ResourceService.Validate(input);
        return validation == null ? (input, null) : (null, validation);
    }

    private static IEnumerable<string> SplitList(string cell) =>
        cell.Split(ListSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
