using System.Net;
using System.Text.Json;
using App.Api;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace App.Tests;

public class WaitlistAllocationTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static readonly DateTimeOffset Now = new(2030, 3, 4, 9, 0, 0, TimeSpan.Zero);
    private static int _day; // each test uses its own date, so tests in this class never share a queue
    private static DateOnly NewDate() => new DateOnly(2030, 3, 4).AddDays(10 + Interlocked.Increment(ref _day));
    private static string Fmt(DateOnly d) => d.ToString("yyyy-MM-dd");

    private WaitlistApp App(DateTimeOffset? now = null) => new(factory, now ?? Now);
    private static Task<int> Temp(WaitlistApp app) => app.AddEmployee(EmployeeParkingStatus.Temporary);

    private static async Task<JsonElement> Mine(WaitlistApp app, int employee, DateOnly date)
    {
        var list = await WaitlistApp.Json(await app.MyWaitlist(employee));
        return list.GetProperty("entries").EnumerateArray().Last(e => e.GetProperty("date").GetString() == Fmt(date));
    }

    [Fact]
    public async Task AC15_AReleaseClaimableForADateWithPeopleWaitingIsHeldForTheFirstOne()
    {
        using var app = App();
        var date = NewDate();
        var a = await Temp(app); var b = await Temp(app);
        await app.Join(a, date); await app.Join(b, date);
        await app.AddRelease(date, claimableAt: Now);

        var first = await Mine(app, a, date);
        var second = await Mine(app, b, date);

        Assert.Equal("Offered", first.GetProperty("status").GetString());
        Assert.Equal(Now.AddMinutes(30), first.GetProperty("heldUntil").GetDateTimeOffset());
        Assert.Equal(1, first.GetProperty("position").GetInt32());
        Assert.Equal("Waiting", second.GetProperty("status").GetString());
        Assert.Equal(2, second.GetProperty("position").GetInt32());
    }

    [Fact]
    public async Task AC16_SeveralSpotsAreHeldForSeveralPeopleOnePerSpot()
    {
        using var app = App();
        var date = NewDate();
        var p = new[] { await Temp(app), await Temp(app), await Temp(app) };
        foreach (var e in p) await app.Join(e, date);
        await app.AddRelease(date, claimableAt: Now, area: "Floor 1");
        await app.AddRelease(date, claimableAt: Now, area: "Floor 2");

        var statuses = new List<string>();
        foreach (var e in p) statuses.Add((await Mine(app, e, date)).GetProperty("status").GetString()!);

        Assert.Equal(new[] { "Offered", "Offered", "Waiting" }, statuses);
        await using var db = app.Db();
        var held = await db.Set<WaitlistEntry>().Where(w => w.Date == date && w.Status == WaitlistEntryStatus.Offered).Select(w => w.ReleaseId).ToListAsync();
        Assert.Equal(2, held.Distinct().Count());
    }

    [Fact]
    public async Task AC17_AHeldSpotCanOnlyBeClaimedByItsHolder_AC23_ThenTheEntryIsClaimed()
    {
        using var app = App();
        var date = NewDate();
        var holder = await Temp(app); var waiting = await Temp(app); var outsider = await Temp(app);
        await app.Join(holder, date); await app.Join(waiting, date);
        await app.AddRelease(date, claimableAt: Now);

        Assert.Equal(HttpStatusCode.Conflict, (await app.Claim(waiting, date)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await app.Claim(outsider, date)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await app.Claim(holder, date)).StatusCode);

        Assert.Equal("Claimed", (await Mine(app, holder, date)).GetProperty("status").GetString());
        var next = await Mine(app, waiting, date);
        Assert.Equal("Waiting", next.GetProperty("status").GetString());
        Assert.Equal(1, next.GetProperty("position").GetInt32());
    }

    [Fact]
    public async Task AC18_AnExpiredHoldMovesToTheNextPersonThenToAnyone()
    {
        using var app = App();
        var date = NewDate();
        var a = await Temp(app); var b = await Temp(app); var outsider = await Temp(app);
        await app.Join(a, date); await app.Join(b, date);
        await app.AddRelease(date, claimableAt: Now);

        app.Time.Now = Now.AddMinutes(31);
        var bEntry = await Mine(app, b, date);
        Assert.Equal("Expired", (await Mine(app, a, date)).GetProperty("status").GetString());
        Assert.Equal("Offered", bEntry.GetProperty("status").GetString());
        Assert.Equal(Now.AddMinutes(60), bEntry.GetProperty("heldUntil").GetDateTimeOffset()); // from when the first hold ended, not from this request
        Assert.Equal(HttpStatusCode.Conflict, (await app.Claim(outsider, date)).StatusCode);

        app.Time.Now = Now.AddMinutes(61);
        Assert.Equal("Expired", (await Mine(app, b, date)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Created, (await app.Claim(outsider, date)).StatusCode);
    }

    [Fact]
    public async Task AC19_TheResultIsTheSameWhicheverRequestEvaluatesTheExpiry()
    {
        var triggers = new Func<WaitlistApp, DateOnly, int, int, Task>[]
        {
            async (app, date, waiting, outsider) => await app.MyWaitlist(waiting),
            async (app, date, waiting, outsider) => await app.Send(HttpMethod.Get, $"/parking/availability?date={Fmt(date)}", outsider),
            async (app, date, waiting, outsider) => await app.Claim(outsider, date),
            async (app, date, waiting, outsider) => await app.Join(await Temp(app), date),
        };
        var results = new List<string>();
        foreach (var trigger in triggers)
        {
            using var app = App();
            var date = NewDate();
            var a = await Temp(app); var b = await Temp(app); var outsider = await Temp(app);
            await app.Join(a, date); await app.Join(b, date);
            await app.AddRelease(date, claimableAt: Now);
            await app.MyWaitlist(a); // offered to a at Now
            app.Time.Now = Now.AddMinutes(45);

            await trigger(app, date, b, outsider);

            await using var db = app.Db();
            var rows = await db.Set<WaitlistEntry>().Where(w => w.Date == date && (w.EmployeeId == a || w.EmployeeId == b)).OrderBy(w => w.Id).ToListAsync();
            results.Add(string.Join(" | ", rows.Select((w, i) => $"{i}:{w.Status}:{w.OfferedAt:O}:{w.HeldUntil:O}")));
        }
        Assert.Equal(4, results.Count);
        Assert.All(results, r => Assert.Equal(results[0], r));
        Assert.Contains("Expired", results[0]);
        Assert.Contains("Offered", results[0]);
    }

    [Fact]
    public async Task AC22_AHoldEndsAtTheEndOfItsDateAtTheLatest()
    {
        var late = new DateTimeOffset(2030, 3, 4, 23, 50, 0, TimeSpan.Zero);
        using var app = App(late);
        var today = new DateOnly(2030, 3, 4);
        var a = await Temp(app);
        await app.Join(a, today);
        await app.AddRelease(today, claimableAt: late);

        var entry = await Mine(app, a, today);

        Assert.Equal("Offered", entry.GetProperty("status").GetString());
        Assert.Equal(new DateTimeOffset(2030, 3, 5, 0, 0, 0, TimeSpan.Zero), entry.GetProperty("heldUntil").GetDateTimeOffset());
    }

    [Fact]
    public async Task AC24_AvailabilityCountsAHeldSpotOnlyForItsHolder()
    {
        using var app = App();
        var date = NewDate();
        var holder = await Temp(app); var other = await Temp(app);
        await app.Join(holder, date);
        await app.AddRelease(date, claimableAt: Now);

        var forHolder = await WaitlistApp.Json(await app.Send(HttpMethod.Get, $"/parking/availability?date={Fmt(date)}", holder));
        var forOther = await WaitlistApp.Json(await app.Send(HttpMethod.Get, $"/parking/availability?date={Fmt(date)}", other));

        Assert.Equal(1, forHolder.GetProperty("count").GetInt32());
        Assert.Equal(0, forOther.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task ASpotFreedWhenTheHolderLeavesIsHeldForTheNextPersonFromThatMoment()
    {
        using var app = App();
        var date = NewDate();
        var a = await Temp(app); var b = await Temp(app);
        await app.Join(a, date); await app.Join(b, date);
        await app.AddRelease(date, claimableAt: Now);
        await app.MyWaitlist(a);

        app.Time.Now = Now.AddMinutes(10);
        Assert.Equal(HttpStatusCode.NoContent, (await app.Send(HttpMethod.Delete, $"/parking/waitlist/{Fmt(date)}", a)).StatusCode);

        var entry = await Mine(app, b, date);
        Assert.Equal("Offered", entry.GetProperty("status").GetString());
        Assert.Equal(Now.AddMinutes(40), entry.GetProperty("heldUntil").GetDateTimeOffset());
    }

    [Fact]
    public async Task ACancelledReleaseReturnsTheHoldToTheQueue()
    {
        using var app = App();
        var date = NewDate();
        var a = await Temp(app);
        await app.Join(a, date);
        var releaseId = await app.AddRelease(date, claimableAt: Now);
        Assert.Equal("Offered", (await Mine(app, a, date)).GetProperty("status").GetString());
        await using (var db = app.Db())
            await db.Set<ParkingRelease>().Where(r => r.Id == releaseId).ExecuteUpdateAsync(s => s.SetProperty(r => r.IsCancelled, true));

        var entry = await Mine(app, a, date);

        Assert.Equal("Waiting", entry.GetProperty("status").GetString());
        Assert.Equal(1, entry.GetProperty("position").GetInt32());
    }

    [Fact]
    public async Task WithNoWaitlistTheLowestIdReleaseIsStillClaimedFirst()
    {
        using var app = App();
        var date = NewDate();
        var first = await app.AddRelease(date, claimableAt: Now, area: "Floor 1");
        await app.AddRelease(date, claimableAt: Now, area: "Floor 2");
        var claimant = await Temp(app);

        var response = await app.Claim(claimant, date);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Floor 1", (await WaitlistApp.Json(response)).GetProperty("area").GetString());
        await using var db = app.Db();
        Assert.Equal(first, (await db.Set<ParkingClaimEntity>().SingleAsync(c => c.EmployeeId == claimant)).ReleaseId);
    }
}
