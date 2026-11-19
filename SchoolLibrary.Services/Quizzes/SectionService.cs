using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Quizzes;

public class SectionService : ISectionService
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 1000;
    public const int MaxPageSize = 100;

    private readonly ApplicationDbContext _context;

    public SectionService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<SectionListItem>> GetManagedAsync(string? ownerId, int page, int pageSize = 10)
    {
        // The list shows the top level of the tree; subsections are reached through their parent
        var query = _context.Sections.AsNoTracking().Where(s => s.ParentId == null);
        if (ownerId != null)
        {
            query = query.Where(s => s.OwnerId == ownerId);
        }

        var totalCount = await query.CountAsync();

        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        page = Math.Clamp(page, 1, Math.Max(totalPages, 1));

        var sections = await query
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<SectionListItem>(await BuildItemsAsync(sections), totalCount, page, pageSize);
    }

    public async Task<IReadOnlyList<SectionListItem>> GetChildrenAsync(int parentId)
    {
        var children = await _context.Sections.AsNoTracking()
            .Where(s => s.ParentId == parentId)
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .ToListAsync();

        return await BuildItemsAsync(children);
    }

    public async Task<IReadOnlyList<Section>> GetAllForOwnerAsync(string? ownerId)
        => await _context.Sections.AsNoTracking()
            .Where(s => ownerId == null || s.OwnerId == ownerId)
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .ToListAsync();

    public async Task<Section?> GetByIdAsync(int id)
        => await _context.Sections.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);

    public async Task<IReadOnlyList<Section>> GetAncestorsAsync(int id)
    {
        var chain = new List<Section>();
        var current = await _context.Sections.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        var parentId = current?.ParentId;

        // Walk up to the root; a depth guard protects against an unexpected cycle
        for (var guard = 0; parentId != null && guard < 100; guard++)
        {
            var parent = await _context.Sections.AsNoTracking().FirstOrDefaultAsync(s => s.Id == parentId);
            if (parent == null)
            {
                break;
            }

            chain.Add(parent);
            parentId = parent.ParentId;
        }

        chain.Reverse();
        return chain;
    }

    // Loads the question and subsection counts for a set of sections (grouped, in-memory-provider friendly)
    private async Task<List<SectionListItem>> BuildItemsAsync(List<Section> sections)
    {
        var ids = sections.Select(s => s.Id).ToList();

        var questionCounts = await _context.Questions
            .Where(q => q.SectionId != null && ids.Contains(q.SectionId.Value))
            .GroupBy(q => q.SectionId!.Value)
            .Select(g => new { SectionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.SectionId, g => g.Count);

        var childCounts = await _context.Sections
            .Where(s => s.ParentId != null && ids.Contains(s.ParentId.Value))
            .GroupBy(s => s.ParentId!.Value)
            .Select(g => new { ParentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ParentId, g => g.Count);

        return sections
            .Select(s => new SectionListItem(s, questionCounts.GetValueOrDefault(s.Id), childCounts.GetValueOrDefault(s.Id)))
            .ToList();
    }

    public async Task<ServiceResult<Section>> CreateAsync(SectionInput input, string ownerId, int? parentId = null)
    {
        var validation = Validate(input);
        if (validation != null)
        {
            return ServiceResult<Section>.Failure(ServiceError.Invalid, validation);
        }

        if (parentId != null && !await _context.Sections.AnyAsync(s => s.Id == parentId))
        {
            return ServiceResult<Section>.Failure(ServiceError.Invalid, "Родителският раздел не е намерен.");
        }

        var section = new Section
        {
            Name = input.Name.Trim(),
            Description = Clean(input.Description),
            ParentId = parentId,
            OwnerId = ownerId,
            CreatedOn = DateTime.UtcNow
        };

        _context.Sections.Add(section);
        await _context.SaveChangesAsync();

        return ServiceResult<Section>.Success(section);
    }

    public async Task<ServiceResult<Section>> UpdateAsync(int id, SectionInput input, string userId, bool isAdministrator, int? parentId = null)
    {
        var section = await _context.Sections.FirstOrDefaultAsync(s => s.Id == id);
        if (section == null)
        {
            return ServiceResult<Section>.Failure(ServiceError.NotFound, "Разделът не е намерен.");
        }

        if (!SectionPermissions.CanManage(section, userId, isAdministrator))
        {
            return ServiceResult<Section>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        var validation = Validate(input);
        if (validation != null)
        {
            return ServiceResult<Section>.Failure(ServiceError.Invalid, validation);
        }

        if (parentId != null)
        {
            if (parentId == id)
            {
                return ServiceResult<Section>.Failure(ServiceError.Invalid, "Разделът не може да е родител на себе си.");
            }

            if (!await _context.Sections.AnyAsync(s => s.Id == parentId))
            {
                return ServiceResult<Section>.Failure(ServiceError.Invalid, "Родителският раздел не е намерен.");
            }

            if (await WouldCycleAsync(id, parentId.Value))
            {
                return ServiceResult<Section>.Failure(ServiceError.Invalid, "Не може да преместите раздел в негов подраздел.");
            }
        }

        section.Name = input.Name.Trim();
        section.Description = Clean(input.Description);
        section.ParentId = parentId;
        await _context.SaveChangesAsync();

        return ServiceResult<Section>.Success(section);
    }

    // True when moving a section under newParentId would make a cycle (the new parent is the section
    // itself or one of its descendants)
    private async Task<bool> WouldCycleAsync(int sectionId, int newParentId)
    {
        int? currentId = newParentId;
        for (var guard = 0; currentId != null && guard < 100; guard++)
        {
            if (currentId == sectionId)
            {
                return true;
            }

            currentId = await _context.Sections.AsNoTracking()
                .Where(s => s.Id == currentId)
                .Select(s => s.ParentId)
                .FirstOrDefaultAsync();
        }

        return false;
    }

    public async Task<IReadOnlyList<Section>> GetParentCandidatesAsync(int sectionId, string? ownerId)
    {
        var all = await _context.Sections.AsNoTracking()
            .Where(s => ownerId == null || s.OwnerId == ownerId)
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .ToListAsync();

        // Exclude the section itself and everything beneath it (those would form a cycle)
        var excluded = new HashSet<int> { sectionId };
        bool grew;
        do
        {
            grew = false;
            foreach (var s in all)
            {
                if (s.ParentId != null && excluded.Contains(s.ParentId.Value) && excluded.Add(s.Id))
                {
                    grew = true;
                }
            }
        }
        while (grew);

        return all.Where(s => !excluded.Contains(s.Id)).ToList();
    }

    public async Task<ServiceResult<Section>> DeleteAsync(int id, string userId, bool isAdministrator)
    {
        var section = await _context.Sections.FirstOrDefaultAsync(s => s.Id == id);
        if (section == null)
        {
            return ServiceResult<Section>.Failure(ServiceError.NotFound, "Разделът не е намерен.");
        }

        if (!SectionPermissions.CanManage(section, userId, isAdministrator))
        {
            return ServiceResult<Section>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        if (await _context.Sections.AnyAsync(s => s.ParentId == id))
        {
            return ServiceResult<Section>.Failure(ServiceError.Conflict,
                "Разделът съдържа подраздели и не може да бъде изтрит.");
        }

        if (await _context.Questions.AnyAsync(q => q.SectionId == id))
        {
            return ServiceResult<Section>.Failure(ServiceError.Conflict,
                "Разделът съдържа въпроси и не може да бъде изтрит.");
        }

        _context.Sections.Remove(section);
        await _context.SaveChangesAsync();

        return ServiceResult<Section>.Success(section);
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Validate(SectionInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return "Името на раздела е задължително.";
        }

        if (input.Name.Trim().Length > MaxNameLength)
        {
            return $"Името не може да бъде по-дълго от {MaxNameLength} символа.";
        }

        if (input.Description != null && input.Description.Trim().Length > MaxDescriptionLength)
        {
            return $"Описанието не може да бъде по-дълго от {MaxDescriptionLength} символа.";
        }

        return null;
    }
}
