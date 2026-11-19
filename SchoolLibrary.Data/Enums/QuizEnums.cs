namespace SchoolLibrary.Data.Enums;

/// <summary>How a quiz is taken. The numbers are stored in the database.</summary>
public enum QuizMode
{
    /// <summary>Open practice: immediate feedback, unlimited retries, no timer, no code.</summary>
    Practice = 0,

    /// <summary>A timed exam entered by code, one attempt, result shown at the end.</summary>
    Exam = 1
}

/// <summary>The kind of a quiz question. The numbers are stored in the database.</summary>
public enum QuestionType
{
    /// <summary>Exactly one correct option. The only type in the first version.</summary>
    SingleChoice = 0
}
