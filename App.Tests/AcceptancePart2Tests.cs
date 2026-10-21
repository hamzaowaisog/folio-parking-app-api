using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace App.Tests.Part2;

public sealed class AcceptancePart2Tests : IClassFixture<AppFactory>
{
    private static readonly DateOnly OfficeDate = new(2025, 10, 14);
    private static readonly DateTime FixedUtc = new(2025, 10, 14, 12, 0, 0, DateTimeKind.Utc);
    private static int _nextEmployeeId = 20_000;
    private readonly AppFactory _factory;

    public AcceptancePart2Tests(AppFactory factory) => _factory = factory;

    [Fact]
    public async Task AC_7_1_DashboardContainsParkingDataAndDocumentedFields()
    {
        using var app = CreateApi();
        var administrator = await AddEmployeeAsync("Dashboard Administrator", "Temporary", isAdministrator: true);
        var employee = await AddEmployeeAsync("Dashboard Employee", "Dedicated", "Floor 2");
        await AddReleaseAsync(employee, OfficeDate, "Released", "Floor 2", FixedUtc.AddHours(-1));

        using var response = await SendAsync(app.Client, HttpMethod.Get, "/parking/dashboard", "employee", employee, administrator);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        AssertProperties(body, "employee", "officeDate", "releases", "availability", "currentClaim", "notice", "unavailableFields");
        Assert.Equal(OfficeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), body.GetProperty("officeDate").GetString());
        var dashboardEmployee = body.GetProperty("employee");
        AssertProperties(dashboardEmployee, "id", "name", "email", "parkingStatus");
        Assert.Equal(employee.Id, dashboardEmployee.GetProperty("id").GetInt32());
        Assert.Equal(employee.Name, dashboardEmployee.GetProperty("name").GetString());
        Assert.Equal(employee.Email, dashboardEmployee.GetProperty("email").GetString());
        Assert.Equal("dedicated", dashboardEmployee.GetProperty("parkingStatus").GetString());

        var releases = body.GetProperty("releases").EnumerateArray().ToArray();
        var release = Assert.Single(releases);
        AssertReleaseRecord(release, OfficeDate, employee.Name, "Floor 2", "Released");

        var availability = body.GetProperty("availability");
        AssertProperties(availability, "date", "count", "areas", "notYetClaimable");
        Assert.Equal(OfficeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), availability.GetProperty("date").GetString());
        Assert.Equal(JsonValueKind.Array, body.GetProperty("unavailableFields").ValueKind);
        var notice = body.GetProperty("notice");
        if (notice.ValueKind != JsonValueKind.Null)
        {
            AssertProperties(notice, "message", "email");
            Assert.False(string.IsNullOrWhiteSpace(notice.GetProperty("message").GetString()));
            Assert.Equal(employee.Email, notice.GetProperty("email").GetString());
        }
    }

    [Fact]
    public async Task AC_7_2_TodaysReleaseReturnsDocumentedReleaseRecord()
    {
        using var app = CreateApi();
        var administrator = await AddEmployeeAsync("Today Administrator", "Temporary", isAdministrator: true);
        var employee = await AddEmployeeAsync("Today Release Employee", "Dedicated", "Floor 3");

        using var response = await SendAsync(app.Client, HttpMethod.Post, "/parking/releases/today", "employee", employee, administrator,
            new { officeDate = OfficeDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var release = await ReadJsonAsync(response);
        AssertReleaseRecord(release, OfficeDate, employee.Name, "Floor 3", "Released");
    }

    [Fact]
    public async Task AC_7_3_ReleaseListIsScopedAndFilteredByDateRange()
    {
        using var app = CreateApi();
        var administrator = await AddEmployeeAsync("List Administrator", "Temporary", isAdministrator: true);
        var employee = await AddEmployeeAsync("List Owner", "Dedicated", "Floor 1");
        var otherEmployee = await AddEmployeeAsync("Other List Owner", "Dedicated", "Floor 3");
        var dates = new[] { new DateOnly(2025, 10, 16), new DateOnly(2025, 10, 17), new DateOnly(2025, 10, 18) };
        foreach (var date in dates)
            await AddReleaseAsync(employee, date, "Reserved", "Floor 1", At(date, 8));
        await AddReleaseAsync(otherEmployee, dates[0], "Reserved", "Floor 3", At(dates[0], 8));

        var path = $"/parking/releases?dateFrom={FormatDate(dates[0])}&dateTo={FormatDate(dates[1])}";
        using var response = await SendAsync(app.Client, HttpMethod.Get, path, "employee", employee, administrator);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        AssertProperties(body, "releases");
        var releases = body.GetProperty("releases").EnumerateArray().ToArray();
        Assert.Equal(2, releases.Length);
        Assert.Equal(new[] { FormatDate(dates[0]), FormatDate(dates[1]) },
            releases.Select(item => item.GetProperty("date").GetString()).OrderBy(value => value, StringComparer.Ordinal).ToArray());
        foreach (var release in releases)
            AssertReleaseRecord(release, DateOnly.Parse(release.GetProperty("date").GetString()!, CultureInfo.InvariantCulture), employee.Name, "Floor 1", "Reserved");
    }

    [Fact]
    public async Task AC_7_4_ScheduleCreatesDocumentedReleaseRecords()
    {
        using var app = CreateApi();
        var administrator = await AddEmployeeAsync("Schedule Administrator", "Temporary", isAdministrator: true);
        var employee = await AddEmployeeAsync("Schedule Owner", "Dedicated", "Floor 2");
        var dates = new[] { new DateOnly(2025, 10, 20), new DateOnly(2025, 10, 21), new DateOnly(2025, 10, 22) };

        using var response = await SendAsync(app.Client, HttpMethod.Post, "/parking/releases", "employee", employee, administrator,
            new { dates = dates.Select(FormatDate).ToArray() });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        AssertProperties(body, "releases");
        var releases = body.GetProperty("releases").EnumerateArray().ToArray();
        Assert.Equal(dates.Length, releases.Length);
        Assert.Equal(dates.Select(FormatDate).OrderBy(value => value, StringComparer.Ordinal),
            releases.Select(item => item.GetProperty("date").GetString()).OrderBy(value => value, StringComparer.Ordinal));
        foreach (var release in releases)
            AssertReleaseRecord(release, DateOnly.Parse(release.GetProperty("date").GetString()!, CultureInfo.InvariantCulture), employee.Name, "Floor 2", "Reserved");
    }

    [Fact]
    public async Task AC_7_5_CancelReturnsMessageAndTheIdentifiedRelease()
    {
        using var app = CreateApi();
        var administrator = await AddEmployeeAsync("Cancel Administrator", "Temporary", isAdministrator: true);
        var employee = await AddEmployeeAsync("Cancellation Owner", "Dedicated", "Floor 1");
        var date = new DateOnly(2025, 10, 23);
        var releaseId = await AddReleaseAsync(employee, date, "Reserved", "Floor 1", At(date, 8));

        using var response = await SendAsync(app.Client, HttpMethod.Post, $"/parking/releases/{releaseId}/cancel", "employee", employee, administrator,
            new { reason = "Employee requested this cancellation." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        AssertProperties(body, "message", "release");
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
        var release = body.GetProperty("release");
        AssertReleaseRecord(release, date, employee.Name, "Floor 1");
        Assert.Equal(releaseId, release.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task AC_7_6_CancelReturnsNotFoundForUnknownRelease()
    {
        using var app = CreateApi();
        var administrator = await AddEmployeeAsync("Missing Release Administrator", "Temporary", isAdministrator: true);
        var employee = await AddEmployeeAsync("Missing Release Caller", "Temporary");

        using var response = await SendAsync(app.Client, HttpMethod.Post, "/parking/releases/2140000000/cancel", "employee", employee, administrator,
            new { reason = "No release exists for this ID." });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("NOT_FOUND", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC_7_7_AvailabilityIncludesDateCountAreasAndScheduledSpaces()
    {
        using var app = CreateApi();
        var administrator = await AddEmployeeAsync("Availability Administrator", "Temporary", isAdministrator: true);
        var caller = await AddEmployeeAsync("Availability Caller", "Temporary");
        var date = new DateOnly(2025, 12, 1);
        var areas = new[] { "Floor 1", "Floor 2", "Floor 3" };
        foreach (var area in areas)
        {
            var owner = await AddEmployeeAsync($"Availability {area} Owner", "Dedicated", area);
            await AddReleaseAsync(owner, date, "Reserved", area, At(date, 8));
        }

        using var response = await SendAsync(app.Client, HttpMethod.Get, $"/parking/availability?date={FormatDate(date)}", "employee", caller, administrator);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        AssertProperties(body, "date", "count", "areas", "notYetClaimable");
        Assert.Equal(FormatDate(date), body.GetProperty("date").GetString());
        Assert.Equal(0, body.GetProperty("count").GetInt32());
        var scheduled = body.GetProperty("notYetClaimable").EnumerateArray().ToArray();
        Assert.Equal(areas.Length, scheduled.Length);
        Assert.Equal(areas.OrderBy(value => value, StringComparer.Ordinal),
            scheduled.Select(item => item.GetProperty("area").GetString()).OrderBy(value => value, StringComparer.Ordinal));
        foreach (var item in scheduled)
        {
            AssertProperties(item, "area", "count", "claimableAt");
            Assert.Equal(1, item.GetProperty("count").GetInt32());
            Assert.True(DateTimeOffset.TryParse(item.GetProperty("claimableAt").GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out _));
        }
    }

    [Fact]
    public async Task AC_7_8_ClaimCreatesDocumentedClaimForRegisteredPlate()
    {
        var claimInstant = new DateTime(2026, 2, 10, 12, 0, 0, DateTimeKind.Utc);
        var claimDate = DateOnly.FromDateTime(claimInstant);
        using var app = CreateApi(fixedUtc: claimInstant);
        var administrator = await AddEmployeeAsync("Claim Administrator", "Temporary", isAdministrator: true);
        var releaser = await AddEmployeeAsync("Claim Space Owner", "Dedicated", "Floor 3");
        await AddReleaseAsync(releaser, claimDate, "Released", "Floor 3", claimInstant.AddHours(-1));
        var claimant = await AddEmployeeAsync("Claimant", "Temporary");
        var plate = $"P2-{claimant.Id}-CAR";
        await AddVehicleAsync(claimant, plate, "Honda", "Civic", "Blue");

        using var response = await SendAsync(app.Client, HttpMethod.Post, "/parking/claims", "employee", claimant, administrator,
            new { date = FormatDate(claimDate), plate });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var claim = await ReadJsonAsync(response);
        AssertProperties(claim, "id", "date", "plate", "area", "status", "createdBy", "createdAt", "lastChangedBy", "lastChangedAt");
        Assert.True(claim.GetProperty("id").GetInt32() > 0);
        Assert.Equal(FormatDate(claimDate), claim.GetProperty("date").GetString());
        Assert.Equal(plate, claim.GetProperty("plate").GetString());
        Assert.Equal("Floor 3", claim.GetProperty("area").GetString());
        Assert.Equal("Claimed", claim.GetProperty("status").GetString());
        Assert.Equal(claimant.Name, claim.GetProperty("createdBy").GetString());
        Assert.Equal(claimant.Name, claim.GetProperty("lastChangedBy").GetString());
        AssertTimestamp(claim.GetProperty("createdAt"));
        AssertTimestamp(claim.GetProperty("lastChangedAt"));
    }

    [Fact]
    public async Task AC_7_9_ClaimWithoutAvailableSpaceReturnsConflict()
    {
        using var app = CreateApi();
        var administrator = await AddEmployeeAsync("No Space Administrator", "Temporary", isAdministrator: true);
        var claimant = await AddEmployeeAsync("No Space Claimant", "Temporary");
        var plate = $"P2-{claimant.Id}-CAR";
        await AddVehicleAsync(claimant, plate, "Toyota", "Corolla", "Grey");

        using var response = await SendAsync(app.Client, HttpMethod.Post, "/parking/claims", "employee", claimant, administrator,
            new { date = "2025-11-04", plate });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("CONFLICT", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC_7_10_MalformedReleaseScheduleReturnsBadRequest()
    {
        using var app = CreateApi();
        var administrator = await AddEmployeeAsync("Malformed Schedule Administrator", "Temporary", isAdministrator: true);
        var employee = await AddEmployeeAsync("Malformed Schedule Caller", "Dedicated", "Floor 2");

        using var response = await SendAsync(app.Client, HttpMethod.Post, "/parking/releases", "employee", employee, administrator, new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("BAD_REQUEST", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC_8_1_LiveGateFeedReturnsDocumentedEventsWithinLimit()
    {
        using var app = CreateApi();
        var administrator = await AddEmployeeAsync("Gate Feed Administrator", "Temporary", isAdministrator: true);
        var gateGuard = await AddEmployeeAsync("Gate Guard", "Temporary", accountRole: "gateGuard");
        var eventRows = new[]
        {
            ("P2-GATE-A", "Driver A", new TimeOnly(8, 10), "Denied", "off", false),
            ("P2-GATE-B", "Driver B", new TimeOnly(8, 20), "Authorised", "green", true),
            ("P2-GATE-C", "Driver C", new TimeOnly(8, 30), "Existing process", "not used", true)
        };
        foreach (var row in eventRows)
            await AddVerificationAsync(row.Item1, row.Item2, OfficeDate, row.Item3, row.Item4, row.Item5, $"Reason for {row.Item1}", row.Item6);

        using var response = await SendAsync(app.Client, HttpMethod.Get, "/gate/events/live?limit=2", "GateGuard", gateGuard, administrator);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        AssertProperties(body, "cameraStatus", "events");
        Assert.Contains(body.GetProperty("cameraStatus").GetString(), new[] { "online", "unavailable" });
        var events = body.GetProperty("events").EnumerateArray().ToArray();
        Assert.InRange(events.Length, 0, 2);
        Assert.All(events, item => AssertVerification(item));
        Assert.All(events, item => Assert.Contains(item.GetProperty("plate").GetString(), eventRows.Select(row => row.Item1)));
    }

    [Fact]
    public async Task AC_8_2_UnavailableGateEventServiceReturnsServiceUnavailable()
    {
        using var app = CreateApi(makeGateServiceUnavailable: true);
        var administrator = await AddEmployeeAsync("Unavailable Gate Administrator", "Temporary", isAdministrator: true);
        var gateGuard = await AddEmployeeAsync("Unavailable Gate Guard", "Temporary", accountRole: "gateGuard");

        using var response = await SendAsync(app.Client, HttpMethod.Get, "/gate/events/live", "GateGuard", gateGuard, administrator);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.Equal("SERVICE_UNAVAILABLE", body.GetProperty("code").GetString());
        Assert.True(app.GateServiceCallCount > 0, "The unavailable gate-event stand-in should receive the request.");
    }

    [Fact]
    public async Task AC_8_3_EntryRecordsMatchResultAndDateFilters()
    {
        using var app = CreateApi();
        var administrator = await AddEmployeeAsync("Entry Records Administrator", "Temporary", isAdministrator: true);
        await AddVerificationAsync("P2-ENTRY-A", "Driver A", new DateOnly(2025, 10, 2), new TimeOnly(8, 1), "Denied", "off", "No valid authorisation", false);
        await AddVerificationAsync("P2-ENTRY-B", "Driver B", new DateOnly(2025, 10, 3), new TimeOnly(8, 2), "Denied", "off", "No valid authorisation", false);
        await AddVerificationAsync("P2-ENTRY-C", "Driver C", new DateOnly(2025, 10, 3), new TimeOnly(8, 3), "Authorised", "green", "Valid parking", true);
        await AddVerificationAsync("P2-ENTRY-OUTSIDE", "Driver D", new DateOnly(2025, 10, 4), new TimeOnly(8, 4), "Denied", "off", "No valid authorisation", false);

        using var response = await SendAsync(app.Client, HttpMethod.Get,
            "/admin/entry-records?result=Denied&dateFrom=2025-10-02&dateTo=2025-10-03", "Administrator", administrator, administrator);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadJsonAsync(response);
        AssertProperties(body, "records", "total");
        Assert.Equal(2, body.GetProperty("total").GetInt32());
        var records = body.GetProperty("records").EnumerateArray().ToArray();
        Assert.Equal(2, records.Length);
        Assert.Equal(new[] { "P2-ENTRY-A", "P2-ENTRY-B" },
            records.Select(item => item.GetProperty("plate").GetString()).OrderBy(value => value, StringComparer.Ordinal));
        foreach (var record in records)
        {
            AssertVerification(record);
            Assert.Equal("Denied", record.GetProperty("result").GetString());
            Assert.Contains(record.GetProperty("date").GetString(), new[] { "2025-10-02", "2025-10-03" });
        }
    }

    [Fact]
    public async Task AC_8_4_ReportContainsDailyAndWeeklyStatisticsForStoredActivity()
    {
        using var app = CreateApi();
        var administrator = await AddEmployeeAsync("Report Administrator", "Temporary", isAdministrator: true);
        var dates = new[] { new DateOnly(2026, 1, 20), new DateOnly(2026, 1, 21), new DateOnly(2026, 1, 22) };
        var releases = new List<(TestEmployee Owner, DateOnly Date, int ReleaseId)>();
        foreach (var date in dates)
        {
            var owner = await AddEmployeeAsync($"Report Space Owner {FormatDate(date)}", "Dedicated", "Floor 2");
            var releaseId = await AddReleaseAsync(owner, date, "Released", "Floor 2", At(date, 8));
            releases.Add((owner, date, releaseId));
        }
        for (var index = 0; index < 2; index++)
        {
            var release = releases[index];
            var claimant = await AddEmployeeAsync($"Report Claimant {index + 1}", "Temporary");
            var plate = $"P2-REPORT-{claimant.Id}";
            await AddVehicleAsync(claimant, plate, "Honda", "City", "White");
            await AddClaimAsync(claimant, release.ReleaseId, release.Date, plate, "Floor 2", FixedUtc);
        }

        foreach (var period in new[] { "daily", "weekly" })
        {
            using var response = await SendAsync(app.Client, HttpMethod.Get,
                $"/admin/reports?period={period}&dateFrom={FormatDate(dates[0])}&dateTo={FormatDate(dates[2])}",
                "Administrator", administrator, administrator);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await ReadJsonAsync(response);
            AssertProperties(body, "period", "rows", "dailyStatistics", "weeklyStatistics");
            Assert.Equal(period, body.GetProperty("period").GetString());
            var rows = body.GetProperty("rows").EnumerateArray().ToArray();
            if (period == "daily")
            {
                Assert.Equal(3, rows.Length);
                Assert.Equal(dates.Select(FormatDate).OrderBy(value => value, StringComparer.Ordinal),
                    rows.Select(item => item.GetProperty("date").GetString()).OrderBy(value => value, StringComparer.Ordinal));
            }
            Assert.All(rows, row =>
            {
                AssertProperties(row, "date", "week", "spacesReleased", "temporaryClaims", "utilizationPercent");
                Assert.True(row.GetProperty("spacesReleased").GetInt32() >= 0);
                Assert.True(row.GetProperty("temporaryClaims").GetInt32() >= 0);
                Assert.True(row.GetProperty("utilizationPercent").ValueKind == JsonValueKind.Number);
            });
            AssertReportStatistics(body.GetProperty("dailyStatistics"));
            AssertReportStatistics(body.GetProperty("weeklyStatistics"));
            Assert.Equal(3, body.GetProperty("dailyStatistics").GetProperty("releasedSpaces").GetInt32());
            Assert.Equal(2, body.GetProperty("dailyStatistics").GetProperty("temporaryClaims").GetInt32());
            Assert.Equal(3, body.GetProperty("weeklyStatistics").GetProperty("releasedSpaces").GetInt32());
            Assert.Equal(2, body.GetProperty("weeklyStatistics").GetProperty("temporaryClaims").GetInt32());
        }
    }

    [Fact]
    public async Task AC_8_5_UnusedParkingRecordsMatchCategoryAndDateFilters()
    {
        using var app = CreateApi();
        var administrator = await AddEmployeeAsync("Unused Records Administrator", "Temporary", isAdministrator: true);
        var dates = new[] { new DateOnly(2025, 10, 10), new DateOnly(2025, 10, 11), new DateOnly(2025, 10, 12) };
        var expectedEmployees = new HashSet<string>(StringComparer.Ordinal);
        foreach (var date in dates)
        {
            var dedicated = await AddEmployeeAsync($"Unused Dedicated {FormatDate(date)}", "Dedicated", "Floor 1");
            await AddVehicleAsync(dedicated, $"P2-DED-{dedicated.Id}", "Suzuki", "Swift", "Red");
            await AddReleaseAsync(dedicated, date, "Released", "Floor 1", At(date, 8));
            expectedEmployees.Add(dedicated.Name);

            var temporary = await AddEmployeeAsync($"Unused Temporary {FormatDate(date)}", "Temporary");
            await AddVehicleAsync(temporary, $"P2-TMP-{temporary.Id}", "Toyota", "Yaris", "Grey");
            await AddReleaseAsync(temporary, date, "Released", "Floor 2", At(date, 8));
            expectedEmployees.Add(temporary.Name);
        }

        using var allResponse = await SendAsync(app.Client, HttpMethod.Get,
            $"/admin/reports/unused?dateFrom={FormatDate(dates[0])}&dateTo={FormatDate(dates[2])}",
            "Administrator", administrator, administrator);

        Assert.Equal(HttpStatusCode.OK, allResponse.StatusCode);
        var allBody = await ReadJsonAsync(allResponse);
        AssertProperties(allBody, "records", "total");
        Assert.Equal(6, allBody.GetProperty("total").GetInt32());
        var allRecords = allBody.GetProperty("records").EnumerateArray().ToArray();
        Assert.Equal(6, allRecords.Length);
        Assert.Equal(expectedEmployees.OrderBy(value => value, StringComparer.Ordinal),
            allRecords.Select(item => item.GetProperty("employee").GetString()).OrderBy(value => value, StringComparer.Ordinal));
        Assert.Contains(allRecords, item => item.GetProperty("category").GetString() == "Unused Dedicated Parking");
        Assert.Contains(allRecords, item => item.GetProperty("category").GetString() == "Unused Temporary Parking");
        Assert.All(allRecords, record =>
        {
            AssertUnusedRecord(record);
            Assert.Contains(record.GetProperty("date").GetString(), dates.Select(FormatDate));
            Assert.False(string.IsNullOrWhiteSpace(record.GetProperty("vehicle").GetString()));
        });

        using var filteredResponse = await SendAsync(app.Client, HttpMethod.Get,
            $"/admin/reports/unused?category=Temporary&dateFrom={FormatDate(dates[1])}&dateTo={FormatDate(dates[1])}",
            "Administrator", administrator, administrator);

        Assert.Equal(HttpStatusCode.OK, filteredResponse.StatusCode);
        var filteredBody = await ReadJsonAsync(filteredResponse);
        AssertProperties(filteredBody, "records", "total");
        Assert.Equal(1, filteredBody.GetProperty("total").GetInt32());
        var filtered = Assert.Single(filteredBody.GetProperty("records").EnumerateArray().ToArray());
        AssertUnusedRecord(filtered);
        Assert.Equal("Unused Temporary Parking", filtered.GetProperty("category").GetString());
        Assert.Equal(FormatDate(dates[1]), filtered.GetProperty("date").GetString());
        Assert.Contains("Unused Temporary 2025-10-11", filtered.GetProperty("employee").GetString());
    }

    private TestApp CreateApi(bool makeGateServiceUnavailable = false, DateTime? fixedUtc = null)
    {
        var restarted = _factory.Restarted();
        var gateFakes = new List<UnavailableGateServiceProxy>();
        var configured = restarted.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(new DateTimeOffset(fixedUtc ?? FixedUtc)));
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = Part2AuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = Part2AuthenticationHandler.SchemeName;
                    options.DefaultForbidScheme = Part2AuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, Part2AuthenticationHandler>(Part2AuthenticationHandler.SchemeName, _ => { });
            services.PostConfigure<AuthorizationOptions>(options =>
            {
                if (options.GetPolicy("Administrator") is null)
                    options.AddPolicy("Administrator", policy => policy.RequireRole("Administrator", "administrator"));
                if (options.GetPolicy("GateOrAdmin") is null)
                    options.AddPolicy("GateOrAdmin", policy => policy.RequireRole("GateGuard", "gateGuard", "Administrator", "administrator"));
            });

            if (makeGateServiceUnavailable)
            {
                var gateInterfaces = typeof(Program).Assembly.GetTypes()
                    .Where(type => type.IsInterface && type.Name.Contains("Gate", StringComparison.OrdinalIgnoreCase)
                        && (type.Name.Contains("Event", StringComparison.OrdinalIgnoreCase)
                            || type.Name.Contains("Camera", StringComparison.OrdinalIgnoreCase)
                            || type.Name.Contains("Service", StringComparison.OrdinalIgnoreCase)))
                    .ToArray();
                foreach (var serviceType in gateInterfaces)
                {
                    services.RemoveAll(serviceType);
                    var fake = (UnavailableGateServiceProxy)DispatchProxy.Create(serviceType, typeof(UnavailableGateServiceProxy));
                    gateFakes.Add(fake);
                    services.Add(ServiceDescriptor.Singleton(serviceType, fake));
                }
            }
        }));
        return new TestApp(restarted, configured, configured.CreateClient(), gateFakes);
    }

    private async Task<TestEmployee> AddEmployeeAsync(string name, string parkingStatus, string? assignedFloor = null,
        string accountRole = "employee", bool isAdministrator = false)
    {
        var externalId = Interlocked.Increment(ref _nextEmployeeId);
        var email = $"part2-{externalId}@example.test";
        var employeeId = await ExecuteScalarAsync(
            """
            INSERT INTO "Employees" ("EmployeeId", "Name", "Email", "Department", "Role", "ParkingStatus", "AssignedFloor")
            VALUES (@employeeId, @name, @email, 'Testing', 'Parking Test User', @parkingStatus, @assignedFloor)
            RETURNING "Id";
            """,
            ("employeeId", $"PART2-{externalId}"), ("name", name), ("email", email), ("parkingStatus", parkingStatus),
            ("assignedFloor", (object?)assignedFloor ?? DBNull.Value));
        var accountId = await ExecuteScalarAsync(
            """
            INSERT INTO "Accounts" ("EmployeeId", "Email", "PasswordHash", "Role", "CreatedAt")
            VALUES (@employeeId, @email, 'part2-test-hash', @role, @createdAt)
            RETURNING "Id";
            """,
            ("employeeId", employeeId), ("email", email), ("role", accountRole), ("createdAt", FixedUtc));
        var employee = new TestEmployee(employeeId, accountId, name, email, parkingStatus, assignedFloor);
        if (isAdministrator)
        {
            await ExecuteSqlAsync(
                """
                INSERT INTO "AdministratorAccess" ("EmployeeId", "AccessStatus", "ChangedByEmployeeId", "ChangedAt")
                VALUES (@employeeId, 'active', @employeeId, @changedAt);
                """,
                ("employeeId", employeeId), ("changedAt", FixedUtc));
        }
        return employee;
    }

    private Task AddVehicleAsync(TestEmployee employee, string plate, string make, string model, string color) =>
        ExecuteSqlAsync(
            """
            INSERT INTO "Vehicles" ("EmployeeId", "Plate", "Make", "Model", "Color")
            VALUES (@employeeId, @plate, @make, @model, @color);
            """,
            ("employeeId", employee.Id), ("plate", plate), ("make", make), ("model", model), ("color", color));

    private Task<int> AddReleaseAsync(TestEmployee employee, DateOnly date, string status, string area, DateTime claimableAt) =>
        ExecuteScalarAsync(
            """
            INSERT INTO "ParkingReleases" ("EmployeeId", "Date", "Status", "Area", "CreatedByEmployeeId", "CreatedAt",
                "LastChangedByEmployeeId", "LastChangedAt", "ClaimableAt", "IsCancelled", "CancellationReason")
            VALUES (@employeeId, @date, @status, @area, @changedBy, @createdAt, @changedBy, @changedAt, @claimableAt, false, NULL)
            RETURNING "Id";
            """,
            ("employeeId", employee.Id), ("date", date), ("status", status), ("area", area), ("changedBy", employee.Id),
            ("createdAt", FixedUtc), ("changedAt", FixedUtc), ("claimableAt", claimableAt));

    private Task<int> AddClaimAsync(TestEmployee claimant, int releaseId, DateOnly date, string plate, string area, DateTime createdAt) =>
        ExecuteScalarAsync(
            """
            INSERT INTO "ParkingClaims" ("EmployeeId", "ReleaseId", "Date", "VehiclePlate", "Area", "Status",
                "CreatedByEmployeeId", "CreatedAt", "LastChangedByEmployeeId", "LastChangedAt")
            VALUES (@employeeId, @releaseId, @date, @plate, @area, 'Claimed', @changedBy, @createdAt, @changedBy, @createdAt)
            RETURNING "Id";
            """,
            ("employeeId", claimant.Id), ("releaseId", releaseId), ("date", date), ("plate", plate), ("area", area),
            ("changedBy", claimant.Id), ("createdAt", createdAt));

    private Task AddVerificationAsync(string plate, string driver, DateOnly date, TimeOnly time, string result,
        string indicatorState, string reason, bool permanentSticker) =>
        ExecuteSqlAsync(
            """
            INSERT INTO "VerificationRecords" ("Plate", "Driver", "Date", "Time", "Result", "EntryStatus",
                "IndicatorState", "Reason", "PermanentSticker")
            VALUES (@plate, @driver, @date, @time, @result, @result, @indicatorState, @reason, @permanentSticker);
            """,
            ("plate", plate), ("driver", driver), ("date", date), ("time", time), ("result", result),
            ("indicatorState", indicatorState), ("reason", reason), ("permanentSticker", permanentSticker));

    private async Task ExecuteSqlAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            if (value is DBNull)
                command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Text) { Value = DBNull.Value });
            else
                command.Parameters.AddWithValue(name, value);
        }
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> ExecuteScalarAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            if (value is DBNull)
                command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Text) { Value = DBNull.Value });
            else
                command.Parameters.AddWithValue(name, value);
        }
        return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path,
        string role, TestEmployee caller, TestEmployee administrator, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-Employee-Id", caller.Id.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add("X-Test-Account-Id", caller.AccountId.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add("X-Test-Role", role);
        request.Headers.Add("X-Test-Employee-Name", caller.Name);
        request.Headers.Add("X-Test-Employee-Email", caller.Email);
        request.Headers.Add("X-Test-Administrator-Id", administrator.Id.ToString(CultureInfo.InvariantCulture));
        if (body is not null)
            request.Content = System.Net.Http.Json.JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static void AssertReleaseRecord(JsonElement release, DateOnly date, string employeeName, string? area, string? status = null)
    {
        AssertProperties(release, "id", "date", "status", "area", "createdBy", "createdAt", "lastChangedBy", "lastChangedAt", "claimableAt");
        Assert.True(release.GetProperty("id").GetInt32() > 0);
        Assert.Equal(FormatDate(date), release.GetProperty("date").GetString());
        if (area is not null) Assert.Equal(area, release.GetProperty("area").GetString());
        if (status is not null) Assert.Equal(status, release.GetProperty("status").GetString());
        Assert.Equal(employeeName, release.GetProperty("createdBy").GetString());
        Assert.Equal(employeeName, release.GetProperty("lastChangedBy").GetString());
        AssertTimestamp(release.GetProperty("createdAt"));
        AssertTimestamp(release.GetProperty("lastChangedAt"));
        AssertTimestamp(release.GetProperty("claimableAt"));
    }

    private static void AssertVerification(JsonElement record)
    {
        AssertProperties(record, "id", "plate", "driver", "date", "time", "result", "entryStatus", "indicatorState", "reason", "permanentSticker");
        Assert.True(record.GetProperty("id").GetInt64() > 0);
        Assert.False(string.IsNullOrWhiteSpace(record.GetProperty("plate").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(record.GetProperty("driver").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(record.GetProperty("entryStatus").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(record.GetProperty("reason").GetString()));
        Assert.Contains(record.GetProperty("result").GetString(), new[] { "Authorised", "Existing process", "Denied" });
        Assert.Contains(record.GetProperty("indicatorState").GetString(), new[] { "green", "off", "not used" });
        Assert.True(DateOnly.TryParse(record.GetProperty("date").GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
        Assert.True(TimeOnly.TryParse(record.GetProperty("time").GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
        Assert.True(record.GetProperty("permanentSticker").ValueKind is JsonValueKind.True or JsonValueKind.False);
    }

    private static void AssertReportStatistics(JsonElement statistics)
    {
        AssertProperties(statistics, "releasedSpaces", "temporaryClaims", "utilizationPercent");
        Assert.True(statistics.GetProperty("releasedSpaces").GetInt32() >= 0);
        Assert.True(statistics.GetProperty("temporaryClaims").GetInt32() >= 0);
        Assert.Equal(JsonValueKind.Number, statistics.GetProperty("utilizationPercent").ValueKind);
    }

    private static void AssertUnusedRecord(JsonElement record)
    {
        AssertProperties(record, "id", "category", "employee", "vehicle", "date");
        Assert.True(record.GetProperty("id").GetInt32() > 0);
        Assert.Contains(record.GetProperty("category").GetString(), new[] { "Unused Dedicated Parking", "Unused Temporary Parking" });
        Assert.False(string.IsNullOrWhiteSpace(record.GetProperty("employee").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(record.GetProperty("vehicle").GetString()));
        Assert.True(DateOnly.TryParse(record.GetProperty("date").GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
    }

    private static void AssertTimestamp(JsonElement value) =>
        Assert.True(DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _));

    private static void AssertProperties(JsonElement value, params string[] properties)
    {
        Assert.Equal(JsonValueKind.Object, value.ValueKind);
        foreach (var property in properties)
            Assert.True(value.TryGetProperty(property, out _), $"Expected JSON property '{property}'.");
    }

    private static DateTime At(DateOnly date, int hour) =>
        new(date.Year, date.Month, date.Day, hour, 0, 0, DateTimeKind.Utc);

    private static string FormatDate(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed record TestEmployee(int Id, int AccountId, string Name, string Email, string ParkingStatus, string? AssignedFloor);

    private sealed class TestApp : IDisposable
    {
        private readonly AppFactory _restarted;
        private readonly WebApplicationFactory<Program> _configured;
        private readonly IReadOnlyList<UnavailableGateServiceProxy> _gateFakes;
        public HttpClient Client { get; }
        public int GateServiceCallCount => _gateFakes.Sum(fake => fake.CallCount);

        public TestApp(AppFactory restarted, WebApplicationFactory<Program> configured, HttpClient client,
            IReadOnlyList<UnavailableGateServiceProxy> gateFakes)
        {
            _restarted = restarted;
            _configured = configured;
            Client = client;
            _gateFakes = gateFakes;
        }

        public void Dispose()
        {
            Client.Dispose();
            _configured.Dispose();
            _restarted.Dispose();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset fixedUtcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => fixedUtcNow;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}

public sealed class Part2AuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Part2TestAuthentication";

    public Part2AuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var employeeId = Request.Headers["X-Test-Employee-Id"].ToString();
        if (string.IsNullOrWhiteSpace(employeeId))
            return Task.FromResult(AuthenticateResult.NoResult());

        var accountId = Request.Headers["X-Test-Account-Id"].ToString();
        var role = Request.Headers["X-Test-Role"].ToString();
        var name = Request.Headers["X-Test-Employee-Name"].ToString();
        var email = Request.Headers["X-Test-Employee-Email"].ToString();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, accountId),
            new("sub", accountId),
            new("accountId", accountId),
            new("employeeId", employeeId),
            new("EmployeeId", employeeId),
            new("employee_id", employeeId),
            new(ClaimTypes.Name, name),
            new(ClaimTypes.Email, email),
            new("role", role)
        };
        if (role.Equals("Administrator", StringComparison.OrdinalIgnoreCase))
        {
            claims.Add(new Claim(ClaimTypes.Role, "Administrator"));
            claims.Add(new Claim(ClaimTypes.Role, "administrator"));
        }
        else if (role.Equals("GateGuard", StringComparison.OrdinalIgnoreCase))
        {
            claims.Add(new Claim(ClaimTypes.Role, "GateGuard"));
            claims.Add(new Claim(ClaimTypes.Role, "gateGuard"));
        }
        else
        {
            claims.Add(new Claim(ClaimTypes.Role, "employee"));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public sealed class UnavailableGateServiceProxy : DispatchProxy
{
    private int _callCount;
    public int CallCount => Volatile.Read(ref _callCount);

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        Fail();

    private object? Fail()
    {
        Interlocked.Increment(ref _callCount);
        throw new HttpRequestException("The gate-event service is unavailable.");
    }
}
