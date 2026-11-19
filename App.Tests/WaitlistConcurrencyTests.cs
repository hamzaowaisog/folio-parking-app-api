using System.Net;
using App.Api;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace App.Tests;

public class WaitlistConcurrencyTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private static readonly DateTimeOffset Now = new(2030, 3, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AC20_AtTheSameMomentOnlyTheHolderGetsTheSpotAndItIsNeverHeldOrClaimedTwice()
    {
        using var app = new WaitlistApp(factory, Now);
        var date = new DateOnly(2030, 4, 1);
        var people = new List<int>();
        for (var i = 0; i < 8; i++) people.Add(await app.AddEmployee(EmployeeParkingStatus.Temporary));
        foreach (var p in people) await app.Join(p, date);
        await app.AddRelease(date, claimableAt: Now);

        // every person claims, and reads of every kind run, all together
        var claims = people.Select(p => Task.Run(() => app.Claim(p, date))).ToList();
        var reads = people.SelectMany(p => new[]
        {
            Task.Run(() => app.MyWaitlist(p)),
            Task.Run(() => app.Send(HttpMethod.Get, $"/parking/availability?date={date:yyyy-MM-dd}", p)),
        }).ToList();
        var responses = await Task.WhenAll(claims);
        await Task.WhenAll(reads);

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        await using var db = app.Db();
        Assert.Equal(1, await db.Set<ParkingClaimEntity>().CountAsync(c => c.Date == date));
        Assert.Equal(people[0], (await db.Set<ParkingClaimEntity>().SingleAsync(c => c.Date == date)).EmployeeId); // the first in line
        Assert.Empty(await db.Set<WaitlistEntry>().Where(w => w.Date == date && w.Status == WaitlistEntryStatus.Offered).ToListAsync());
    }

    [Fact]
    public async Task AC20_ParallelEvaluationsOfLapsedHoldsNeverHoldOneSpotForTwoPeople()
    {
        using var app = new WaitlistApp(factory, Now);
        var date = new DateOnly(2030, 4, 2);
        var people = new List<int>();
        for (var i = 0; i < 8; i++) people.Add(await app.AddEmployee(EmployeeParkingStatus.Temporary));
        foreach (var p in people) await app.Join(p, date);
        await app.AddRelease(date, claimableAt: Now, area: "Floor 1");
        await app.AddRelease(date, claimableAt: Now, area: "Floor 2");
        await app.MyWaitlist(people[0]);
        // Spot 1 passes p0, p2, p3-in-order as holds lapse; spot 2 passes p1, p4, p5. At 10:05 the holders are p3 and p5, whichever request evaluates first.
        app.Time.Now = Now.AddMinutes(65);

        var responses = await Task.WhenAll(people.Select(p => Task.Run(() => app.MyWaitlist(p))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        await using var db = app.Db();
        var offered = await db.Set<WaitlistEntry>().Where(w => w.Date == date && w.Status == WaitlistEntryStatus.Offered).ToListAsync();
        Assert.Equal(2, offered.Count);
        Assert.Equal(2, offered.Select(w => w.ReleaseId).Distinct().Count());
        Assert.Equal(new[] { people[3], people[5] }, offered.Select(w => w.EmployeeId).Order());
    }
}
