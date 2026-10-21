using App.Api.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace App.Api;

// Sample rows for a person trying the product. The app calls Run at startup, after the tables exist, only when the setting
// Seed:Demo is true (the factory's run files set it; tests never do). Insert missing rows by their stable keys only.
public static class SeedData
{
    private static readonly DateOnly DemoDate = new(2025, 10, 14);
    private static readonly DateTimeOffset DemoInstant = new(2025, 10, 14, 3, 1, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiredInstant = new(2024, 10, 14, 3, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Run(AppDb db)
    {
        ArgumentNullException.ThrowIfNull(db);

        db.Database.ExecuteSqlRaw(EnumCastsSql);

        var employees = SeedEmployees(db);
        db.SaveChanges();

        var accounts = SeedAccounts(db, employees);
        db.SaveChanges();

        SeedSessionTokens(db, accounts);
        SeedVehicles(db, employees);
        var releases = SeedReleases(db, employees);
        db.SaveChanges();

        SeedClaims(db, employees, releases);
        db.SaveChanges();

        SeedSettingsAdministratorsAndVerifications(db, employees);
    }

    private static Dictionary<string, Employee> SeedEmployees(AppDb db)
    {
        var employees = new Dictionary<string, Employee>(StringComparer.Ordinal);
        foreach (var sample in EmployeeSamples)
        {
            var employee = db.Employees.SingleOrDefault(row => row.EmployeeId == sample.EmployeeId)
                ?? db.Employees.SingleOrDefault(row => row.Email == sample.Email);
            if (employee is null)
            {
                employee = new Employee
                {
                    EmployeeId = sample.EmployeeId,
                    Name = sample.Name,
                    Email = sample.Email,
                    Department = sample.Department,
                    Role = sample.Role,
                    ParkingStatus = sample.ParkingStatus,
                    AssignedFloor = sample.AssignedFloor
                };
                db.Employees.Add(employee);
            }

            employees.Add(sample.EmployeeId, employee);
        }

        return employees;
    }

    private static Dictionary<string, Account> SeedAccounts(AppDb db, IReadOnlyDictionary<string, Employee> employees)
    {
        var accounts = new Dictionary<string, Account>(StringComparer.Ordinal);
        var passwordHasher = new PasswordHasher<Account>();
        foreach (var sample in AccountSamples)
        {
            var employee = employees[sample.EmployeeId];
            var account = db.Accounts.SingleOrDefault(row => row.EmployeeId == employee.Id)
                ?? db.Accounts.SingleOrDefault(row => row.Email == sample.Email);
            if (account is null)
            {
                account = new Account
                {
                    EmployeeId = employee.Id,
                    Employee = employee,
                    Email = sample.Email,
                    PasswordHash = string.Empty,
                    Role = sample.Role,
                    CreatedAt = DemoInstant
                };
                account.PasswordHash = passwordHasher.HashPassword(account, "Parking2025!");
                db.Accounts.Add(account);
            }

            accounts.Add(sample.EmployeeId, account);
        }

        return accounts;
    }

    private static void SeedSessionTokens(AppDb db, IReadOnlyDictionary<string, Account> accounts)
    {
        foreach (var sample in SessionTokenSamples)
        {
            var tokenHash = SessionTokenHasher.Hash(sample.Token);
            if (db.Set<SessionToken>().Any(token => token.Id == sample.Id || token.TokenHash == tokenHash))
                continue;

            db.Set<SessionToken>().Add(new SessionToken
            {
                Id = sample.Id,
                AccountId = accounts[sample.EmployeeId].Id,
                Account = accounts[sample.EmployeeId],
                TokenHash = tokenHash,
                ExpiresAt = ExpiredInstant,
                RevokedAt = ExpiredInstant
            });
        }
    }

    private static void SeedVehicles(AppDb db, IReadOnlyDictionary<string, Employee> employees)
    {
        foreach (var sample in VehicleSamples)
        {
            var employee = employees[sample.EmployeeId];
            if (db.Set<VehicleEntity>().Any(vehicle => vehicle.EmployeeId == employee.Id && vehicle.Plate == sample.Plate))
                continue;

            db.Set<VehicleEntity>().Add(new VehicleEntity
            {
                EmployeeId = employee.Id,
                Employee = employee,
                Plate = sample.Plate,
                Make = sample.Make,
                Model = sample.Model,
                Color = sample.Color
            });
        }
    }

    private static Dictionary<(string EmployeeId, DateOnly Date), ParkingRelease> SeedReleases(
        AppDb db,
        IReadOnlyDictionary<string, Employee> employees)
    {
        var releases = new Dictionary<(string EmployeeId, DateOnly Date), ParkingRelease>();
        foreach (var sample in ReleaseSamples)
        {
            var employee = employees[sample.EmployeeId];
            var release = db.Set<ParkingRelease>().SingleOrDefault(row => row.EmployeeId == employee.Id && row.Date == sample.Date);
            if (release is null)
            {
                var changedBy = employees[sample.ChangedByEmployeeId];
                release = new ParkingRelease
                {
                    EmployeeId = employee.Id,
                    Employee = employee,
                    Date = sample.Date,
                    Status = sample.Status,
                    Area = sample.Area,
                    CreatedByEmployeeId = changedBy.Id,
                    CreatedByEmployee = changedBy,
                    CreatedAt = sample.CreatedAt,
                    LastChangedByEmployeeId = changedBy.Id,
                    LastChangedByEmployee = changedBy,
                    LastChangedAt = sample.CreatedAt,
                    ClaimableAt = sample.ClaimableAt,
                    IsCancelled = false
                };
                db.Set<ParkingRelease>().Add(release);
            }

            releases.Add((sample.EmployeeId, sample.Date), release);
        }

        return releases;
    }

    private static void SeedClaims(
        AppDb db,
        IReadOnlyDictionary<string, Employee> employees,
        IReadOnlyDictionary<(string EmployeeId, DateOnly Date), ParkingRelease> releases)
    {
        foreach (var sample in ClaimSamples)
        {
            var employee = employees[sample.EmployeeId];
            if (db.Set<ParkingClaimEntity>().Any(claim => claim.EmployeeId == employee.Id && claim.Date == sample.Date))
                continue;

            var release = releases[(sample.ReleaseEmployeeId, sample.Date)];
            var releaseId = db.Set<ParkingClaimEntity>().Any(claim => claim.ReleaseId == release.Id)
                ? (int?)null
                : release.Id;
            db.Set<ParkingClaimEntity>().Add(new ParkingClaimEntity
            {
                EmployeeId = employee.Id,
                Employee = employee,
                ReleaseId = releaseId,
                Release = releaseId.HasValue ? release : null,
                Date = sample.Date,
                VehiclePlate = sample.Plate,
                Area = sample.Area,
                Status = ParkingClaimStatus.Claimed,
                CreatedByEmployeeId = employee.Id,
                CreatedByEmployee = employee,
                CreatedAt = sample.CreatedAt,
                LastChangedByEmployeeId = employee.Id,
                LastChangedByEmployee = employee,
                LastChangedAt = sample.CreatedAt
            });
        }
    }

    private static void SeedSettingsAdministratorsAndVerifications(
        AppDb db,
        IReadOnlyDictionary<string, Employee> employees)
    {
        var settingsToDispose = new List<ParkingSetting>();
        foreach (var sample in SettingSamples())
        {
            if (db.Set<ParkingSetting>().Any(setting => setting.Key == sample.Key))
            {
                sample.Value.Dispose();
                continue;
            }

            db.Set<ParkingSetting>().Add(sample);
            settingsToDispose.Add(sample);
        }

        foreach (var sample in AdministratorSamples)
        {
            var employee = employees[sample.EmployeeId];
            if (db.Set<AdministratorAccess>().Any(access => access.EmployeeId == employee.Id))
                continue;

            var changedBy = employees[sample.ChangedByEmployeeId];
            db.Set<AdministratorAccess>().Add(new AdministratorAccess
            {
                EmployeeId = employee.Id,
                Employee = employee,
                AccessStatus = sample.AccessStatus,
                ChangedByEmployeeId = changedBy.Id,
                ChangedByEmployee = changedBy,
                ChangedAt = DemoInstant
            });
        }

        foreach (var sample in VerificationSamples)
        {
            if (db.Set<VerificationRecord>().Any(record =>
                    record.Date == DemoDate && record.Time == sample.Time && record.Plate == sample.Plate))
                continue;

            db.Set<VerificationRecord>().Add(new VerificationRecord
            {
                Plate = sample.Plate,
                Driver = sample.Driver,
                Date = DemoDate,
                Time = sample.Time,
                Result = sample.Result,
                EntryStatus = sample.EntryStatus,
                IndicatorState = sample.IndicatorState,
                Reason = sample.Reason,
                PermanentSticker = sample.PermanentSticker
            });
        }

        try
        {
            db.SaveChanges();
        }
        finally
        {
            foreach (var setting in settingsToDispose)
                setting.Value.Dispose();
        }
    }

    private static ParkingSetting[] SettingSamples() =>
    [
        NewSetting("dailyReleaseTime", "08:00:00"),
        NewSetting("areas", new[] { new { name = "Floor 1" }, new { name = "Floor 2" }, new { name = "Floor 3" } }),
        NewSetting("reportRecipients", new[] { "parking-reports@folio3.com" })
    ];

    private static ParkingSetting NewSetting(string key, object value) => new()
    {
        Key = key,
        Value = JsonDocument.Parse(JsonSerializer.Serialize(value, JsonOptions))
    };

    private const string EnumCastsSql = """
        DO $$
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM pg_cast
                WHERE castsource = 'text'::regtype
                  AND casttarget = 'employee_parking_status'::regtype
            ) THEN
                EXECUTE 'CREATE CAST (text AS employee_parking_status) WITH INOUT AS IMPLICIT';
            END IF;
            IF NOT EXISTS (
                SELECT 1 FROM pg_cast
                WHERE castsource = 'text'::regtype
                  AND casttarget = 'account_role'::regtype
            ) THEN
                EXECUTE 'CREATE CAST (text AS account_role) WITH INOUT AS IMPLICIT';
            END IF;
            IF NOT EXISTS (
                SELECT 1 FROM pg_cast
                WHERE castsource = 'text'::regtype
                  AND casttarget = 'parking_release_status'::regtype
            ) THEN
                EXECUTE 'CREATE CAST (text AS parking_release_status) WITH INOUT AS IMPLICIT';
            END IF;
            IF NOT EXISTS (
                SELECT 1 FROM pg_cast
                WHERE castsource = 'text'::regtype
                  AND casttarget = 'parking_claim_status'::regtype
            ) THEN
                EXECUTE 'CREATE CAST (text AS parking_claim_status) WITH INOUT AS IMPLICIT';
            END IF;
        END
        $$;
        """;

    private static readonly EmployeeSample[] EmployeeSamples =
    [
        new("EMP-1042", "Ayesha Malik", "ayesha.malik@folio3.com", "Engineering", "Senior Engineer", EmployeeParkingStatus.Dedicated, "Floor 2"),
        new("EMP-1078", "Bilal Ahmed", "bilal.ahmed@folio3.com", "Operations", "Coordinator", EmployeeParkingStatus.Temporary, null),
        new("EMP-1220", "Daniyal Sheikh", "daniyal.sheikh@folio3.com", "Operations", "Coordinator", EmployeeParkingStatus.Temporary, null),
        new("EMP-1305", "Sana Raza", "sana.raza@folio3.com", "Facilities", "Gate Guard", EmployeeParkingStatus.Dedicated, "Floor 1")
    ];

    private static readonly AccountSample[] AccountSamples =
    [
        new("EMP-1042", "ayesha.malik@folio3.com", AccountRole.Employee),
        new("EMP-1078", "bilal.ahmed@folio3.com", AccountRole.Employee),
        new("EMP-1220", "daniyal.sheikh@folio3.com", AccountRole.Employee),
        new("EMP-1305", "sana.raza@folio3.com", AccountRole.GateGuard)
    ];

    private static readonly SessionTokenSample[] SessionTokenSamples =
    [
        new("EMP-1042", Guid.Parse("a5df9d7b-efc8-45eb-b68a-17e9a68a0101"), "demo-inert-session-ayesha"),
        new("EMP-1078", Guid.Parse("a5df9d7b-efc8-45eb-b68a-17e9a68a0102"), "demo-inert-session-bilal"),
        new("EMP-1305", Guid.Parse("a5df9d7b-efc8-45eb-b68a-17e9a68a0103"), "demo-inert-session-sana")
    ];

    private static readonly VehicleSample[] VehicleSamples =
    [
        new("EMP-1042", "BRD-139", "Suzuki", "Cultus", "Red"),
        new("EMP-1078", "AWC-917", "Toyota", "Corolla", "White"),
        new("EMP-1220", "DAN-220", "Honda", "City", "White")
    ];

    private static readonly ReleaseSample[] ReleaseSamples =
    [
        new("EMP-1042", DemoDate, ParkingReleaseStatus.Released, "Floor 2", "EMP-1042", DemoInstant, new DateTimeOffset(2025, 10, 14, 3, 0, 0, TimeSpan.Zero)),
        new("EMP-1220", DemoDate, ParkingReleaseStatus.Released, "Floor 3", "EMP-1220", DemoInstant, new DateTimeOffset(2025, 10, 14, 3, 0, 0, TimeSpan.Zero)),
        new("EMP-1042", new DateOnly(2025, 10, 13), ParkingReleaseStatus.Claimed, "Floor 2", "EMP-1042", DemoInstant, new DateTimeOffset(2025, 10, 13, 3, 0, 0, TimeSpan.Zero)),
        new("EMP-1078", new DateOnly(2025, 10, 17), ParkingReleaseStatus.Reserved, "Floor 2", "EMP-1078", DemoInstant, new DateTimeOffset(2025, 10, 17, 3, 0, 0, TimeSpan.Zero))
    ];

    private static readonly ClaimSample[] ClaimSamples =
    [
        new("EMP-1078", DemoDate, "EMP-1220", "AWC-917", "Floor 3", DemoInstant),
        new("EMP-1042", new DateOnly(2025, 10, 13), "EMP-1042", "BRD-139", "Floor 2", DemoInstant)
    ];

    private static readonly AdministratorSample[] AdministratorSamples =
    [
        new("EMP-1042", "active", "EMP-1042"),
        new("EMP-1078", "revoked", "EMP-1042")
    ];

    private static readonly VerificationSample[] VerificationSamples =
    [
        new("BRD-139", "Ayesha Malik", new TimeOnly(8, 19, 10), "Authorised", "Authorised", "green", "Dedicated parking authorisation confirmed.", true),
        new("KLM-305", "Sana Raza", new TimeOnly(8, 31, 2), "Existing process", "Existing process", "not used", "Permanent sticker. Use the existing process.", true),
        new("LXA-093", "Unknown driver", new TimeOnly(8, 24, 15), "Denied", "Denied", "off", "No valid authorisation today", false)
    ];

    private sealed record EmployeeSample(
        string EmployeeId,
        string Name,
        string Email,
        string Department,
        string Role,
        EmployeeParkingStatus ParkingStatus,
        string? AssignedFloor);

    private sealed record AccountSample(string EmployeeId, string Email, AccountRole Role);
    private sealed record SessionTokenSample(string EmployeeId, Guid Id, string Token);
    private sealed record VehicleSample(string EmployeeId, string Plate, string Make, string Model, string Color);
    private sealed record ReleaseSample(
        string EmployeeId,
        DateOnly Date,
        ParkingReleaseStatus Status,
        string Area,
        string ChangedByEmployeeId,
        DateTimeOffset CreatedAt,
        DateTimeOffset ClaimableAt);
    private sealed record ClaimSample(
        string EmployeeId,
        DateOnly Date,
        string ReleaseEmployeeId,
        string Plate,
        string Area,
        DateTimeOffset CreatedAt);
    private sealed record AdministratorSample(string EmployeeId, string AccessStatus, string ChangedByEmployeeId);
    private sealed record VerificationSample(
        string Plate,
        string Driver,
        TimeOnly Time,
        string Result,
        string EntryStatus,
        string IndicatorState,
        string Reason,
        bool PermanentSticker);
}
