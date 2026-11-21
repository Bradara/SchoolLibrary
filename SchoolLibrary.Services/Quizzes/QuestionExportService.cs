using System.Text;
using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Quizzes;

public class QuestionExportService : IQuestionExportService
{
    private readonly ApplicationDbContext _context;

    public QuestionExportService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<ServiceResult<QuestionExport>> ExportSectionAsync(int sectionId, string userId, bool isAdministrator)
    {
        var section = await _context.Sections.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sectionId);
        if (section == null)
        {
            return ServiceResult<QuestionExport>.Failure(ServiceError.NotFound, "Разделът не е намерен.");
        }

        if (!SectionPermissions.CanManage(section, userId, isAdministrator))
        {
            return ServiceResult<QuestionExport>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        // The subsections share the owner, so the owner's sections are enough to walk the tree
        var sections = await _context.Sections
            .AsNoTracking()
            .Where(s => s.OwnerId == section.OwnerId)
            .ToListAsync();
        var children = sections.Where(s => s.ParentId != null).ToLookup(s => s.ParentId!.Value);

        // Depth-first from the section, each with its path of names (the exported section is the top)
        var tree = new List<(Section Section, List<string> Path)>();
        var stack = new Stack<(Section Section, List<string> Path)>();
        stack.Push((section, new List<string> { section.Name }));
        var seen = new HashSet<int>();
        while (stack.Count > 0)
        {
            var (current, path) = stack.Pop();
            if (!seen.Add(current.Id))
            {
                continue;
            }

            tree.Add((current, path));
            foreach (var child in children[current.Id].OrderByDescending(c => c.Name, StringComparer.CurrentCulture))
            {
                stack.Push((child, new List<string>(path) { child.Name }));
            }
        }

        var ids = tree.Select(t => t.Section.Id).ToList();
        var questions = await _context.Questions
            .AsNoTracking()
            .Where(q => q.SectionId != null && ids.Contains(q.SectionId.Value))
            .Include(q => q.Options.OrderBy(o => o.Order))
            .ToListAsync();
        var bySection = questions.ToLookup(q => q.SectionId!.Value);

        var groups = tree
            .Select(t => new MoodleXmlWriter.Group(MoodleXmlWriter.CategoryPath(t.Path),
                bySection[t.Section.Id].OrderBy(q => q.Order).ToList()))
            .ToList();

        return ServiceResult<QuestionExport>.Success(
            new QuestionExport(FileName(section.Name), MoodleXmlWriter.Write(groups), questions.Count));
    }

    public async Task<ServiceResult<QuestionExport>> ExportQuizAsync(int quizId, string userId, bool isAdministrator)
    {
        var quiz = await _context.Quizzes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == quizId);
        if (quiz == null)
        {
            return ServiceResult<QuestionExport>.Failure(ServiceError.NotFound, "Тестът не е намерен.");
        }

        if (!QuizPermissions.CanManage(quiz, userId, isAdministrator))
        {
            return ServiceResult<QuestionExport>.Failure(ServiceError.Forbidden, "Нямате права за това действие.");
        }

        // As when solving: the quiz's own questions first, then the items in their order
        var questions = await _context.Questions
            .AsNoTracking()
            .Where(q => q.QuizId == quizId)
            .OrderBy(q => q.Order)
            .Include(q => q.Options.OrderBy(o => o.Order))
            .ToListAsync();

        var items = await _context.QuizItems
            .AsNoTracking()
            .Where(i => i.QuizId == quizId)
            .OrderBy(i => i.Order)
            .Include(i => i.Question!)
                .ThenInclude(q => q.Options.OrderBy(o => o.Order))
            .ToListAsync();

        questions.AddRange(items
            .Where(i => i.Kind == QuizItemKind.FixedQuestion && i.Question != null)
            .Select(i => i.Question!));
        var skipped = items.Count(i => i.Kind == QuizItemKind.RandomFromSection);

        return ServiceResult<QuestionExport>.Success(
            new QuestionExport(FileName(quiz.Title), MoodleXmlWriter.Write(questions), questions.Count, skipped));
    }

    // A file name from a title: letters and digits kept (Cyrillic too), everything else becomes "-"
    public static string FileName(string title)
    {
        var builder = new StringBuilder();
        foreach (var c in title.Trim())
        {
            builder.Append(char.IsLetterOrDigit(c) ? c : '-');
        }

        var name = string.Join('-', builder.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (name.Length > 80)
        {
            name = name[..80].TrimEnd('-');
        }

        return (name.Length == 0 ? "questions" : name) + ".xml";
    }
}
