using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace App.Api;

/// <summary>Lets <c>dotnet ef</c> build the context without starting the app or reaching a database.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDb>
{
    public AppDb CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<AppDb>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__App") ?? "Host=localhost;Database=design_time")
        .Options);
}
