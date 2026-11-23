using SchoolLibrary.Data.Common;

namespace SchoolLibrary.Tests;

public class SubjectAppearanceTests
{
    [Fact]
    public void Lists_HaveNoDuplicates()
    {
        Assert.Equal(SubjectAppearance.Icons.Count, SubjectAppearance.Icons.Select(i => i.Class).Distinct().Count());
        Assert.Equal(SubjectAppearance.Colors.Count, SubjectAppearance.Colors.Select(c => c.Hex).Distinct().Count());
    }

    [Fact]
    public void Lists_ContainTheDefaults()
    {
        Assert.True(SubjectAppearance.IsValidIcon(SubjectAppearance.DefaultIcon));
        Assert.True(SubjectAppearance.IsValidColor(SubjectAppearance.DefaultColor));
    }

    // A value from the list must pass the check constraint of the database
    [Fact]
    public void Lists_MatchTheDatabaseRules()
    {
        Assert.All(SubjectAppearance.Icons, i =>
        {
            Assert.Matches(SubjectAppearance.IconPattern, i.Class);
            Assert.True(i.Class.Length <= SubjectAppearance.IconMaxLength);
            Assert.False(string.IsNullOrWhiteSpace(i.Label));
        });
        Assert.All(SubjectAppearance.Colors, c =>
        {
            Assert.Matches(SubjectAppearance.ColorPattern, c.Hex);
            Assert.Equal(SubjectAppearance.ColorMaxLength, c.Hex.Length);
            Assert.False(string.IsNullOrWhiteSpace(c.Label));
        });
    }

    // A typo in an icon name would show an empty square
    [Fact]
    public void Icons_ExistInTheBundledIconFont()
    {
        var css = File.ReadAllText(FindIconCss());

        Assert.All(SubjectAppearance.Icons, i => Assert.Contains("." + i.Class + "::before", css));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bi-does-not-exist")]
    [InlineData("bi-book\" onmouseover=\"alert(1)")]
    [InlineData("BI-BOOK")]
    public void IsValidIcon_RejectsValuesOutsideTheList(string? icon) =>
        Assert.False(SubjectAppearance.IsValidIcon(icon));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("red")]
    [InlineData("#2F6FED")]
    [InlineData("#fff")]
    [InlineData("#123456")]
    [InlineData("#2f6fed;background:url(x)")]
    public void IsValidColor_RejectsValuesOutsideTheList(string? color) =>
        Assert.False(SubjectAppearance.IsValidColor(color));

    [Fact]
    public void ForName_GivesAStandardSubjectItsLook()
    {
        Assert.Equal(("bi-plus-slash-minus", "#2f6fed"), SubjectAppearance.ForName("Математика"));
        Assert.Equal(("bi-code-slash", "#0f9d8a"), SubjectAppearance.ForName("  Компютърно моделиране и информационни технологии "));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Астрология")]
    [InlineData("математика")]
    public void ForName_GivesAnyOtherNameTheDefaultLook(string? name) =>
        Assert.Equal((SubjectAppearance.DefaultIcon, SubjectAppearance.DefaultColor), SubjectAppearance.ForName(name));

    [Fact]
    public void ForName_UsesOnlyValuesFromTheLists()
    {
        Assert.All(SubjectAppearance.StandardNames, name =>
        {
            var (icon, color) = SubjectAppearance.ForName(name);
            Assert.True(SubjectAppearance.IsValidIcon(icon), name);
            Assert.True(SubjectAppearance.IsValidColor(color), name);
        });
    }

    // The migration that gave the existing subjects their look keeps a frozen copy of the list:
    // when it was written, the copy and the list had to agree
    [Fact]
    public void Migration_GivesTheSameLookAsTheSeed()
    {
        var field = typeof(SchoolLibrary.Data.Migrations.AddCategoryAppearance)
            .GetField("StandardLooks", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var looks = Assert.IsType<(string Name, string Icon, string Color)[]>(field!.GetValue(null));

        Assert.All(looks, l => Assert.Equal(SubjectAppearance.ForName(l.Name), (l.Icon, l.Color)));
        // Every standard subject except "Друго" (it keeps the default look) is in the migration
        Assert.Equal(
            SubjectAppearance.StandardNames.Where(n => n != "Друго").OrderBy(n => n, StringComparer.Ordinal),
            looks.Select(l => l.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    private static string FindIconCss()
    {
        var relative = Path.Combine("SchoolLibrary.Web", "wwwroot", "lib", "bootstrap-icons", "bootstrap-icons.min.css");
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("The Bootstrap Icons stylesheet was not found.", relative);
    }
}
