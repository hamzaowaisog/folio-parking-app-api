using Microsoft.EntityFrameworkCore;

namespace App.Api;

/// <summary>Brings the database up to date with the migrations. See docs/adr/0001.</summary>
public static class DatabaseMigrator
{
    public static void Run(AppDb db)
    {
        // a database the old EnsureCreated() code built has the tables but no history: record the initial migration as applied
        // (its tables and casts are already there), so only newer migrations run and no row is touched
        var builtByEnsureCreated = db.Database.SqlQueryRaw<bool>(
            """SELECT to_regclass('public."__EFMigrationsHistory"') IS NULL AND to_regclass('public."Employees"') IS NOT NULL AS "Value" """)
            .AsEnumerable().Single();
        if (builtByEnsureCreated)
        {
            var initial = db.Database.GetMigrations().First();
            db.Database.ExecuteSql($"""
                CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                    "MigrationId" character varying(150) NOT NULL,
                    "ProductVersion" character varying(32) NOT NULL,
                    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId"));
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ({initial}, '9.0.0') ON CONFLICT DO NOTHING;
                """);
        }

        db.Database.Migrate();
    }
}
