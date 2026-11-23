using System.ComponentModel.DataAnnotations;
using SchoolLibrary.Areas.Admin.Models;
using SchoolLibrary.Data.Models;

namespace SchoolLibrary.Tests.Web;

public class CategoryFormViewModelTests
{
    private static List<ValidationResult> Validate(CategoryFormViewModel model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", " ")]
    [InlineData("bi-book", "#2f6fed")]
    public void Validate_NoLookOrALookFromTheLists_IsValid(string? icon, string? color)
    {
        Assert.Empty(Validate(new CategoryFormViewModel { Name = "Музика", Icon = icon, Color = color }));
    }

    [Fact]
    public void Validate_UnknownIcon_ErrorIsOnTheIconField()
    {
        var error = Assert.Single(Validate(new CategoryFormViewModel { Name = "Музика", Icon = "bi-book\" onclick=\"x" }));

        Assert.Equal(new[] { nameof(CategoryFormViewModel.Icon) }, error.MemberNames);
    }

    [Fact]
    public void Validate_UnknownColor_ErrorIsOnTheColorField()
    {
        var error = Assert.Single(Validate(new CategoryFormViewModel { Name = "Музика", Color = "red" }));

        Assert.Equal(new[] { nameof(CategoryFormViewModel.Color) }, error.MemberNames);
    }

    [Fact]
    public void Validate_EmptyName_IsInvalid()
    {
        Assert.Contains(Validate(new CategoryFormViewModel { Name = "" }), r => r.MemberNames.Contains(nameof(CategoryFormViewModel.Name)));
    }

    [Fact]
    public void From_And_ToInput_CarryTheNameAndTheLook()
    {
        var model = CategoryFormViewModel.From(new Category { Id = 7, Name = "Химия и опазване на околната среда", Icon = "bi-droplet", Color = "#9c36b5" });

        Assert.Equal(7, model.Id);
        Assert.Equal(new SchoolLibrary.Services.Categories.CategoryInput("Химия и опазване на околната среда", "bi-droplet", "#9c36b5"), model.ToInput());
    }
}
