using App.Api;
using Microsoft.EntityFrameworkCore.Design;
using Xunit;

namespace App.Tests;

public class MigrationTests
{
    [Fact]
    public void DesignTimeFactoryBuildsTheContextWithoutADatabase()
    {
        var factory = (IDesignTimeDbContextFactory<AppDb>)Activator.CreateInstance(
            typeof(AppDb).Assembly.GetType("App.Api.DesignTimeDbContextFactory")!)!;
        using var db = factory.CreateDbContext([]);
        Assert.NotNull(db.Model.FindEntityType(typeof(Employee)));
    }
}
