using Microsoft.EntityFrameworkCore;
using SchoolLibrary.Data;

namespace SchoolLibrary.Tests;

/// <summary>Creates an isolated in-memory database for each test.</summary>
public static class TestDbContextFactory
{
    public static ApplicationDbContext Create()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }
}
