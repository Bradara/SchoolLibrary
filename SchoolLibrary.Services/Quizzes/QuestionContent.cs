using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>Copies a validated <see cref="QuestionInput"/> onto a question: text, type, points and options.</summary>
public static class QuestionContent
{
    /// <summary>
    /// Sets the question's text, type and points and replaces its options with the input's. The caller
    /// removes the old option rows from the context when the question is already saved.
    /// </summary>
    public static void Apply(Question question, QuestionInput input)
    {
        question.Text = input.Text.Trim();
        question.Type = input.Type;
        question.Points = input.Points;

        question.Options.Clear();
        for (var i = 0; i < input.Options.Count; i++)
        {
            question.Options.Add(new AnswerOption
            {
                Text = input.Options[i].Trim(),
                IsCorrect = i == input.CorrectIndex,
                Order = i + 1
            });
        }
    }
}
