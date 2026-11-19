using System.Security.Cryptography;

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
}
