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
    /// <summary>Exactly one correct option.</summary>
    SingleChoice = 0,

    /// <summary>A statement to mark true or false: two fixed options, "Вярно" first, never shuffled.</summary>
    TrueFalse = 1,

    /// <summary>
    /// Several options may be correct; the student ticks any number of them. Each option carries a share
    /// of the points (positive for correct, negative for wrong) and the ticked shares are added up.
    /// </summary>
    MultipleChoice = 2,

    /// <summary>
    /// The student types a short answer. The options are the accepted answers (with <c>*</c> matching any
    /// text), each with its share of the points; the best matching one counts.
    /// </summary>
    ShortAnswer = 3,

    /// <summary>
    /// The student types a number. The options are the accepted values (stored with a decimal point, or
    /// <c>*</c> for any number), each with a tolerance and a share of the points; the best match counts.
    /// </summary>
    Numerical = 4
}

/// <summary>How a quiz item pulls questions from the bank. The numbers are stored in the database.</summary>
public enum QuizItemKind
{
    /// <summary>One specific question chosen from the bank.</summary>
    FixedQuestion = 0,

    /// <summary>A number of questions drawn at random from a section when the student starts.</summary>
    RandomFromSection = 1
}
