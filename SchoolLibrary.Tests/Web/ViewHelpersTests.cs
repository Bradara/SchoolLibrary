using System.Security.Claims;
using SchoolLibrary.Data.Constants;
using SchoolLibrary.Data.Enums;
using SchoolLibrary.Data.Models;
using SchoolLibrary.Extensions;

namespace SchoolLibrary.Tests.Web;

// The small helpers the views use for texts and for what to show to whom
public class ViewHelpersTests
{
    // ---------- report names ----------

    [Fact]
    public void EveryReason_HasADifferentBulgarianName()
    {
        var names = Enum.GetValues<ReportReason>().Select(r => r.DisplayName()).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Equal("Нарушено авторско право", ReportReason.Copyright.DisplayName());
        Assert.Equal("Неподходящо съдържание", ReportReason.Inappropriate.DisplayName());
        Assert.Equal("Невалиден линк", ReportReason.BrokenLink.DisplayName());
        Assert.Equal("Друго", ReportReason.Other.DisplayName());
    }

    [Theory]
    [InlineData(ReportStatus.New, "Нов", "text-bg-warning")]
    [InlineData(ReportStatus.Reviewed, "Разгледан", "text-bg-success")]
    [InlineData(ReportStatus.Rejected, "Отхвърлен", "text-bg-secondary")]
    public void EveryStatus_HasANameAndABadgeColor(ReportStatus status, string name, string badge)
    {
        Assert.Equal(name, status.DisplayName());
        Assert.Equal(badge, status.BadgeClass());
    }

    [Theory]
    [InlineData(ReportTarget.Resource, "Ресурс")]
    [InlineData(ReportTarget.Comment, "Коментар")]
    public void EveryTarget_HasAName(ReportTarget target, string name)
    {
        Assert.Equal(name, target.DisplayName());
    }

    // ---------- who published a resource ----------

    [Fact]
    public void PublisherName_IsTheOwnersFullName()
    {
        var resource = new Resource { Owner = new ApplicationUser { FirstName = "Иван", LastName = "Петров" } };

        Assert.Equal("Иван Петров", resource.PublisherName());
    }

    [Fact]
    public void PublisherName_ForAFreeResourceWithoutAnOwner_IsThePlatform()
    {
        Assert.Equal("Платформата", new Resource { Kind = ResourceKind.External }.PublisherName());
    }

    [Fact]
    public void PublisherName_ForALessonWhoseAuthorDeletedTheProfile_IsADeletedUser()
    {
        Assert.Equal("Изтрит потребител", new Resource { Kind = ResourceKind.Lesson }.PublisherName());
    }

    [Fact]
    public void PublisherName_AnOwnerWinsOverTheKind()
    {
        var resource = new Resource { Kind = ResourceKind.External, Owner = new ApplicationUser { FirstName = "А", LastName = "Б" } };

        Assert.Equal("А Б", resource.PublisherName());
    }

    // ---------- who may add resources ----------

    private static ClaimsPrincipal UserWithRoles(params string[] roles) =>
        new(new ClaimsIdentity(roles.Select(r => new Claim(ClaimTypes.Role, r)), "test"));

    [Theory]
    [InlineData(Roles.Teacher, true)]
    [InlineData(Roles.Administrator, true)]
    [InlineData(Roles.Student, false)]
    [InlineData(Roles.Parent, false)]
    [InlineData(Roles.User, false)]
    public void CanAddResources_OnlyTeachersAndAdministrators(string role, bool expected)
    {
        Assert.Equal(expected, UserWithRoles(Roles.User, role).CanAddResources());
    }

    [Fact]
    public void CanAddResources_ForAVisitorWithoutRoles_IsFalse()
    {
        Assert.False(new ClaimsPrincipal(new ClaimsIdentity()).CanAddResources());
    }
}
