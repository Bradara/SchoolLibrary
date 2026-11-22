using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>A section with its full path from the root (e.g. "Информатика > 9 клас"), for pickers.</summary>
public record SectionOption(int Id, string Path);

/// <summary>
/// Builds the full paths of sections so that pickers can tell apart sections with the same name
/// under different parents.
/// </summary>
public static class SectionPaths
{
    public const string Separator = " > ";

    // Guards against an unexpected cycle in the parent links
    private const int MaxDepth = 100;

    /// <summary>
    /// The paths of the given sections in tree order: each parent is followed by its subsections, and
    /// siblings keep the order in which they were given. A section whose parent is not in the list is
    /// treated as a root (its path starts with its own name).
    /// </summary>
    public static IReadOnlyList<SectionOption> Build(IEnumerable<Section> sections)
    {
        var list = sections.ToList();
        var byId = list.ToDictionary(s => s.Id);
        var children = list
            .Where(s => s.ParentId != null && byId.ContainsKey(s.ParentId.Value))
            .ToLookup(s => s.ParentId!.Value);

        var result = new List<SectionOption>(list.Count);
        var visited = new HashSet<int>();

        void Visit(Section section, string? parentPath, int depth)
        {
            if (depth > MaxDepth || !visited.Add(section.Id))
            {
                return;
            }

            var path = parentPath == null ? section.Name : parentPath + Separator + section.Name;
            result.Add(new SectionOption(section.Id, path));
            foreach (var child in children[section.Id])
            {
                Visit(child, path, depth + 1);
            }
        }

        foreach (var root in list.Where(s => s.ParentId == null || !byId.ContainsKey(s.ParentId.Value)))
        {
            Visit(root, null, 0);
        }

        // Sections caught in a cycle have no root; list them by name so that none is lost
        foreach (var section in list.Where(s => !visited.Contains(s.Id)))
        {
            Visit(section, null, 0);
        }

        return result;
    }
}
