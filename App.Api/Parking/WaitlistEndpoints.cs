using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace App.Api;

internal sealed class WaitlistEndpoints : IApiEndpointModule
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/parking/waitlist", Join)
            .WithName("joinParkingWaitlist")
            .RequireAuthorization()
            .Produces<WaitlistRecord>(StatusCodes.Status201Created)
            .Produces<Error>(StatusCodes.Status400BadRequest)
            .Produces<Error>(StatusCodes.Status401Unauthorized)
            .Produces<Error>(StatusCodes.Status403Forbidden)
            .Produces<Error>(StatusCodes.Status409Conflict);

        endpoints.MapDelete("/parking/waitlist/{date}", Leave)
            .WithName("leaveParkingWaitlist")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent)
            .Produces<Error>(StatusCodes.Status400BadRequest)
            .Produces<Error>(StatusCodes.Status401Unauthorized)
            .Produces<Error>(StatusCodes.Status404NotFound);

        endpoints.MapGet("/parking/waitlist", ListMine)
            .WithName("listMyWaitlist")
            .RequireAuthorization()
            .Produces<WaitlistList>(StatusCodes.Status200OK)
            .Produces<Error>(StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> Join(
        WaitlistJoinRequest request,
        HttpContext context,
        AppDb db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryGetEmployeeId(context, out var employeeId)) return Unauthorized();
        if (request.Date == default) return Fail(StatusCodes.Status400BadRequest, "BAD_REQUEST", "Provide a valid date.");

        var employee = await db.Employees.SingleAsync(candidate => candidate.Id == employeeId, cancellationToken);
        if (employee.ParkingStatus != EmployeeParkingStatus.Temporary)
            return Fail(StatusCodes.Status403Forbidden, "FORBIDDEN", "Only temporary-parking employees can join the waitlist.");

        var now = AuthTime.ToDatabasePrecision(timeProvider.GetUtcNow());
        if (request.Date < WaitlistAllocator.Today(now))
            return Fail(StatusCodes.Status400BadRequest, "BAD_REQUEST", "The waitlist date cannot be in the past.");

        var entries = db.Set<WaitlistEntry>();
        if (await entries.AnyAsync(entry => entry.EmployeeId == employeeId && entry.Date == request.Date
                && (entry.Status == WaitlistEntryStatus.Waiting || entry.Status == WaitlistEntryStatus.Offered), cancellationToken))
            return Conflict("This employee is already on the waitlist for the requested date.");
        if (await db.Set<ParkingClaimEntity>().AnyAsync(claim => claim.EmployeeId == employeeId && claim.Date == request.Date, cancellationToken))
            return Conflict("This employee already has a parking claim for the requested date.");
        if (await WaitlistAllocator.ClaimableFor(db, request.Date, now, employeeId).AnyAsync(cancellationToken))
            return Conflict("A parking space is available for the requested date. Claim it instead.");

        var entry = new WaitlistEntry { EmployeeId = employeeId, Date = request.Date, Status = WaitlistEntryStatus.Waiting, JoinedAt = now };
        entries.Add(entry);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Conflict("This employee is already on the waitlist for the requested date.");
        }

        return Results.Json(await ToRecord(db, entry, cancellationToken), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> Leave(
        string date,
        HttpContext context,
        AppDb db,
        CancellationToken cancellationToken)
    {
        if (!TryGetEmployeeId(context, out var employeeId)) return Unauthorized();
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return Fail(StatusCodes.Status400BadRequest, "BAD_REQUEST", "Provide the date as yyyy-MM-dd.");

        var entry = await db.Set<WaitlistEntry>().SingleOrDefaultAsync(candidate => candidate.EmployeeId == employeeId
            && candidate.Date == day
            && (candidate.Status == WaitlistEntryStatus.Waiting || candidate.Status == WaitlistEntryStatus.Offered), cancellationToken);
        if (entry is null) return Fail(StatusCodes.Status404NotFound, "NOT_FOUND", "The employee is not on the waitlist for this date.");

        entry.Status = WaitlistEntryStatus.Left;
        entry.ReleaseId = null;
        entry.HeldUntil = null;
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ListMine(
        HttpContext context,
        AppDb db,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TryGetEmployeeId(context, out var employeeId)) return Unauthorized();

        var today = WaitlistAllocator.Today(timeProvider.GetUtcNow());
        var mine = await db.Set<WaitlistEntry>().Include(entry => entry.Release)
            .Where(entry => entry.EmployeeId == employeeId && entry.Date >= today)
            .OrderBy(entry => entry.Date).ThenBy(entry => entry.Id)
            .ToListAsync(cancellationToken);
        var records = new List<WaitlistRecord>();
        foreach (var entry in mine) records.Add(await ToRecord(db, entry, cancellationToken));
        return Results.Ok(new WaitlistList(records));
    }

    private static async Task<WaitlistRecord> ToRecord(AppDb db, WaitlistEntry entry, CancellationToken cancellationToken)
    {
        var active = entry.Status is WaitlistEntryStatus.Waiting or WaitlistEntryStatus.Offered;
        string? area = null;
        if (entry.Status == WaitlistEntryStatus.Offered && entry.ReleaseId is not null)
            area = await db.Set<ParkingRelease>().Where(release => release.Id == entry.ReleaseId).Select(release => release.Area).SingleOrDefaultAsync(cancellationToken);
        return new WaitlistRecord(
            entry.Date,
            active ? await WaitlistAllocator.PositionOf(db, entry, cancellationToken) : null,
            entry.Status,
            entry.Status == WaitlistEntryStatus.Offered ? entry.HeldUntil : null,
            area);
    }

    private static bool TryGetEmployeeId(HttpContext context, out int employeeId) =>
        int.TryParse(context.User.FindFirst("employeeId")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out employeeId);

    private static IResult Fail(int status, string code, string message) => Results.Json(new Error(message, code), statusCode: status);
    private static IResult Unauthorized() => Fail(StatusCodes.Status401Unauthorized, "UNAUTHORIZED", "Sign in to continue.");
    private static IResult Conflict(string message) => Fail(StatusCodes.Status409Conflict, "CONFLICT", message);
}
