using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>What a teacher types in the section form.</summary>
public record SectionInput(string Name, string? Description);

/// <summary>A section with the number of questions it holds and of subsections it has, for the lists.</summary>
public record SectionListItem(Section Section, int QuestionCount, int ChildCount = 0);
