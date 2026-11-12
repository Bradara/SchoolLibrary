using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.GradeLevels;

public class GradeLevelService : IGradeLevelService
{
    private readonly ApplicationDbContext _context;

    public GradeLevelService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<GradeLevel>> GetAllAsync()
    {
        return await _context.GradeLevels
            .AsNoTracking()
            .OrderBy(g => g.Number)
            .ToListAsync();
    }
}
