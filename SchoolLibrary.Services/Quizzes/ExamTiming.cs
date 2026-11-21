using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>
/// The server's view of an exam's time limit. The countdown in the browser only shows the clock and submits
/// the form when it reaches zero; a student who turns off scripts or sends the form by hand is caught here.
/// </summary>
public static class ExamTiming
{
    /// <summary>
    /// Allowance for the network and for the browser's own auto-submit, which fires right at zero.
    /// </summary>
    public const int GraceSeconds = 30;

    /// <summary>
    /// How many seconds past the limit a submission made at <paramref name="submittedOn"/> is; null when it is
    /// within the limit (plus the grace period) or the exam has no time limit.
    /// </summary>
    public static int? LateBySeconds(QuizAttempt attempt, DateTime submittedOn)
    {
        if (attempt.TimeLimitMinutes is not int minutes)
        {
            return null;
        }

        var over = (submittedOn - attempt.StartedOn).TotalSeconds - minutes * 60;
        return over > GraceSeconds ? (int)Math.Min(over, int.MaxValue) : null;
    }

    /// <summary>A short Bulgarian label for the delay, e.g. "закъснял с 3 мин." or "закъснял с 45 сек.".</summary>
    public static string LateLabel(int lateBySeconds)
        => lateBySeconds < 60
            ? $"закъснял с {lateBySeconds} сек."
            : $"закъснял с {(int)Math.Ceiling(lateBySeconds / 60.0)} мин.";
}
