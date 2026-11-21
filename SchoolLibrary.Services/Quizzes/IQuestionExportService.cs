using SchoolLibrary.Services.Common;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>Exports questions as a Moodle XML file.</summary>
public interface IQuestionExportService
{
    /// <summary>
    /// A bank section with its subsections, each written under its own category. Fails when the section
    /// is missing or not allowed.
    /// </summary>
    Task<ServiceResult<QuestionExport>> ExportSectionAsync(int sectionId, string userId, bool isAdministrator);

    /// <summary>
    /// A quiz's own and pinned questions, in the quiz's order. Random draws from sections are not
    /// written (their count is reported). Fails when the quiz is missing or not allowed.
    /// </summary>
    Task<ServiceResult<QuestionExport>> ExportQuizAsync(int quizId, string userId, bool isAdministrator);
}

/// <summary>An exported file: its suggested name, the XML, how many questions it holds and how many random draws were left out.</summary>
public record QuestionExport(string FileName, string Content, int QuestionCount, int SkippedRandomItems = 0);
