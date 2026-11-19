using System.Net;
using App.Api;
using Xunit;

namespace App.Tests;

public class WaitlistJoinTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static readonly DateTimeOffset Now = new(2030, 3, 4, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2030, 3, 4);
    private static int _day; // each test uses its own future date, so tests in this class never share a queue
    private static DateOnly NewDate() => Today.AddDays(10 + Interlocked.Increment(ref _day));

    private WaitlistApp App() => new(factory, Now);

    [Fact]
    public async Task AC6_JoiningReturns201WithThePosition()
    {
        using var app = App();
        var date = NewDate();
        var first = await app.AddEmployee(EmployeeParkingStatus.Temporary);
        var second = await app.AddEmployee(EmployeeParkingStatus.Temporary);

        var r1 = await app.Join(first, date);
        var r2 = await app.Join(second, date);

        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        var body = await WaitlistApp.Json(r1);
        Assert.Equal(1, body.GetProperty("position").GetInt32());
        Assert.Equal("Waiting", body.GetProperty("status").GetString());
        Assert.Equal(date.ToString("yyyy-MM-dd"), body.GetProperty("date").GetString());
        Assert.Equal(2, (await WaitlistApp.Json(r2)).GetProperty("position").GetInt32());
    }

    [Fact]
    public async Task AC7_JoiningTwiceIs409()
    {
        using var app = App();
        var date = NewDate();
        var emp = await app.AddEmployee(EmployeeParkingStatus.Temporary);
        await app.Join(emp, date);
        Assert.Equal(HttpStatusCode.Conflict, (await app.Join(emp, date)).StatusCode);
    }

    [Fact]
    public async Task AC7_JoiningWithAnExistingClaimForTheDateIs409()
    {
        using var app = App();
        var date = NewDate();
        var emp = await app.AddEmployee(EmployeeParkingStatus.Temporary);
        await app.AddRelease(date);
        Assert.Equal(HttpStatusCode.Created, (await app.Claim(emp, date)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await app.Join(emp, date)).StatusCode);
    }

    [Fact]
    public async Task AC8_JoiningWhileASpotIsClaimableIs409()
    {
        using var app = App();
        var date = NewDate();
        await app.AddRelease(date);
        var emp = await app.AddEmployee(EmployeeParkingStatus.Temporary);
        Assert.Equal(HttpStatusCode.Conflict, (await app.Join(emp, date)).StatusCode);
    }

    [Fact]
    public async Task AC13_JoiningIsAllowedBeforeAnyReleaseExistsAndWhileTheOnlyReleaseIsNotYetClaimable()
    {
        using var app = App();
        var date = NewDate();
        var early = await app.AddEmployee(EmployeeParkingStatus.Temporary);
        Assert.Equal(HttpStatusCode.Created, (await app.Join(early, date)).StatusCode);

        var date2 = NewDate();
        await app.AddRelease(date2, claimableAt: Now.AddHours(5));
        var other = await app.AddEmployee(EmployeeParkingStatus.Temporary);
        Assert.Equal(HttpStatusCode.Created, (await app.Join(other, date2)).StatusCode);
    }

    [Fact]
    public async Task AC9_DedicatedEmployeesGet403()
    {
        using var app = App();
        var dedicated = await app.AddEmployee(EmployeeParkingStatus.Dedicated, "Floor 1");
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Join(dedicated, NewDate())).StatusCode);
    }

    [Fact]
    public async Task AC10_PastDatesGet400AndTodayIsAllowed()
    {
        using var app = App();
        var emp = await app.AddEmployee(EmployeeParkingStatus.Temporary);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Join(emp, Today.AddDays(-1))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await app.Join(emp, Today)).StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedRequestsGet401()
    {
        using var app = App();
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Send(HttpMethod.Get, "/parking/waitlist", null)).StatusCode);
    }

    [Fact]
    public async Task AC11_LeavingReturns204AndEveryoneBehindMovesUp()
    {
        using var app = App();
        var date = NewDate();
        var a = await app.AddEmployee(EmployeeParkingStatus.Temporary);
        var b = await app.AddEmployee(EmployeeParkingStatus.Temporary);
        var c = await app.AddEmployee(EmployeeParkingStatus.Temporary);
        foreach (var e in new[] { a, b, c }) await app.Join(e, date);

        var left = await app.Send(HttpMethod.Delete, $"/parking/waitlist/{date:yyyy-MM-dd}", a);

        Assert.Equal(HttpStatusCode.NoContent, left.StatusCode);
        Assert.Equal(1, Position(await WaitlistApp.Json(await app.MyWaitlist(b))));
        Assert.Equal(2, Position(await WaitlistApp.Json(await app.MyWaitlist(c))));
        var mine = (await WaitlistApp.Json(await app.MyWaitlist(a))).GetProperty("entries")[0];
        Assert.Equal("Left", mine.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await app.Send(HttpMethod.Delete, $"/parking/waitlist/{date:yyyy-MM-dd}", a)).StatusCode);
    }

    [Fact]
    public async Task AC12_ListingShowsUpcomingEntriesWithDatePositionAndStatus()
    {
        using var app = App();
        var d1 = NewDate();
        var d2 = NewDate();
        var emp = await app.AddEmployee(EmployeeParkingStatus.Temporary);
        await app.Join(emp, d2);
        await app.Join(emp, d1);

        var body = await WaitlistApp.Json(await app.MyWaitlist(emp));

        var entries = body.GetProperty("entries").EnumerateArray().ToArray();
        Assert.Equal(2, entries.Length);
        Assert.Equal(new[] { d1, d2 }.Order().Select(d => d.ToString("yyyy-MM-dd")), entries.Select(e => e.GetProperty("date").GetString()));
        Assert.All(entries, e => Assert.Equal("Waiting", e.GetProperty("status").GetString()));
        Assert.All(entries, e => Assert.Equal(1, e.GetProperty("position").GetInt32()));
    }

    [Fact]
    public async Task AC12_PastEntriesAreNotListed()
    {
        using var app = App();
        var emp = await app.AddEmployee(EmployeeParkingStatus.Temporary);
        await app.Join(emp, Today);
        app.Time.Now = Now.AddDays(1);
        Assert.Empty((await WaitlistApp.Json(await app.MyWaitlist(emp))).GetProperty("entries").EnumerateArray());
    }

    [Fact]
    public async Task AC14_RejoiningAfterLeavingGoesToTheBackOfTheQueue()
    {
        using var app = App();
        var date = NewDate();
        var a = await app.AddEmployee(EmployeeParkingStatus.Temporary);
        var b = await app.AddEmployee(EmployeeParkingStatus.Temporary);
        await app.Join(a, date);
        await app.Join(b, date);
        await app.Send(HttpMethod.Delete, $"/parking/waitlist/{date:yyyy-MM-dd}", a);

        var again = await app.Join(a, date);

        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
        Assert.Equal(2, (await WaitlistApp.Json(again)).GetProperty("position").GetInt32());
        Assert.Equal(1, Position(await WaitlistApp.Json(await app.MyWaitlist(b))));
    }

    private static int Position(System.Text.Json.JsonElement list) =>
        list.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("status").GetString() is "Waiting" or "Offered").GetProperty("position").GetInt32();
}
