using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Licenses;

public class LicenseService : ILicenseService
{
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
}
