using SchoolLibrary.Data.Enums;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>Finds the grader for a question type. Graders are stateless, so one instance of each is shared.</summary>
public static class QuestionGraders
{
    private static readonly IReadOnlyDictionary<QuestionType, IQuestionGrader> ByType =
        new IQuestionGrader[]
        {
            new SingleChoiceGrader(),
            new TrueFalseGrader(),
            new MultipleChoiceGrader()
        }.ToDictionary(g => g.Type);

    /// <summary>The grader for <paramref name="type"/>.</summary>
    /// <exception cref="NotSupportedException">No grader handles the type.</exception>
    public static IQuestionGrader For(QuestionType type)
        => ByType.TryGetValue(type, out var grader)
            ? grader
            : throw new NotSupportedException($"No grader for question type {type}.");
}
