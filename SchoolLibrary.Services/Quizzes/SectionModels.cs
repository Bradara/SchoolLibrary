using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>What a teacher types in the section form.</summary>
public record SectionInput(string Name, string? Description);

/// <summary>A section with the number of questions it holds and of subsections it has, for the lists.</summary>
public record SectionListItem(Section Section, int QuestionCount, int ChildCount = 0);

/// <summary>The questions a parser produced from an import file, plus per-block error messages.</summary>
public record ImportParseResult(IReadOnlyList<QuestionInput> Questions, IReadOnlyList<string> Errors);

/// <summary>The outcome of importing questions into a section: how many were added and what failed.</summary>
public record ImportSummary(int Added, IReadOnlyList<string> Errors);

/// <summary>
/// The outcome of deleting several bank questions: how many were deleted and how many were kept because
/// students have already answered them (their results must stay intact).
/// </summary>
public record QuestionDeleteSummary(int Deleted, int KeptSolved);
