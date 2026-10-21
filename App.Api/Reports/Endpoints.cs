using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace App.Api;

internal sealed class ReportEndpoints : IApiEndpointModule
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/admin/entry-records", GetEntryRecordsAsync)
            .WithName("listEntryRecords")
            .RequireAuthorization("Administrator")
            .Produces<VerificationList>(StatusCodes.Status200OK)
            .Produces<Error>(StatusCodes.Status401Unauthorized)
            .Produces<Error>(StatusCodes.Status403Forbidden);
        endpoints.MapGet("/admin/reports", GetReportAsync)
            .WithName("getParkingReport")
            .RequireAuthorization("Administrator")
            .Produces<Report>(StatusCodes.Status200OK)
            .Produces<Error>(StatusCodes.Status401Unauthorized)
            .Produces<Error>(StatusCodes.Status403Forbidden);
        endpoints.MapGet("/admin/reports/unused", GetUnusedParkingAsync)
            .WithName("listUnusedParking")
            .RequireAuthorization("Administrator")
            .Produces<UnusedParkingList>(StatusCodes.Status200OK)
            .Produces<Error>(StatusCodes.Status401Unauthorized)
            .Produces<Error>(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> GetEntryRecordsAsync(
        AppDb db,
        string? result,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken cancellationToken)
    {
        var query = db.Set<VerificationRecord>().AsNoTracking();
        if (result is not null)
        {
            if (VerificationMapping.TryParseResult(result, out var parsedResult))
            {
                var storedResult = VerificationMapping.ToStoredResult(parsedResult);
                query = query.Where(record => record.Result == storedResult);
            }
            else
            {
                query = query.Where(_ => false);
            }
        }
        if (dateFrom.HasValue) query = query.Where(record => record.Date >= dateFrom.Value);
        if (dateTo.HasValue) query = query.Where(record => record.Date <= dateTo.Value);

        var total = await query.CountAsync(cancellationToken);
        var records = await query
            .OrderByDescending(record => record.Date)
            .ThenByDescending(record => record.Time)
            .ThenByDescending(record => record.Id)
            .ToListAsync(cancellationToken);
        return Results.Ok(new VerificationList(records.Select(VerificationMapping.ToVerification).ToArray(), total));
    }

    private static async Task<IResult> GetReportAsync(
        ReportPeriod period,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        AppDb db,
        CancellationToken cancellationToken)
    {
        var releases = await db.Set<ParkingRelease>()
            .AsNoTracking()
            .Where(release => !release.IsCancelled
                && (release.Status == ParkingReleaseStatus.Released || release.Status == ParkingReleaseStatus.Claimed))
            .Where(release => !dateFrom.HasValue || release.Date >= dateFrom.Value)
            .Where(release => !dateTo.HasValue || release.Date <= dateTo.Value)
            .Select(release => release.Date)
            .ToListAsync(cancellationToken);
        var claims = await db.Set<ParkingClaimEntity>()
            .AsNoTracking()
            .Where(claim => !dateFrom.HasValue || claim.Date >= dateFrom.Value)
            .Where(claim => !dateTo.HasValue || claim.Date <= dateTo.Value)
            .Select(claim => claim.Date)
            .ToListAsync(cancellationToken);

        var releaseCounts = releases.GroupBy(date => date).ToDictionary(group => group.Key, group => group.Count());
        var claimCounts = claims.GroupBy(date => date).ToDictionary(group => group.Key, group => group.Count());
        var dailyValues = releaseCounts.Keys.Concat(claimCounts.Keys).Distinct().OrderBy(date => date)
            .Select(date => (
                Date: date,
                SpacesReleased: releaseCounts.GetValueOrDefault(date),
                TemporaryClaims: claimCounts.GetValueOrDefault(date)))
            .ToArray();
        var weeklyValues = dailyValues
            .GroupBy(activity => WeekStart(activity.Date))
            .OrderBy(group => group.Key)
            .Select(group => (
                Date: group.Key,
                SpacesReleased: group.Sum(activity => activity.SpacesReleased),
                TemporaryClaims: group.Sum(activity => activity.TemporaryClaims)))
            .ToArray();

        var rows = period == ReportPeriod.Weekly
            ? weeklyValues.Select(ToReportRow).ToArray()
            : dailyValues.Select(ToReportRow).ToArray();
        return Results.Ok(new Report(
            period,
            rows,
            ToStatistics(dailyValues),
            ToStatistics(weeklyValues)));
    }

    private static async Task<IResult> GetUnusedParkingAsync(
        AppDb db,
        ParkingStatusFilter? category,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        CancellationToken cancellationToken)
    {
        var releases = await db.Set<ParkingRelease>()
            .AsNoTracking()
            .Include(release => release.Employee)
            .Where(release => !release.IsCancelled && release.Status == ParkingReleaseStatus.Released)
            .Where(release => !dateFrom.HasValue || release.Date >= dateFrom.Value)
            .Where(release => !dateTo.HasValue || release.Date <= dateTo.Value)
            .Where(release => category == null
                || (category == ParkingStatusFilter.Dedicated && release.Employee.ParkingStatus == EmployeeParkingStatus.Dedicated)
                || (category == ParkingStatusFilter.Temporary && release.Employee.ParkingStatus == EmployeeParkingStatus.Temporary))
            .OrderBy(release => release.Date)
            .ThenBy(release => release.Id)
            .ToListAsync(cancellationToken);

        var releaseIds = releases.Select(release => release.Id).ToArray();
        var claimedReleaseIds = (await db.Set<ParkingClaimEntity>()
                .AsNoTracking()
                .Where(claim => claim.ReleaseId.HasValue && releaseIds.Contains(claim.ReleaseId.Value))
                .Select(claim => claim.ReleaseId!.Value)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var employeeIds = releases.Select(release => release.EmployeeId).Distinct().ToArray();
        var vehiclesByEmployee = (await db.Set<VehicleEntity>()
                .AsNoTracking()
                .Where(vehicle => employeeIds.Contains(vehicle.EmployeeId))
                .OrderBy(vehicle => vehicle.Id)
                .ToListAsync(cancellationToken))
            .GroupBy(vehicle => vehicle.EmployeeId)
            .ToDictionary(group => group.Key, group => group.First());

        var records = releases
            .Where(release => !claimedReleaseIds.Contains(release.Id)
                && vehiclesByEmployee.ContainsKey(release.EmployeeId))
            .Select(release =>
            {
                var vehicle = vehiclesByEmployee[release.EmployeeId];
                var description = string.Join(" ", new[] { vehicle.Make, vehicle.Model, vehicle.Color, vehicle.Plate }
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
                var parkingCategory = release.Employee.ParkingStatus == EmployeeParkingStatus.Dedicated
                    ? UnusedParkingCategory.Dedicated
                    : UnusedParkingCategory.Temporary;
                return new UnusedParkingRecord(release.Id, parkingCategory, release.Employee.Name, description, release.Date);
            })
            .ToArray();

        return Results.Ok(new UnusedParkingList(records, records.Length));
    }

    private static ReportRow ToReportRow((DateOnly Date, int SpacesReleased, int TemporaryClaims) activity) => new(
        activity.Date,
        $"Week of {WeekStart(activity.Date).ToString("d MMM", CultureInfo.InvariantCulture)}",
        activity.SpacesReleased,
        activity.TemporaryClaims,
        CalculateUtilization(activity.SpacesReleased, activity.TemporaryClaims));

    private static ReportStatistics ToStatistics(IEnumerable<(DateOnly Date, int SpacesReleased, int TemporaryClaims)> activities)
    {
        var releasedSpaces = activities.Sum(activity => activity.SpacesReleased);
        var temporaryClaims = activities.Sum(activity => activity.TemporaryClaims);
        return new ReportStatistics(releasedSpaces, temporaryClaims, CalculateUtilization(releasedSpaces, temporaryClaims));
    }

    private static float CalculateUtilization(int releasedSpaces, int temporaryClaims) =>
        releasedSpaces == 0 ? 0 : (float)temporaryClaims / releasedSpaces * 100;

    private static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
