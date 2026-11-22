using SchoolLibrary.Data.Enums;
using SchoolLibrary.Services.Resources;

namespace SchoolLibrary.Tests;

/// <summary>Ready-made viewers of the catalog for the tests.</summary>
public static class TestViewers
{
    public static ResourceViewer Student(string userId) =>
        new(userId, ResourceAudience.Guests | ResourceAudience.Students, false);

    public static ResourceViewer Parent(string userId) =>
        new(userId, ResourceAudience.Guests | ResourceAudience.Parents, false);

    public static ResourceViewer Teacher(string userId) =>
        new(userId, ResourceAudience.Guests | ResourceAudience.Teachers, false);

    public static ResourceViewer Administrator(string userId = "admin") =>
        new(userId, ResourceAudience.Guests, true);
}
