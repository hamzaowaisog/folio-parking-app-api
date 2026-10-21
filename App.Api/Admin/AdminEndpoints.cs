using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using App.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.Data;
using System.Reflection;
using EmployeeEntity = App.Api.Employee;
using AdminEmployee = App.Api.Admin.Employee;

namespace App.Api.Admin;

internal sealed class AdminEndpoints : IApiEndpointModule
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Map(IEndpointRouteBuilder endpoints)
    {
        EnsureAdministratorAccessStatusType(endpoints);

        endpoints.MapGet("/admin/employees", ListEmployees)
            .WithName("listEmployees")
            .RequireAuthorization("Administrator")
            .Produces<EmployeeList>(StatusCodes.Status200OK)
            .Produces<Error>(StatusCodes.Status401Unauthorized)
            .Produces<Error>(StatusCodes.Status403Forbidden);
        endpoints.MapPost("/admin/employees", CreateEmployee)
            .WithName("createEmployee")
            .RequireAuthorization("Administrator")
            .Produces<EmployeeRecord>(StatusCodes.Status201Created)
            .Produces<Error>(StatusCodes.Status400BadRequest)
            .Produces<Error>(StatusCodes.Status401Unauthorized)
            .Produces<Error>(StatusCodes.Status403Forbidden)
            .Produces<Error>(StatusCodes.Status409Conflict);
        endpoints.MapGet("/admin/employees/{employeeId}", GetEmployee)
            .WithName("getEmployee")
            .RequireAuthorization("Administrator")
            .Produces<EmployeeRecord>(StatusCodes.Status200OK)
            .Produces<Error>(StatusCodes.Status401Unauthorized)
            .Produces<Error>(StatusCodes.Status403Forbidden)
            .Produces<Error>(StatusCodes.Status404NotFound);
        endpoints.MapPut("/admin/employees/{employeeId}", UpdateEmployee)
            .WithName("updateEmployee")
            .RequireAuthorization("Administrator")
            .Produces<EmployeeRecord>(StatusCodes.Status200OK)
            .Produces<Error>(StatusCodes.Status400BadRequest)
            .Produces<Error>(StatusCodes.Status401Unauthorized)
            .Produces<Error>(StatusCodes.Status403Forbidden)
            .Produces<Error>(StatusCodes.Status404NotFound);
        endpoints.MapGet("/admin/settings", GetParkingSettings)
            .WithName("getParkingSettings")
            .RequireAuthorization("Administrator")
            .Produces<ParkingSettings>(StatusCodes.Status200OK)
            .Produces<Error>(StatusCodes.Status401Unauthorized)
            .Produces<Error>(StatusCodes.Status403Forbidden);
        endpoints.MapPut("/admin/settings", UpdateParkingSettings)
            .WithName("updateParkingSettings")
            .RequireAuthorization("Administrator")
            .Produces<ParkingSettings>(StatusCodes.Status200OK)
            .Produces<Error>(StatusCodes.Status400BadRequest)
            .Produces<Error>(StatusCodes.Status401Unauthorized)
            .Produces<Error>(StatusCodes.Status403Forbidden);
        endpoints.MapGet("/admin/admins", ListAdministrators)
            .WithName("listAdministrators")
            .RequireAuthorization("Administrator")
            .Produces<AdministratorList>(StatusCodes.Status200OK)
            .Produces<Error>(StatusCodes.Status401Unauthorized)
            .Produces<Error>(StatusCodes.Status403Forbidden);
        endpoints.MapPost("/admin/admins", GrantAdministratorAccess)
            .WithName("grantAdministratorAccess")
            .RequireAuthorization("Administrator")
            .Produces<AdminAccessResult>(StatusCodes.Status200OK)
            .Produces<Error>(StatusCodes.Status400BadRequest)
            .Produces<Error>(StatusCodes.Status401Unauthorized)
            .Produces<Error>(StatusCodes.Status403Forbidden)
            .Produces<Error>(StatusCodes.Status409Conflict);
        endpoints.MapGet("/admin/admins/directory", SearchEmployeeDirectory)
            .WithName("searchEmployeeDirectory")
            .RequireAuthorization("Administrator")
            .Produces<DirectorySearchResult>(StatusCodes.Status200OK)
            .Produces<Error>(StatusCodes.Status401Unauthorized)
            .Produces<Error>(StatusCodes.Status403Forbidden);
        endpoints.MapDelete("/admin/admins/{employeeId}", RevokeAdministratorAccess)
            .WithName("revokeAdministratorAccess")
            .RequireAuthorization("Administrator")
            .Produces<AdminAccessResult>(StatusCodes.Status200OK)
            .Produces<Error>(StatusCodes.Status401Unauthorized)
            .Produces<Error>(StatusCodes.Status403Forbidden)
            .Produces<Error>(StatusCodes.Status404NotFound);
    }

    private static void EnsureAdministratorAccessStatusType(IEndpointRouteBuilder endpoints)
    {
        if (Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider") return;

        using var scope = endpoints.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        db.Database.ExecuteSqlRaw("""
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_type WHERE typname = 'administrator_access_status'
                ) THEN
                    CREATE TYPE administrator_access_status AS ENUM ('active', 'revoked');
                END IF;
            END
            $$;
            ALTER TABLE "AdministratorAccess"
                ALTER COLUMN "AccessStatus" TYPE administrator_access_status
                USING "AccessStatus"::administrator_access_status;
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_cast
                    WHERE castsource = 'text'::regtype
                      AND casttarget = 'administrator_access_status'::regtype
                ) THEN
                    CREATE CAST (text AS administrator_access_status) WITH INOUT AS IMPLICIT;
                END IF;
            END
            $$;
            """);
    }

    private static async Task<IResult> ListEmployees(
        AppDb db,
        string? search,
        string? parkingStatus,
        CancellationToken cancellationToken)
    {
        var query = db.Employees.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(employee => employee.EmployeeId.ToLower().Contains(term)
                || employee.Name.ToLower().Contains(term)
                || employee.Email.ToLower().Contains(term)
                || (employee.Department != null && employee.Department.ToLower().Contains(term))
                || employee.Role.ToLower().Contains(term));
        }

        if (parkingStatus is not null)
        {
            if (!Enum.TryParse<ParkingStatusFilter>(parkingStatus, out var status)
                || !Enum.IsDefined(status))
            {
                query = query.Where(_ => false);
            }
            else
            {
                var employeeStatus = status == ParkingStatusFilter.Dedicated
                    ? EmployeeParkingStatus.Dedicated
                    : EmployeeParkingStatus.Temporary;
                query = query.Where(employee => employee.ParkingStatus == employeeStatus);
            }
        }

        var total = await query.CountAsync(cancellationToken);
        var employees = await query.OrderBy(employee => employee.Id).ToListAsync(cancellationToken);
        var views = new Employee[employees.Count];
        for (var index = 0; index < employees.Count; index++)
        {
            views[index] = await CreateEmployeeView(db, employees[index], cancellationToken);
        }

        return Results.Ok(new EmployeeList(views, total));
    }

    private static async Task<IResult> CreateEmployee(
        EmployeeInput request,
        AppDb db,
        CancellationToken cancellationToken)
    {
        if (!IsValid(request)) return BadRequest();

        var employeeId = request.EmployeeId.Trim();
        var email = request.Email.Trim();
        if (await HasEmployeeIdentityConflict(db, employeeId, email, null, cancellationToken))
        {
            return Conflict("An employee with this ID or email already exists.");
        }

        var employee = ToEmployee(request, employeeId, email);
        db.Employees.Add(employee);
        foreach (var vehicle in request.Vehicles)
        {
            db.Set<VehicleEntity>().Add(ToVehicle(vehicle, employee.Id, employee));
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict("An employee with this ID or email already exists.");
        }

        return Results.Json(
            new EmployeeRecord(await CreateEmployeeView(db, employee, cancellationToken)),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> GetEmployee(int employeeId, AppDb db, CancellationToken cancellationToken)
    {
        var employee = await db.Employees.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == employeeId, cancellationToken);
        if (employee is null) return NotFound();
        return Results.Ok(new EmployeeRecord(await CreateEmployeeView(db, employee, cancellationToken)));
    }

    private static async Task<IResult> UpdateEmployee(
        int employeeId,
        EmployeeInput request,
        AppDb db,
        CancellationToken cancellationToken)
    {
        if (!IsValid(request)) return BadRequest();

        var employee = await db.Employees
            .Include(candidate => candidate.Account)
            .SingleOrDefaultAsync(candidate => candidate.Id == employeeId, cancellationToken);
        if (employee is null) return NotFound();

        var employeeCode = request.EmployeeId.Trim();
        var email = request.Email.Trim();
        if (await HasEmployeeIdentityConflict(db, employeeCode, email, employee.Id, cancellationToken))
        {
            return BadRequest("An employee with this ID or email already exists.");
        }

        employee.EmployeeId = employeeCode;
        employee.Name = request.Name.Trim();
        employee.Email = email;
        employee.Department = request.Department.Trim();
        employee.Role = request.Role.Trim();
        employee.ParkingStatus = ToEmployeeParkingStatus(request.ParkingStatus);
        employee.AssignedFloor = NormalizeOptional(request.AssignedFloor);
        if (employee.Account is not null)
        {
            employee.Account.Email = email;
        }

        await UpdateVehicles(db, employee, request.Vehicles, cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return BadRequest("An employee with this ID or email already exists.");
        }

        return Results.Ok(new EmployeeRecord(await CreateEmployeeView(db, employee, cancellationToken)));
    }

    private static async Task<IResult> GetParkingSettings(AppDb db, CancellationToken cancellationToken)
    {
        var values = await db.Set<ParkingSetting>().AsNoTracking().ToDictionaryAsync(
            setting => setting.Key,
            setting => setting.Value,
            cancellationToken);
        return Results.Ok(new ParkingSettings
        {
            DailyReleaseTime = ReadSetting(values, "dailyReleaseTime", "08:00:00"),
            Areas = ReadSetting(values, "areas", new[]
            {
                new ParkingArea { Name = "Floor 1" },
                new ParkingArea { Name = "Floor 2" },
                new ParkingArea { Name = "Floor 3" }
            }),
            ReportRecipients = ReadSetting(values, "reportRecipients", new[] { "parking-reports@folio3.com" })
        });
    }

    private static async Task<IResult> UpdateParkingSettings(
        ParkingSettings request,
        AppDb db,
        CancellationToken cancellationToken)
    {
        if (!IsValid(request)) return BadRequest();

        var settings = new ParkingSettings
        {
            DailyReleaseTime = TimeOnly.Parse(request.DailyReleaseTime, CultureInfo.InvariantCulture)
                .ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            Areas = request.Areas.Select(area => new ParkingArea { Name = area.Name.Trim() }).ToArray(),
            ReportRecipients = request.ReportRecipients.Select(email => email.Trim()).ToArray()
        };
        await SaveSetting(db, "dailyReleaseTime", settings.DailyReleaseTime, cancellationToken);
        await SaveSetting(db, "areas", settings.Areas, cancellationToken);
        await SaveSetting(db, "reportRecipients", settings.ReportRecipients, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(settings);
    }

    private static async Task<IResult> ListAdministrators(AppDb db, CancellationToken cancellationToken)
    {
        var administrators = await WithConnection(db, cancellationToken, async connection =>
        {
            await using var command = new NpgsqlCommand("""
                SELECT employee."Id", employee."Name", employee."Email", access."AccessStatus"::text
                FROM "AdministratorAccess" AS access
                INNER JOIN "Employees" AS employee ON employee."Id" = access."EmployeeId"
                ORDER BY access."EmployeeId"
                """, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<Administrator>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new Administrator(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    ParseAccessStatus(reader.GetString(3))));
            }

            return rows.ToArray();
        });
        return Results.Ok(new AdministratorList(administrators, administrators.Length));
    }

    private static async Task<IResult> GrantAdministratorAccess(
        AdminAccessRequest request,
        HttpContext context,
        AppDb db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (request.EmployeeId <= 0) return BadRequest();

        var employee = await db.Employees.SingleOrDefaultAsync(
            candidate => candidate.Id == request.EmployeeId,
            cancellationToken);
        if (employee is null) return BadRequest("The submitted values are not valid.");

        var currentStatus = await GetAccessStatus(db, employee.Id, cancellationToken);
        if (currentStatus == "active")
        {
            return Conflict("This employee already has administrator access.");
        }

        var changedByEmployeeId = await FindChangedByEmployeeId(context, employee.Id, db, cancellationToken);
        try
        {
            await SaveAdministratorAccess(
                db,
                employee.Id,
                "active",
                changedByEmployeeId,
                AuthTime.ToDatabasePrecision(timeProvider.GetUtcNow()),
                cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Conflict("This employee already has administrator access.");
        }

        var administrator = new Administrator(employee.Id, employee.Name, employee.Email, AdministratorAccessStatus.Active);
        return Results.Ok(new AdminAccessResult(administrator, $"{employee.Name} is now an administrator."));
    }

    private static async Task<IResult> SearchEmployeeDirectory(
        string search,
        AppDb db,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return Results.Ok(new DirectorySearchResult([], 0));
        }

        var term = search.Trim().ToLowerInvariant();
        var employees = await db.Employees.AsNoTracking()
            .Where(employee => employee.Name.ToLower().Contains(term)
                || employee.Email.ToLower().Contains(term)
                || employee.EmployeeId.ToLower().Contains(term))
            .OrderBy(employee => employee.Id)
            .Select(employee => new DirectoryEmployee(employee.Id, employee.Name, employee.Email))
            .ToArrayAsync(cancellationToken);
        return Results.Ok(new DirectorySearchResult(employees, employees.Length));
    }

    private static async Task<IResult> RevokeAdministratorAccess(
        int employeeId,
        HttpContext context,
        AppDb db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var employee = await GetAdministratorEmployee(db, employeeId, cancellationToken);
        if (employee is null) return NotFound();

        var changedByEmployeeId = await FindChangedByEmployeeId(context, employeeId, db, cancellationToken);
        await SaveAdministratorAccess(
            db,
            employeeId,
            "revoked",
            changedByEmployeeId,
            AuthTime.ToDatabasePrecision(timeProvider.GetUtcNow()),
            cancellationToken);
        var administrator = new Administrator(employee.Id, employee.Name, employee.Email, AdministratorAccessStatus.Revoked);
        return Results.Ok(new AdminAccessResult(administrator, $"Access removed for {employee.Name}."));
    }

    private static async Task<AdminEmployee> CreateEmployeeView(
        AppDb db,
        EmployeeEntity employee,
        CancellationToken cancellationToken)
    {
        var vehicles = await db.Set<VehicleEntity>().AsNoTracking()
            .Where(vehicle => vehicle.EmployeeId == employee.Id)
            .OrderBy(vehicle => vehicle.Id)
            .Select(vehicle => new EmployeeVehicle(vehicle.Id, vehicle.Plate, vehicle.Make, vehicle.Model, vehicle.Color))
            .ToArrayAsync(cancellationToken);
        var releases = await db.Set<ParkingRelease>().AsNoTracking()
            .Where(release => release.EmployeeId == employee.Id && !release.IsCancelled)
            .Select(release => new DatedActivity(release.Date, DatedActivityKind.Release, release.Status))
            .ToListAsync(cancellationToken);
        var claims = await db.Set<ParkingClaimEntity>().AsNoTracking()
            .Where(claim => claim.EmployeeId == employee.Id)
            .Select(claim => new DatedActivity(claim.Date, DatedActivityKind.Claim, ParkingReleaseStatus.Claimed))
            .ToListAsync(cancellationToken);
        var activities = releases.Concat(claims)
            .OrderBy(activity => activity.Date)
            .ThenBy(activity => activity.Kind)
            .ToArray();

        return new AdminEmployee(
            employee.Id,
            employee.EmployeeId,
            employee.Name,
            employee.Email,
            employee.Department ?? string.Empty,
            employee.Role,
            employee.ParkingStatus == EmployeeParkingStatus.Dedicated
                ? ParkingStatusFilter.Dedicated
                : ParkingStatusFilter.Temporary,
            employee.AssignedFloor,
            vehicles,
            activities);
    }

    private static async Task<bool> HasEmployeeIdentityConflict(
        AppDb db,
        string employeeCode,
        string email,
        int? exceptId,
        CancellationToken cancellationToken)
    {
        var normalizedCode = employeeCode.ToLowerInvariant();
        var normalizedEmail = email.ToLowerInvariant();
        var query = db.Employees.AsQueryable();
        if (exceptId.HasValue) query = query.Where(employee => employee.Id != exceptId.Value);
        return await query.AnyAsync(employee => employee.EmployeeId.ToLower() == normalizedCode
            || employee.Email.ToLower() == normalizedEmail, cancellationToken);
    }

    private static bool IsValid(EmployeeInput request)
    {
        var email = request.Email?.Trim();
        return !string.IsNullOrWhiteSpace(request.EmployeeId)
            && !string.IsNullOrWhiteSpace(request.Name)
            && !string.IsNullOrWhiteSpace(email)
            && new EmailAddressAttribute().IsValid(email)
            && !string.IsNullOrWhiteSpace(request.Department)
            && !string.IsNullOrWhiteSpace(request.Role)
            && Enum.IsDefined(request.ParkingStatus)
            && request.Vehicles is not null
            && request.Vehicles.All(vehicle => vehicle is not null && !string.IsNullOrWhiteSpace(vehicle.Plate))
            && request.Vehicles.Select(vehicle => vehicle?.Plate?.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() == request.Vehicles.Length;
    }

    private static bool IsValid(ParkingSettings request) =>
        TimeOnly.TryParse(request.DailyReleaseTime, CultureInfo.InvariantCulture, out _)
        && request.Areas is { Length: > 0 }
        && request.Areas.All(area => area is not null && !string.IsNullOrWhiteSpace(area.Name))
        && request.Areas.Select(area => area?.Name?.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() == request.Areas.Length
        && request.ReportRecipients is not null
        && request.ReportRecipients.All(email => !string.IsNullOrWhiteSpace(email)
            && new EmailAddressAttribute().IsValid(email.Trim()));

    private static EmployeeEntity ToEmployee(EmployeeInput request, string employeeId, string email) => new()
    {
        EmployeeId = employeeId,
        Name = request.Name.Trim(),
        Email = email,
        Department = request.Department.Trim(),
        Role = request.Role.Trim(),
        ParkingStatus = ToEmployeeParkingStatus(request.ParkingStatus),
        AssignedFloor = NormalizeOptional(request.AssignedFloor)
    };

    private static EmployeeParkingStatus ToEmployeeParkingStatus(ParkingStatusFilter status) =>
        status == ParkingStatusFilter.Dedicated ? EmployeeParkingStatus.Dedicated : EmployeeParkingStatus.Temporary;

    private static VehicleEntity ToVehicle(EmployeeVehicleInput request, int employeeId, EmployeeEntity employee) => new()
    {
        EmployeeId = employeeId,
        Employee = employee,
        Plate = request.Plate.Trim(),
        Make = NormalizeOptional(request.Make),
        Model = NormalizeOptional(request.Model),
        Color = NormalizeOptional(request.Color)
    };

    private static async Task UpdateVehicles(
        AppDb db,
        EmployeeEntity employee,
        EmployeeVehicleInput[] requestedVehicles,
        CancellationToken cancellationToken)
    {
        var currentVehicles = await db.Set<VehicleEntity>()
            .Where(vehicle => vehicle.EmployeeId == employee.Id)
            .ToListAsync(cancellationToken);
        var remaining = currentVehicles.ToDictionary(vehicle => vehicle.Plate, StringComparer.OrdinalIgnoreCase);
        foreach (var input in requestedVehicles)
        {
            var plate = input.Plate.Trim();
            if (remaining.Remove(plate, out var vehicle))
            {
                vehicle.Plate = plate;
                vehicle.Make = NormalizeOptional(input.Make);
                vehicle.Model = NormalizeOptional(input.Model);
                vehicle.Color = NormalizeOptional(input.Color);
            }
            else
            {
                db.Set<VehicleEntity>().Add(ToVehicle(input, employee.Id, employee));
            }
        }

        db.Set<VehicleEntity>().RemoveRange(remaining.Values);
    }

    private static async Task SaveSetting<T>(AppDb db, string key, T value, CancellationToken cancellationToken)
    {
        var setting = await db.Set<ParkingSetting>().SingleOrDefaultAsync(candidate => candidate.Key == key, cancellationToken);
        var json = JsonDocument.Parse(JsonSerializer.Serialize(value, JsonOptions));
        if (setting is null)
        {
            db.Set<ParkingSetting>().Add(new ParkingSetting { Key = key, Value = json });
        }
        else
        {
            setting.Value = json;
        }
    }

    private static T ReadSetting<T>(Dictionary<string, JsonDocument> values, string key, T fallback) =>
        values.TryGetValue(key, out var value)
            ? value.RootElement.Deserialize<T>(JsonOptions) ?? fallback
            : fallback;

    private static async Task<int> FindChangedByEmployeeId(
        HttpContext context,
        int fallbackEmployeeId,
        AppDb db,
        CancellationToken cancellationToken)
    {
        var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("employeeId")?.Value
            ?? context.User.FindFirst("employee_id")?.Value;
        return int.TryParse(userId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var employeeId)
            && await db.Employees.AnyAsync(candidate => candidate.Id == employeeId, cancellationToken)
                ? employeeId
                : fallbackEmployeeId;
    }

    private static AdministratorAccessStatus ParseAccessStatus(string status) => status == "revoked"
        ? AdministratorAccessStatus.Revoked
        : AdministratorAccessStatus.Active;

    private static Task<string?> GetAccessStatus(AppDb db, int employeeId, CancellationToken cancellationToken) =>
        WithConnection(db, cancellationToken, async connection =>
        {
            await using var command = new NpgsqlCommand(
                "SELECT \"AccessStatus\"::text FROM \"AdministratorAccess\" WHERE \"EmployeeId\" = @employeeId",
                connection);
            command.Parameters.AddWithValue("employeeId", employeeId);
            return (string?)await command.ExecuteScalarAsync(cancellationToken);
        });

    private static Task<DirectoryEmployee?> GetAdministratorEmployee(AppDb db, int employeeId, CancellationToken cancellationToken) =>
        WithConnection(db, cancellationToken, async connection =>
        {
            await using var command = new NpgsqlCommand("""
                SELECT employee."Id", employee."Name", employee."Email"
                FROM "AdministratorAccess" AS access
                INNER JOIN "Employees" AS employee ON employee."Id" = access."EmployeeId"
                WHERE access."EmployeeId" = @employeeId
                """, connection);
            command.Parameters.AddWithValue("employeeId", employeeId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? new DirectoryEmployee(reader.GetInt32(0), reader.GetString(1), reader.GetString(2))
                : null;
        });

    private static Task SaveAdministratorAccess(
        AppDb db,
        int employeeId,
        string status,
        int changedByEmployeeId,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken) =>
        WithConnection(db, cancellationToken, async connection =>
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO "AdministratorAccess" ("EmployeeId", "AccessStatus", "ChangedByEmployeeId", "ChangedAt")
                VALUES (@employeeId, CAST(@status AS administrator_access_status), @changedByEmployeeId, @changedAt)
                ON CONFLICT ("EmployeeId") DO UPDATE
                SET "AccessStatus" = EXCLUDED."AccessStatus",
                    "ChangedByEmployeeId" = EXCLUDED."ChangedByEmployeeId",
                    "ChangedAt" = EXCLUDED."ChangedAt"
                """, connection);
            command.Parameters.AddWithValue("employeeId", employeeId);
            command.Parameters.AddWithValue("status", status);
            command.Parameters.AddWithValue("changedByEmployeeId", changedByEmployeeId);
            command.Parameters.AddWithValue("changedAt", changedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return true;
        });

    private static async Task<T> WithConnection<T>(
        AppDb db,
        CancellationToken cancellationToken,
        Func<NpgsqlConnection, Task<T>> operation)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await connection.OpenAsync(cancellationToken);
        try
        {
            return await operation(connection);
        }
        finally
        {
            if (shouldClose) await connection.CloseAsync();
        }
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static IResult BadRequest(string message = "The submitted values are not valid.") => Results.Json(
        new Error(message, "BAD_REQUEST"),
        statusCode: StatusCodes.Status400BadRequest);

    private static IResult Conflict(string message) => Results.Json(
        new Error(message, "CONFLICT"),
        statusCode: StatusCodes.Status409Conflict);

    private static IResult NotFound() => Results.Json(
        new Error("The requested record was not found.", "NOT_FOUND"),
        statusCode: StatusCodes.Status404NotFound);
}
