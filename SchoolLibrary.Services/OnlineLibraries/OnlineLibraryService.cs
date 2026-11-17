using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Common;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.OnlineLibraries;

public class OnlineLibraryService : IOnlineLibraryService
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 300;
    public const int MaxUrlLength = 500;

    private static readonly IReadOnlyList<LibraryIconOption> Icons = new LibraryIconOption[]
    {
        new("bi-book", "Книга"),
        new("bi-bookshelf", "Рафт с книги"),
        new("bi-journal-text", "Тетрадка"),
        new("bi-journals", "Тетрадки"),
        new("bi-feather", "Перо"),
        new("bi-mortarboard", "Шапка на дипломант"),
        new("bi-bank", "Сграда"),
        new("bi-globe2", "Глобус"),
        new("bi-translate", "Езици"),
        new("bi-calculator", "Калкулатор"),
        new("bi-music-note-beamed", "Музика"),
        new("bi-film", "Филми"),
        new("bi-newspaper", "Вестник")
    };

    private readonly ApplicationDbContext _context;

    public OnlineLibraryService(ApplicationDbContext context)
    {
        _context = context;
    }

    public IReadOnlyList<LibraryIconOption> GetIcons() => Icons;

    public async Task<IReadOnlyList<OnlineLibrary>> GetAllAsync()
    {
        return await _context.OnlineLibraries
            .AsNoTracking()
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Name)
            .ToListAsync();
    }

    public async Task<OnlineLibrary?> GetByIdAsync(int id)
    {
        return await _context.OnlineLibraries.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
    }

    public async Task<ServiceResult<OnlineLibrary>> CreateAsync(OnlineLibraryInput input)
    {
        var validation = Validate(input);
        if (validation != null)
        {
            return ServiceResult<OnlineLibrary>.Failure(ServiceError.Invalid, validation);
        }

        var conflict = await FindConflictAsync(input, excludeId: null);
        if (conflict != null)
        {
            return ServiceResult<OnlineLibrary>.Failure(ServiceError.Conflict, conflict);
        }

        var library = new OnlineLibrary();
        Apply(library, input);
        _context.OnlineLibraries.Add(library);
        await _context.SaveChangesAsync();

        return ServiceResult<OnlineLibrary>.Success(library);
    }

    public async Task<ServiceResult<OnlineLibrary>> UpdateAsync(int id, OnlineLibraryInput input)
    {
        var library = await _context.OnlineLibraries.FirstOrDefaultAsync(l => l.Id == id);
        if (library == null)
        {
            return ServiceResult<OnlineLibrary>.Failure(ServiceError.NotFound, "Библиотеката не е намерена.");
        }

        var validation = Validate(input);
        if (validation != null)
        {
            return ServiceResult<OnlineLibrary>.Failure(ServiceError.Invalid, validation);
        }

        var conflict = await FindConflictAsync(input, excludeId: id);
        if (conflict != null)
        {
            return ServiceResult<OnlineLibrary>.Failure(ServiceError.Conflict, conflict);
        }

        Apply(library, input);
        await _context.SaveChangesAsync();

        return ServiceResult<OnlineLibrary>.Success(library);
    }

    public async Task<ServiceResult<OnlineLibrary>> DeleteAsync(int id)
    {
        var library = await _context.OnlineLibraries.FirstOrDefaultAsync(l => l.Id == id);
        if (library == null)
        {
            return ServiceResult<OnlineLibrary>.Failure(ServiceError.NotFound, "Библиотеката не е намерена.");
        }

        _context.OnlineLibraries.Remove(library);
        await _context.SaveChangesAsync();

        return ServiceResult<OnlineLibrary>.Success(library);
    }

    private static string? Validate(OnlineLibraryInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return "Името на библиотеката е задължително.";
        }

        if (input.Name.Trim().Length > MaxNameLength)
        {
            return $"Името не може да бъде по-дълго от {MaxNameLength} символа.";
        }

        if (input.Description != null && input.Description.Trim().Length > MaxDescriptionLength)
        {
            return $"Описанието не може да бъде по-дълго от {MaxDescriptionLength} символа.";
        }

        var url = input.Url?.Trim();
        if (string.IsNullOrEmpty(url))
        {
            return "Уеб адресът е задължителен.";
        }

        if (url.Length > MaxUrlLength)
        {
            return $"Уеб адресът не може да бъде по-дълъг от {MaxUrlLength} символа.";
        }

        // The address becomes a link on the catalog page: only http(s) may be clickable
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return "Уеб адресът трябва да започва с http:// или https://.";
        }

        if (!string.IsNullOrWhiteSpace(input.Icon) && Icons.All(i => i.Class != input.Icon))
        {
            return "Избраната иконка не е от списъка.";
        }

        return null;
    }

    // Empty optional fields are stored as null; an empty icon becomes the default one
    private static void Apply(OnlineLibrary library, OnlineLibraryInput input)
    {
        library.Name = input.Name.Trim();
        library.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        library.Url = input.Url.Trim();
        library.Icon = string.IsNullOrWhiteSpace(input.Icon) ? OnlineLibrary.DefaultIcon : input.Icon;
        library.SortOrder = input.SortOrder;
    }

    // A catalog has few entries, so addresses are compared in memory with the same normalization as resources
    private async Task<string?> FindConflictAsync(OnlineLibraryInput input, int? excludeId)
    {
        var name = input.Name.Trim();
        var lowered = name.ToLower();
        if (await _context.OnlineLibraries.AnyAsync(l => l.Name.ToLower() == lowered && l.Id != excludeId))
        {
            return $"Библиотека с име {name} вече съществува.";
        }

        var normalized = UrlNormalizer.Normalize(input.Url.Trim());
        var others = await _context.OnlineLibraries
            .Where(l => l.Id != excludeId)
            .Select(l => new { l.Name, l.Url })
            .ToListAsync();

        var same = others.FirstOrDefault(o => UrlNormalizer.Normalize(o.Url) == normalized);
        return same == null ? null : $"Този адрес вече е в каталога като {same.Name}.";
    }
}
