using System.Net;
using App.Api;
using Xunit;

namespace App.Tests;

public class WaitlistReportTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static readonly DateTimeOffset Now = new(2030, 3, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AC27_ReportsPerDateJoinedOfferedClaimedExpiredAndStillWaiting()
    {
        using var root = new AppFactory(); // own database: the report counts every entry in range
        using var app = new WaitlistApp(root, Now);
        var admin = await app.AddEmployee(EmployeeParkingStatus.Temporary);
        var day1 = new DateOnly(2030, 5, 1);
        var day2 = new DateOnly(2030, 5, 2);
        var p = new List<int>();
        for (var i = 0; i < 5; i++) p.Add(await app.AddEmployee(EmployeeParkingStatus.Temporary));
        // day 1: four join, one spot. p0 claims it; p1 leaves; p2, p3 keep waiting.
        for (var i = 0; i < 4; i++) await app.Join(p[i], day1);
        await app.AddRelease(day1, claimableAt: Now);
        await app.Claim(p[0], day1);
        await app.Send(HttpMethod.Delete, $"/parking/waitlist/{day1:yyyy-MM-dd}", p[1]);
        // day 2: p4 joins, one spot, holds lapse unclaimed
        await app.Join(p[4], day2);
        await app.AddRelease(day2, claimableAt: Now);
        await app.MyWaitlist(p[4]);
        app.Time.Now = Now.AddMinutes(31);

        var response = await app.Send(HttpMethod.Get, "/admin/reports/waitlist?from=2030-05-01&to=2030-05-02", admin, admin: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var days = (await WaitlistApp.Json(response)).GetProperty("days").EnumerateArray().ToArray();
        Assert.Equal(2, days.Length);
        AssertDay(days[0], "2030-05-01", joined: 4, offered: 1, claimed: 1, expired: 0, stillWaiting: 2);
        AssertDay(days[1], "2030-05-02", joined: 1, offered: 1, claimed: 0, expired: 1, stillWaiting: 0);
    }

    [Fact]
    public async Task OnlyAdministratorsMayReadItAndTheRangeIsValidated()
    {
        using var app = new WaitlistApp(factory, Now);
        var emp = await app.AddEmployee(EmployeeParkingStatus.Temporary);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Send(HttpMethod.Get, "/admin/reports/waitlist?from=2030-05-01&to=2030-05-02", emp)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Send(HttpMethod.Get, "/admin/reports/waitlist?from=2030-05-01&to=2030-05-02", null)).StatusCode);
        foreach (var bad in new[] { "", "?from=2030-05-01", "?from=2030-05-03&to=2030-05-01", "?from=2030-01-01&to=2031-12-31" })
            Assert.Equal(HttpStatusCode.BadRequest, (await app.Send(HttpMethod.Get, "/admin/reports/waitlist" + bad, emp, admin: true)).StatusCode);
    }

    private static void AssertDay(System.Text.Json.JsonElement day, string date, int joined, int offered, int claimed, int expired, int stillWaiting)
    {
        Assert.Equal(date, day.GetProperty("date").GetString());
        Assert.Equal(joined, day.GetProperty("joined").GetInt32());
        Assert.Equal(offered, day.GetProperty("offered").GetInt32());
        Assert.Equal(claimed, day.GetProperty("claimed").GetInt32());
        Assert.Equal(expired, day.GetProperty("expired").GetInt32());
        Assert.Equal(stillWaiting, day.GetProperty("stillWaiting").GetInt32());
    }
}
