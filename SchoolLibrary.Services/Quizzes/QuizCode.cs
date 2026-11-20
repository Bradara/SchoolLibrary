using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;

namespace SchoolLibrary.Services.Quizzes;

/// <summary>Generates short exam codes that students type to enter an exam.</summary>
public static class QuizCode
{
    // Upper-case only, without the easily confused 0/O, 1/I/L: a code is read aloud and typed by hand.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public static string Generate(int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }

    /// <summary>
    /// A code no exam uses yet, neither an exam-mode quiz nor a class exam, since students enter both
    /// on the same page.
    /// </summary>
    public static async Task<string> GenerateUniqueAsync(ApplicationDbContext context, int length)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var code = Generate(length);
            if (!await context.Quizzes.AnyAsync(q => q.Code == code)
                && !await context.ExamAssignments.AnyAsync(e => e.Code == code))
            {
                return code;
            }
        }

        throw new InvalidOperationException("Could not generate a unique exam code.");
    }
}
