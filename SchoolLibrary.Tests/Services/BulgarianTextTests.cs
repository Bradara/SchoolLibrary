using SchoolLibrary.Services.Discovery;

namespace SchoolLibrary.Tests.Services;

public class BulgarianTextTests
{
    [Theory]
    [InlineData("Обикновени дроби")]
    [InlineData("Многоъгълник с подпрограма")]
    [InlineData("5 кл - Правоъгълен паралелепипед")]
    public void LooksBulgarian_CyrillicWithoutForeignLetters(string text)
    {
        Assert.True(BulgarianText.LooksBulgarian(text));
    }

    [Theory]
    [InlineData("Обыкновенные дроби")]   // Russian ы
    [InlineData("Это куб")]              // Russian э
    [InlineData("Вивчаємо куб")]         // Ukrainian є, ї
    [InlineData("Фигуре у равни џ")]     // Serbian џ
    [InlineData("Fractions")]
    [InlineData("")]
    [InlineData(null)]
    public void LooksBulgarian_OtherTextIsNot(string? text)
    {
        Assert.False(BulgarianText.LooksBulgarian(text));
    }
}
