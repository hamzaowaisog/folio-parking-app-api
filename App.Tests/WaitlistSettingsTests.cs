using System.Net;
using App.Api;
using Xunit;

namespace App.Tests;

public class WaitlistSettingsTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 9, 0, 0, TimeSpan.Zero);

    private static object Settings(int? hold) => hold is null
        ? new { dailyReleaseTime = "08:00:00", areas = new[] { new { name = "Floor 1" } }, reportRecipients = new[] { "a@example.test" } }
        : new { dailyReleaseTime = "08:00:00", areas = new[] { new { name = "Floor 1" } }, reportRecipients = new[] { "a@example.test" }, waitlistHoldMinutes = hold };

    [Fact]
    public async Task HoldMinutesDefaultToThirty()
    {
        using var root = new AppFactory(); // own database: the stored value is what is being tested
        using var app = new WaitlistApp(root, Now);
        var response = await app.Send(HttpMethod.Get, "/admin/settings", await app.AddEmployee(EmployeeParkingStatus.Temporary), admin: true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(30, (await WaitlistApp.Json(response)).GetProperty("waitlistHoldMinutes").GetInt32());
    }

    [Theory]
    [InlineData(5)]
    [InlineData(240)]
    public async Task HoldMinutesInRangeAreSavedAndReturned(int minutes)
    {
        using var root = new AppFactory(); // own database: the stored value is what is being tested
        using var app = new WaitlistApp(root, Now);
        var admin = await app.AddEmployee(EmployeeParkingStatus.Temporary);
        var put = await app.Send(HttpMethod.Put, "/admin/settings", admin, Settings(minutes), admin: true);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(minutes, (await WaitlistApp.Json(put)).GetProperty("waitlistHoldMinutes").GetInt32());
        var get = await app.Send(HttpMethod.Get, "/admin/settings", admin, admin: true);
        Assert.Equal(minutes, (await WaitlistApp.Json(get)).GetProperty("waitlistHoldMinutes").GetInt32());
    }

    [Theory]
    [InlineData(4)]
    [InlineData(241)]
    [InlineData(0)]
    [InlineData(-30)]
    public async Task HoldMinutesOutsideTheRangeAreRejected(int minutes)
    {
        using var root = new AppFactory(); // own database: the stored value is what is being tested
        using var app = new WaitlistApp(root, Now);
        var admin = await app.AddEmployee(EmployeeParkingStatus.Temporary);
        var put = await app.Send(HttpMethod.Put, "/admin/settings", admin, Settings(minutes), admin: true);
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task OmittingHoldMinutesKeepsTheStoredValue()
    {
        using var root = new AppFactory(); // own database: the stored value is what is being tested
        using var app = new WaitlistApp(root, Now);
        var admin = await app.AddEmployee(EmployeeParkingStatus.Temporary);
        await app.Send(HttpMethod.Put, "/admin/settings", admin, Settings(90), admin: true);
        var put = await app.Send(HttpMethod.Put, "/admin/settings", admin, Settings(null), admin: true);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(90, (await WaitlistApp.Json(put)).GetProperty("waitlistHoldMinutes").GetInt32());
    }
}
