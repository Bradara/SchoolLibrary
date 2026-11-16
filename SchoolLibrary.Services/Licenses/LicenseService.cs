using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Licenses;

public class LicenseService : ILicenseService
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 300;
    public const int MaxUrlLength = 500;

    private readonly ApplicationDbContext _context;

    public LicenseService(ApplicationDbContext context)
    {
        _context = context;
    }

    // Ordered by id, so the most common choices (own material, permission) stay on top
    public async Task<IReadOnlyList<License>> GetOptionsAsync()
    {
        return await _context.Licenses
            .AsNoTracking()
            .OrderBy(l => l.Id)
            .ToListAsync();
    }

    public async Task<IReadOnlyList<LicenseListItem>> GetAllAsync()
    {
        var licenses = await _context.Licenses
            .AsNoTracking()
            .OrderBy(l => l.Name)
            .ToListAsync();

        var counts = await _context.Resources
            .Where(r => r.LicenseId != null)
            .GroupBy(r => r.LicenseId!.Value)
            .Select(g => new { LicenseId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.LicenseId, g => g.Count);

        return licenses
            .Select(l => new LicenseListItem(l, counts.GetValueOrDefault(l.Id)))
            .ToList();
    }

    public async Task<License?> GetByIdAsync(int id)
    {
        return await _context.Licenses.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
    }

    public async Task<ServiceResult<License>> CreateAsync(LicenseInput input)
    {
        var validation = Validate(input);
        if (validation != null)
        {
            return ServiceResult<License>.Failure(ServiceError.Invalid, validation);
        }

        var name = input.Name.Trim();
        if (await NameExistsAsync(name, excludeId: null))
        {
            return ServiceResult<License>.Failure(ServiceError.Conflict, $"Лиценз с име {name} вече съществува.");
        }

        var license = new License();
        Apply(license, input);
        _context.Licenses.Add(license);
        await _context.SaveChangesAsync();

        return ServiceResult<License>.Success(license);
    }

    public async Task<ServiceResult<License>> UpdateAsync(int id, LicenseInput input)
    {
        var license = await _context.Licenses.FirstOrDefaultAsync(l => l.Id == id);
        if (license == null)
        {
            return ServiceResult<License>.Failure(ServiceError.NotFound, "Лицензът не е намерен.");
        }

        var validation = Validate(input);
        if (validation != null)
        {
            return ServiceResult<License>.Failure(ServiceError.Invalid, validation);
        }

        var name = input.Name.Trim();
        if (await NameExistsAsync(name, excludeId: id))
        {
            return ServiceResult<License>.Failure(ServiceError.Conflict, $"Лиценз с име {name} вече съществува.");
        }

        Apply(license, input);
        await _context.SaveChangesAsync();

        return ServiceResult<License>.Success(license);
    }

    public async Task<ServiceResult<License>> DeleteAsync(int id)
    {
        var license = await _context.Licenses.FirstOrDefaultAsync(l => l.Id == id);
        if (license == null)
        {
            return ServiceResult<License>.Failure(ServiceError.NotFound, "Лицензът не е намерен.");
        }

        var used = await _context.Resources.CountAsync(r => r.LicenseId == id);
        if (used > 0)
        {
            return ServiceResult<License>.Failure(
                ServiceError.Conflict,
                $"Лицензът {license.Name} не може да бъде изтрит — използват го {used} ресурса.");
        }

        _context.Licenses.Remove(license);
        await _context.SaveChangesAsync();

        return ServiceResult<License>.Success(license);
    }

    private static string? Validate(LicenseInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return "Името на лиценза е задължително.";
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
        if (!string.IsNullOrEmpty(url))
        {
            if (url.Length > MaxUrlLength)
            {
                return $"Уеб адресът не може да бъде по-дълъг от {MaxUrlLength} символа.";
            }

            // The address becomes a link on the resource page: only http(s) may be clickable
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return "Уеб адресът трябва да започва с http:// или https://.";
            }
        }

        return null;
    }

    // Empty optional fields are stored as null, not as an empty string
    private static void Apply(License license, LicenseInput input)
    {
        license.Name = input.Name.Trim();
        license.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        license.Url = string.IsNullOrWhiteSpace(input.Url) ? null : input.Url.Trim();
    }

    // Names are compared ignoring case, as for subjects
    private Task<bool> NameExistsAsync(string name, int? excludeId)
    {
        var lowered = name.ToLower();
        return _context.Licenses.AnyAsync(l => l.Name.ToLower() == lowered && l.Id != excludeId);
    }
}
