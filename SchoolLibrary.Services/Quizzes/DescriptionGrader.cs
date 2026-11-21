using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>A description has nothing to answer: it always earns 0 points and keeps no answer.</summary>
public sealed class DescriptionGrader : IQuestionGrader
{
    public QuestionType Type => QuestionType.Description;

    public QuestionGrade Grade(Question question, SubmittedAnswer answer)
        => new(0m, false, SubmittedAnswer.Empty);
}
