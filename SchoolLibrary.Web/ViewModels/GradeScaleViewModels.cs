using System.ComponentModel.DataAnnotations;
using System.Globalization;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Services.Exams;
using SchoolLibrary.Services.Quizzes;

namespace SchoolLibrary.ViewModels;

/// <summary>
/// The new/edit grade scale form: a name and the lowest percentage of the points for each grade. The thresholds are
/// read as text ("33,33" or "33.33"), so they do not depend on the server's culture.
/// </summary>
public class GradeScaleFormViewModel : IValidatableObject
{
    [Required(ErrorMessage = "Въведете име на скалата")]
    [StringLength(GradeScale.NameMaxLength, ErrorMessage = "Името е най-много 100 символа")]
    [Display(Name = "Име")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Среден 3 от (%)")]
    public string? Three { get; set; } = Text(ExamGrading.Default.Three);

    [Display(Name = "Добър 4 от (%)")]
    public string? Four { get; set; } = Text(ExamGrading.Default.Four);

    [Display(Name = "Много добър 5 от (%)")]
    public string? Five { get; set; } = Text(ExamGrading.Default.Five);

    [Display(Name = "Отличен 6 от (%)")]
    public string? Six { get; set; } = Text(ExamGrading.Default.Six);

    /// <summary>The input for the service; call it only when the form is valid.</summary>
    public GradeScaleInput ToInput()
        => new(Name, NumericAnswer.Parse(Three)!.Value, NumericAnswer.Parse(Four)!.Value,
            NumericAnswer.Parse(Five)!.Value, NumericAnswer.Parse(Six)!.Value);

    public static GradeScaleFormViewModel From(GradeScale scale) => new()
    {
        Name = scale.Name,
        Three = Text(scale.Three),
        Four = Text(scale.Four),
        Five = Text(scale.Five),
        Six = Text(scale.Six)
    };

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var fields = new[] { (nameof(Three), Three, "3"), (nameof(Four), Four, "4"), (nameof(Five), Five, "5"), (nameof(Six), Six, "6") };
        var values = new List<decimal>();
        foreach (var (name, text, grade) in fields)
        {
            if (NumericAnswer.Parse(text) is { } value)
            {
                values.Add(value);
            }
            else
            {
                yield return new ValidationResult($"Въведете прага за {grade} като число, например 33,33.", new[] { name });
            }
        }

        // The range and the order of the thresholds are shown above the form, before the service is asked
        if (values.Count == fields.Length && ExamGrading.ValidateThresholds(values[0], values[1], values[2], values[3]) is { } error)
        {
            yield return new ValidationResult(error);
        }
    }

    // As the number field shows it: a decimal point, no trailing zeros ("33.33", "50")
    private static string Text(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
