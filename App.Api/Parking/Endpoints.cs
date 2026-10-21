using System.Data;
using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace App.Api;

internal sealed class ParkingEndpoints : IApiEndpointModule
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/parking/dashboard", GetDashboard)
            .WithName("getParkingDashboard")
            .RequireAuthorization()
            .Produces<Dashboard>(StatusCodes.Status200OK)
            .Produces<Error>(StatusCodes.Status401Unauthorized);

        endpoints.MapPost("/parking/releases/today", ReleaseToday)
            .WithName("releaseTodaysParking")
            .RequireAuthorization()
            .Produces<ReleaseRecord>(StatusCodes.Status200OK)
            .Produces<Error>(StatusCodes.Status401Unauthorized)
            .Produces<Error>(StatusCodes.Status403Forbidden)
            .Produces<Error>(StatusCodes.Status409Conflict);

        endpoints.MapGet("/parking/releases", ListReleases)
            .WithName("listMyReleases")
            .RequireAuthorization()
            .Produces<ReleaseList>(StatusCodes.Status200OK)
            .Produces<Error>(StatusCodes.Status401Unauthorized);

        endpoints.MapPost("/parking/releases", ScheduleReleases)
            .WithName("scheduleReleases")
            .RequireAuthorization()
            .Produces<ReleaseList>(StatusCodes.Status201Created)
            .Produces<Error>(StatusCodes.Status400BadRequest)
            .Produces<Error>(StatusCodes.Status401Unauthorized)
            .Produces<Error>(StatusCodes.Status403Forbidden)
            .Produces<Error>(StatusCodes.Status409Conflict);

        endpoints.MapPost("/parking/releases/{releaseId:int}/cancel", CancelRelease)
            .WithName("cancelRelease")
            .RequireAuthorization()
            .Produces<ReleaseActionResult>(StatusCodes.Status200OK)
            .Produces<Error>(StatusCodes.Status401Unauthorized)
            .Produces<Error>(StatusCodes.Status403Forbidden)
            .Produces<Error>(StatusCodes.Status404NotFound)
            .Produces<Error>(StatusCodes.Status409Conflict);

        endpoints.MapGet("/parking/availability", GetAvailability)
            .WithName("getParkingAvailability")
            .RequireAuthorization()
            .Produces<Availability>(StatusCodes.Status200OK)
            .Produces<Error>(StatusCodes.Status401Unauthorized);

        endpoints.MapPost("/parking/claims", ClaimParkingSpace)
            .WithName("claimParkingSpace")
            .RequireAuthorization()
            .Produces<ParkingClaim>(StatusCodes.Status201Created)
            .Produces<Error>(StatusCodes.Status400BadRequest)
            .Produces<Error>(StatusCodes.Status401Unauthorized)
            .Produces<Error>(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> GetDashboard(
        HttpContext context,
        AppDb db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryGetEmployeeId(context, out var employeeId))
        {
            return Unauthorized();
        }

        var employee = await db.Employees.SingleAsync(candidate => candidate.Id == employeeId, cancellationToken);
        var now = AuthTime.ToDatabasePrecision(timeProvider.GetUtcNow());
        var officeDate = DateOnly.FromDateTime(now.UtcDateTime);
        var releases = await db.Set<ParkingRelease>()
            .Include(release => release.CreatedByEmployee)
            .Include(release => release.LastChangedByEmployee)
            .Where(release => release.EmployeeId == employeeId && release.Date == officeDate)
            .OrderBy(release => release.Id)
            .ToListAsync(cancellationToken);
        var currentClaimEntity = await db.Set<ParkingClaimEntity>()
            .Include(claim => claim.CreatedByEmployee)
            .Include(claim => claim.LastChangedByEmployee)
            .SingleOrDefaultAsync(claim => claim.EmployeeId == employeeId && claim.Date == officeDate, cancellationToken);
        var availability = await BuildAvailability(db, officeDate, now, cancellationToken);
        var notice = new AvailabilityNotice(
            $"There are {availability.Count} parking spaces available.",
            employee.Email);

        return Results.Ok(new Dashboard(
            new DashboardEmployee(
                employee.Id,
                employee.Name,
                employee.Email,
                employee.ParkingStatus == EmployeeParkingStatus.Dedicated
                    ? ProfileParkingStatus.Dedicated
                    : ProfileParkingStatus.Temporary),
            officeDate,
            releases.Select(ToReleaseRecord).ToArray(),
            availability,
            currentClaimEntity is null ? null : ToParkingClaim(currentClaimEntity),
            notice,
            []));
    }

    private static async Task<IResult> ReleaseToday(
        TodayReleaseRequest request,
        HttpContext context,
        AppDb db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryGetEmployeeId(context, out var employeeId))
        {
            return Unauthorized();
        }

        var now = AuthTime.ToDatabasePrecision(timeProvider.GetUtcNow());
        var officeDate = DateOnly.FromDateTime(now.UtcDateTime);
        if (request.OfficeDate == default || request.OfficeDate != officeDate)
        {
            return Conflict("Only today's eligible office date can be released.");
        }

        var employee = await db.Employees.SingleAsync(candidate => candidate.Id == employeeId, cancellationToken);
        if (employee.ParkingStatus != EmployeeParkingStatus.Dedicated)
        {
            return Forbidden();
        }

        if (await db.Set<ParkingRelease>().AnyAsync(
                release => release.EmployeeId == employeeId && release.Date == officeDate,
                cancellationToken))
        {
            return Conflict("A release already exists for this date.");
        }

        var release = NewRelease(employee, officeDate, ParkingReleaseStatus.Released, now, now);
        db.Set<ParkingRelease>().Add(release);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict("A release already exists for this date.");
        }

        return Results.Ok(ToReleaseRecord(release));
    }

    private static async Task<IResult> ListReleases(
        DateOnly? dateFrom,
        DateOnly? dateTo,
        HttpContext context,
        AppDb db,
        CancellationToken cancellationToken)
    {
        if (!TryGetEmployeeId(context, out var employeeId))
        {
            return Unauthorized();
        }

        var query = db.Set<ParkingRelease>()
            .Include(release => release.CreatedByEmployee)
            .Include(release => release.LastChangedByEmployee)
            .Where(release => release.EmployeeId == employeeId);
        if (dateFrom is not null)
        {
            query = query.Where(release => release.Date >= dateFrom.Value);
        }

        if (dateTo is not null)
        {
            query = query.Where(release => release.Date <= dateTo.Value);
        }

        var releases = await query.OrderBy(release => release.Date).ThenBy(release => release.Id)
            .ToListAsync(cancellationToken);
        return Results.Ok(new ReleaseList(releases.Select(ToReleaseRecord).ToArray()));
    }

    private static async Task<IResult> ScheduleReleases(
        ReleaseScheduleRequest request,
        HttpContext context,
        AppDb db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryGetEmployeeId(context, out var employeeId))
        {
            return Unauthorized();
        }

        if (request.Dates is null || request.Dates.Count == 0
            || request.Dates.Any(date => date == default)
            || request.Dates.Distinct().Count() != request.Dates.Count)
        {
            return BadRequest("Provide a non-empty list of distinct office dates.");
        }

        var employee = await db.Employees.SingleAsync(candidate => candidate.Id == employeeId, cancellationToken);
        if (employee.ParkingStatus != EmployeeParkingStatus.Dedicated)
        {
            return Forbidden();
        }

        var now = AuthTime.ToDatabasePrecision(timeProvider.GetUtcNow());
        var officeDate = DateOnly.FromDateTime(now.UtcDateTime);
        if (request.Dates.Any(date => date <= officeDate))
        {
            return BadRequest("Scheduled release dates must be in the future.");
        }

        var existingDates = await db.Set<ParkingRelease>()
            .Where(release => release.EmployeeId == employeeId && request.Dates.Contains(release.Date))
            .Select(release => release.Date)
            .ToListAsync(cancellationToken);
        if (existingDates.Count > 0)
        {
            return Conflict("A release already exists for one or more requested dates.");
        }

        var releases = request.Dates.OrderBy(date => date)
            .Select(date => NewRelease(employee, date, ParkingReleaseStatus.Reserved, now, ClaimableAt(date)))
            .ToArray();
        db.Set<ParkingRelease>().AddRange(releases);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            return Conflict("A release already exists for one or more requested dates.");
        }

        return Results.Json(new ReleaseList(releases.Select(ToReleaseRecord).ToArray()), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> CancelRelease(
        int releaseId,
        CancelReleaseRequest request,
        HttpContext context,
        AppDb db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryGetEmployeeId(context, out var employeeId))
        {
            return Unauthorized();
        }

        var release = await db.Set<ParkingRelease>()
            .Include(candidate => candidate.CreatedByEmployee)
            .Include(candidate => candidate.LastChangedByEmployee)
            .SingleOrDefaultAsync(candidate => candidate.Id == releaseId, cancellationToken);
        if (release is null)
        {
            return Results.Json(new Error("The requested release was not found.", "NOT_FOUND"), statusCode: StatusCodes.Status404NotFound);
        }

        if (release.EmployeeId != employeeId)
        {
            return Forbidden();
        }

        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || release.IsCancelled || release.Status == ParkingReleaseStatus.Claimed)
        {
            return Conflict("This release cannot be cancelled.");
        }

        var employee = await db.Employees.SingleAsync(candidate => candidate.Id == employeeId, cancellationToken);
        var now = AuthTime.ToDatabasePrecision(timeProvider.GetUtcNow());
        release.IsCancelled = true;
        release.CancellationReason = reason;
        release.LastChangedByEmployeeId = employee.Id;
        release.LastChangedByEmployee = employee;
        release.LastChangedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        var message = $"Release for {release.Date.ToString("ddd dd MMM", CultureInfo.InvariantCulture)} cancelled.";
        return Results.Ok(new ReleaseActionResult(message, ToReleaseRecord(release)));
    }

    private static async Task<IResult> GetAvailability(
        DateOnly? date,
        HttpContext context,
        AppDb db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryGetEmployeeId(context, out _))
        {
            return Unauthorized();
        }

        var now = AuthTime.ToDatabasePrecision(timeProvider.GetUtcNow());
        var officeDate = date ?? DateOnly.FromDateTime(now.UtcDateTime);
        return Results.Ok(await BuildAvailability(db, officeDate, now, cancellationToken));
    }

    private static async Task<IResult> ClaimParkingSpace(
        ClaimRequest request,
        HttpContext context,
        AppDb db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryGetEmployeeId(context, out var employeeId))
        {
            return Unauthorized();
        }

        var plate = request.Plate?.Trim();
        if (request.Date == default || string.IsNullOrWhiteSpace(plate))
        {
            return BadRequest("Provide a valid date and registered vehicle plate.");
        }

        var employee = await db.Employees.SingleAsync(candidate => candidate.Id == employeeId, cancellationToken);
        if (employee.ParkingStatus != EmployeeParkingStatus.Temporary)
        {
            return Conflict("This employee is not eligible for temporary parking.");
        }

        var normalizedPlate = plate.ToUpperInvariant();
        if (!await db.Set<VehicleEntity>().AnyAsync(
                vehicle => vehicle.EmployeeId == employeeId && vehicle.Plate.ToUpper() == normalizedPlate,
                cancellationToken))
        {
            return BadRequest("The vehicle plate is not registered to this employee.");
        }

        var now = AuthTime.ToDatabasePrecision(timeProvider.GetUtcNow());
        if (request.Date < DateOnly.FromDateTime(now.UtcDateTime))
        {
            return Conflict("Parking cannot be claimed for a past date.");
        }

        if (await db.Set<ParkingClaimEntity>().AnyAsync(
                claim => claim.EmployeeId == employeeId && claim.Date == request.Date,
                cancellationToken))
        {
            return Conflict("This employee already has a parking claim for the requested date.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var release = await db.Set<ParkingRelease>()
            .Include(candidate => candidate.Employee)
            .Where(candidate => candidate.Date == request.Date
                && !candidate.IsCancelled
                && candidate.ClaimableAt <= now
                && (candidate.Status == ParkingReleaseStatus.Released || candidate.Status == ParkingReleaseStatus.Reserved)
                && candidate.Area != null
                && !db.Set<ParkingClaimEntity>().Any(claim => claim.ReleaseId == candidate.Id))
            .OrderBy(candidate => candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (release is null)
        {
            return Conflict("No claimable parking space is available for the requested date.");
        }

        var claim = new ParkingClaimEntity
        {
            EmployeeId = employee.Id,
            Employee = employee,
            ReleaseId = release.Id,
            Release = release,
            Date = request.Date,
            VehiclePlate = plate,
            Area = release.Area!,
            Status = ParkingClaimStatus.Claimed,
            CreatedByEmployeeId = employee.Id,
            CreatedByEmployee = employee,
            CreatedAt = now,
            LastChangedByEmployeeId = employee.Id,
            LastChangedByEmployee = employee,
            LastChangedAt = now
        };
        db.Set<ParkingClaimEntity>().Add(claim);
        release.Status = ParkingReleaseStatus.Claimed;
        release.LastChangedByEmployeeId = employee.Id;
        release.LastChangedByEmployee = employee;
        release.LastChangedAt = now;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception) || IsSerializationFailure(exception))
        {
            return Conflict("No claimable parking space is available for the requested date.");
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            return Conflict("No claimable parking space is available for the requested date.");
        }

        return Results.Json(ToParkingClaim(claim), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<Availability> BuildAvailability(
        AppDb db,
        DateOnly date,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var releases = await db.Set<ParkingRelease>()
            .Where(release => release.Date == date && !release.IsCancelled)
            .OrderBy(release => release.Id)
            .ToListAsync(cancellationToken);
        var claimedReleaseIds = await db.Set<ParkingClaimEntity>()
            .Where(claim => claim.Date == date && claim.ReleaseId != null)
            .Select(claim => claim.ReleaseId!.Value)
            .ToListAsync(cancellationToken);
        var claimed = claimedReleaseIds.ToHashSet();
        var remaining = releases.Where(release => !claimed.Contains(release.Id) && !string.IsNullOrWhiteSpace(release.Area)).ToArray();
        var available = remaining.Where(release => release.ClaimableAt <= now
            && (release.Status == ParkingReleaseStatus.Released || release.Status == ParkingReleaseStatus.Reserved));
        var areas = available.GroupBy(release => release.Area!, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new AreaCount(group.Key, group.Count()))
            .ToArray();
        var notYetClaimable = remaining.Where(release => release.ClaimableAt > now && release.Status == ParkingReleaseStatus.Reserved)
            .GroupBy(release => new { release.Area, release.ClaimableAt })
            .OrderBy(group => group.Key.Area, StringComparer.Ordinal)
            .ThenBy(group => group.Key.ClaimableAt)
            .Select(group => new ScheduledArea(group.Key.Area!, group.Count(), group.Key.ClaimableAt))
            .ToArray();

        return new Availability(date, areas.Sum(area => area.Count), areas, notYetClaimable);
    }

    private static ParkingRelease NewRelease(Employee employee, DateOnly date, ParkingReleaseStatus status, DateTimeOffset now, DateTimeOffset claimableAt) => new()
    {
        EmployeeId = employee.Id,
        Employee = employee,
        Date = date,
        Status = status,
        Area = employee.AssignedFloor,
        CreatedByEmployeeId = employee.Id,
        CreatedByEmployee = employee,
        CreatedAt = now,
        LastChangedByEmployeeId = employee.Id,
        LastChangedByEmployee = employee,
        LastChangedAt = now,
        ClaimableAt = AuthTime.ToDatabasePrecision(claimableAt),
        IsCancelled = false
    };

    private static DateTimeOffset ClaimableAt(DateOnly date) =>
        new(date.ToDateTime(new TimeOnly(8, 0), DateTimeKind.Utc));

    private static ReleaseRecord ToReleaseRecord(ParkingRelease release) => new(
        release.Id,
        release.Date,
        release.Status,
        release.Area,
        release.CreatedByEmployee.Name,
        release.CreatedAt,
        release.LastChangedByEmployee.Name,
        release.LastChangedAt,
        release.ClaimableAt);

    private static ParkingClaim ToParkingClaim(ParkingClaimEntity claim) => new(
        claim.Id,
        claim.Date,
        claim.VehiclePlate,
        claim.Area,
        claim.Status,
        claim.CreatedByEmployee.Name,
        claim.CreatedAt,
        claim.LastChangedByEmployee.Name,
        claim.LastChangedAt);

    private static bool TryGetEmployeeId(HttpContext context, out int employeeId) =>
        int.TryParse(context.User.FindFirst("employeeId")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out employeeId);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static bool IsSerializationFailure(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure };

    private static IResult BadRequest(string message) =>
        Results.Json(new Error(message, "BAD_REQUEST"), statusCode: StatusCodes.Status400BadRequest);

    private static IResult Unauthorized() =>
        Results.Json(new Error("Sign in to continue.", "UNAUTHORIZED"), statusCode: StatusCodes.Status401Unauthorized);

    private static IResult Forbidden() =>
        Results.Json(new Error("You do not have access to this parking operation.", "FORBIDDEN"), statusCode: StatusCodes.Status403Forbidden);

    private static IResult Conflict(string message) =>
        Results.Json(new Error(message, "CONFLICT"), statusCode: StatusCodes.Status409Conflict);
}
