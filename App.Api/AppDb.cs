using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace App.Api;

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<SessionToken> SessionTokens => Set<SessionToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresEnum<EmployeeParkingStatus>(name: "employee_parking_status");
        modelBuilder.HasPostgresEnum<AccountRole>(name: "account_role");
        modelBuilder.HasPostgresEnum<ParkingReleaseStatus>(name: "parking_release_status");
        modelBuilder.HasPostgresEnum<ParkingClaimStatus>(name: "parking_claim_status");

        foreach (var entityType in Assembly.GetExecutingAssembly().GetTypes()
                     .Where(type => type is { IsAbstract: false, IsInterface: false } && typeof(IAppEntity).IsAssignableFrom(type)))
        {
            modelBuilder.Entity(entityType);
        }

        modelBuilder.Entity<Employee>(entity =>
        {
            entity.ToTable("Employees");
            entity.HasKey(employee => employee.Id);
            entity.HasIndex(employee => employee.EmployeeId).IsUnique();
            entity.HasIndex(employee => employee.Email).IsUnique();
        });

        modelBuilder.Entity<Account>(entity =>
        {
            entity.ToTable("Accounts");
            entity.HasKey(account => account.Id);
            entity.HasIndex(account => account.EmployeeId).IsUnique();
            entity.HasIndex(account => account.Email).IsUnique();
            entity.HasOne(account => account.Employee)
                .WithOne(employee => employee.Account)
                .HasForeignKey<Account>(account => account.EmployeeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SessionToken>(entity =>
        {
            entity.ToTable("SessionTokens");
            entity.HasKey(token => token.Id);
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.HasOne(token => token.Account)
                .WithMany(account => account.SessionTokens)
                .HasForeignKey(token => token.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
