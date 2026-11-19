using App.Api;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.AspNetCore.Hosting;
using Npgsql;
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

public class MigrationSchemaTests
{
    private static readonly string Server = Environment.GetEnvironmentVariable("ConnectionStrings__App")!;

    // everything that describes the schema, as sorted text lines
    private const string SchemaSql = """
        SELECT 'col ' || table_name || '.' || column_name || ' ' || data_type || ' ' || udt_name || ' ' || is_nullable || ' ' || coalesce(column_default, '') || ' ' || coalesce(is_identity, '')
          FROM information_schema.columns WHERE table_schema = 'public' AND table_name <> '__EFMigrationsHistory'
        UNION ALL SELECT 'idx ' || tablename || ' ' || indexdef FROM pg_indexes WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory'
        UNION ALL SELECT 'con ' || conrelid::regclass || ' ' || pg_get_constraintdef(oid) FROM pg_constraint WHERE connamespace = 'public'::regnamespace AND conrelid::regclass::text <> '"__EFMigrationsHistory"'
        UNION ALL SELECT 'enum ' || t.typname || ' ' || e.enumlabel FROM pg_enum e JOIN pg_type t ON t.oid = e.enumtypid
        UNION ALL SELECT 'cast ' || castsource::regtype || ' ' || casttarget::regtype FROM pg_cast WHERE casttarget IN (SELECT oid FROM pg_type WHERE typtype = 'e')
        """;

    [Fact]
    public async Task InitialMigrationBuildsTheSameSchemaAsEnsureCreated()
    {
        var oldDb = await NewDatabase();
        var newDb = await NewDatabase();
        try
        {
            await using (var db = Context(oldDb))
            {
                await db.Database.EnsureCreatedAsync();
                await db.Database.ExecuteSqlRawAsync(OldCastsSql);
            }
            await using (var db = Context(newDb))
                await db.Database.MigrateAsync("InitialCreate");

            var expected = await Schema(oldDb);
            Assert.Contains(expected, line => line.StartsWith("cast "));
            Assert.Contains(expected, line => line.StartsWith("idx "));
            Assert.Equal(expected, await Schema(newDb));
        }
        finally { await Drop(oldDb); await Drop(newDb); }
    }

    private const string OldCastsSql = """
        CREATE CAST (text AS employee_parking_status) WITH INOUT AS IMPLICIT;
        CREATE CAST (text AS account_role) WITH INOUT AS IMPLICIT;
        CREATE CAST (text AS parking_release_status) WITH INOUT AS IMPLICIT;
        CREATE CAST (text AS parking_claim_status) WITH INOUT AS IMPLICIT
        """;

    private static AppDb Context(string connection) =>
        new(new DbContextOptionsBuilder<AppDb>().UseNpgsql(connection).Options);

    private static async Task<List<string>> Schema(string connection)
    {
        await using var c = new NpgsqlConnection(connection);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(SchemaSql, c);
        await using var r = await cmd.ExecuteReaderAsync();
        var lines = new List<string>();
        while (await r.ReadAsync()) lines.Add(r.GetString(0));
        return [.. lines.Order(StringComparer.Ordinal)];
    }

    private static async Task<string> NewDatabase()
    {
        var name = $"t_{Guid.NewGuid():N}";
        await Exec($"CREATE DATABASE \"{name}\"");
        return new NpgsqlConnectionStringBuilder(Server) { Database = name }.ConnectionString;
    }

    private static async Task Drop(string connection)
    {
        NpgsqlConnection.ClearAllPools();
        await Exec($"DROP DATABASE IF EXISTS \"{new NpgsqlConnectionStringBuilder(connection).Database}\" WITH (FORCE)");
    }

    private static async Task Exec(string sql)
    {
        await using var c = new NpgsqlConnection(Server);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }
}

public class MigrationStartupTests
{
    [Fact]
    public async Task StartingOnADatabaseBuiltByEnsureCreatedKeepsItsRowsAndAppliesOnlyNewMigrations()
    {
        using var factory = new AppFactory();
        await using (var old = new AppDb(new DbContextOptionsBuilder<AppDb>().UseNpgsql(factory.ConnectionString).Options))
        {
            await old.Database.EnsureCreatedAsync(); // what the code before GTMS-32 did
            await old.Database.ExecuteSqlRawAsync("""
                CREATE CAST (text AS employee_parking_status) WITH INOUT AS IMPLICIT;
                CREATE CAST (text AS account_role) WITH INOUT AS IMPLICIT;
                CREATE CAST (text AS parking_release_status) WITH INOUT AS IMPLICIT;
                CREATE CAST (text AS parking_claim_status) WITH INOUT AS IMPLICIT;
                INSERT INTO "Employees" ("EmployeeId", "Name", "Email", "Role", "ParkingStatus")
                VALUES ('EMP-OLD', 'Old Row', 'old@example.test', 'Engineer', 'Temporary')
                """);
        }

        _ = factory.Services; // starts the app: baseline, then Migrate

        await using var db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseNpgsql(factory.ConnectionString).Options);
        Assert.Equal("Old Row", await db.Employees.Where(e => e.EmployeeId == "EMP-OLD").Select(e => e.Name).SingleAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(db.Database.GetMigrations().Count(), (await db.Database.GetAppliedMigrationsAsync()).Count());
    }

    [Fact]
    public async Task StartingOnAnEmptyDatabaseAppliesAllMigrations()
    {
        using var factory = new AppFactory();
        _ = factory.Services;
        await using var db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseNpgsql(factory.ConnectionString).Options);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(db.Database.GetMigrations().ToArray(), (await db.Database.GetAppliedMigrationsAsync()).ToArray());
    }

    [Fact]
    public async Task DemoSeedingStillWorksAfterMigrations()
    {
        using var factory = new AppFactory();
        using var seeded = factory.WithWebHostBuilder(b => b.UseSetting("Seed:Demo", "true"));
        _ = seeded.Services;
        await using var db = new AppDb(new DbContextOptionsBuilder<AppDb>().UseNpgsql(factory.ConnectionString).Options);
        Assert.True(await db.Employees.AnyAsync());
    }
}
