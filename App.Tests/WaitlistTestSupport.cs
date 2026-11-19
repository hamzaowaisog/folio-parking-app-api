using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using App.Api;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace App.Tests;

public sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Signs a request in from two headers: X-Test-Employee (the employee's Id) and X-Test-Admin.</summary>
public sealed class WaitlistTestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "WaitlistTest";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var id = Request.Headers["X-Test-Employee"].ToString();
        if (id == "") return Task.FromResult(AuthenticateResult.NoResult());
        var admin = Request.Headers["X-Test-Admin"].ToString() == "1";
        var role = admin ? "parkingAdministrator" : "employee";
        var identity = new ClaimsIdentity(
            [new Claim("employeeId", id), new Claim("accountId", id), new Claim(ClaimTypes.Role, role), new Claim("role", role), new Claim(ClaimTypes.Name, "Test " + id)],
            Scheme, ClaimTypes.Name, ClaimTypes.Role);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
    }
}

/// <summary>The app on a test class's database with a clock the test moves, and helpers to arrange employees and releases.</summary>
public sealed class WaitlistApp : IDisposable
{
    private static int _next = 1000;
    private readonly AppFactory _restarted;
    private readonly WebApplicationFactory<Program> _app;
    public MutableTimeProvider Time { get; }
    public HttpClient Client { get; }
    public string ConnectionString { get; }

    public WaitlistApp(AppFactory root, DateTimeOffset now)
    {
        Time = new MutableTimeProvider(now);
        ConnectionString = root.ConnectionString;
        _restarted = root.Restarted();
        _app = _restarted.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = WaitlistTestAuth.Scheme;
                    options.DefaultChallengeScheme = WaitlistTestAuth.Scheme;
                    options.DefaultForbidScheme = WaitlistTestAuth.Scheme;
                })
                .AddScheme<AuthenticationSchemeOptions, WaitlistTestAuth>(WaitlistTestAuth.Scheme, _ => { });
        }));
        Client = _app.CreateClient();
    }

    public AppDb Db() => new(new DbContextOptionsBuilder<AppDb>().UseNpgsql(ConnectionString).Options);

    public async Task<int> AddEmployee(EmployeeParkingStatus status, string? floor = null)
    {
        var n = Interlocked.Increment(ref _next);
        await using var db = Db();
        var employee = new Employee { EmployeeId = $"WL-{n}", Name = $"Waitlist {n}", Email = $"wl{n}@example.test", Role = "Tester", ParkingStatus = status, AssignedFloor = floor };
        db.Employees.Add(employee);
        db.Set<VehicleEntity>().Add(new VehicleEntity { Employee = employee, Plate = $"PL-{n}" });
        await db.SaveChangesAsync();
        return employee.Id;
    }

    /// <summary>A Dedicated owner releasing a spot on a date; claimable from claimableAt (default: an hour before the clock's now).</summary>
    public async Task<int> AddRelease(DateOnly date, DateTimeOffset? claimableAt = null, string area = "Floor 1", int? ownerId = null)
    {
        ownerId ??= await AddEmployee(EmployeeParkingStatus.Dedicated, area);
        await using var db = Db();
        var at = claimableAt ?? Time.Now.AddHours(-1);
        var release = new ParkingRelease
        {
            EmployeeId = ownerId.Value, Date = date, Status = ParkingReleaseStatus.Released, Area = area,
            CreatedByEmployeeId = ownerId.Value, LastChangedByEmployeeId = ownerId.Value,
            CreatedAt = at, LastChangedAt = at, ClaimableAt = at
        };
        db.Set<ParkingRelease>().Add(release);
        await db.SaveChangesAsync();
        return release.Id;
    }

    public Task<HttpResponseMessage> Send(HttpMethod method, string path, int? employee, object? body = null, bool admin = false)
    {
        var request = new HttpRequestMessage(method, path);
        if (employee is not null) request.Headers.Add("X-Test-Employee", employee.Value.ToString());
        if (admin) request.Headers.Add("X-Test-Admin", "1");
        if (body is not null) request.Content = JsonContent.Create(body);
        return Client.SendAsync(request);
    }

    public Task<HttpResponseMessage> Join(int employee, DateOnly date) =>
        Send(HttpMethod.Post, "/parking/waitlist", employee, new { date = date.ToString("yyyy-MM-dd") });

    public async Task<string> PlateFor(int employee)
    {
        await using var db = Db();
        return await db.Set<VehicleEntity>().Where(v => v.EmployeeId == employee).Select(v => v.Plate).FirstAsync();
    }

    public async Task<HttpResponseMessage> Claim(int employee, DateOnly date) =>
        await Send(HttpMethod.Post, "/parking/claims", employee, new { date = date.ToString("yyyy-MM-dd"), plate = await PlateFor(employee) });

    public Task<HttpResponseMessage> MyWaitlist(int employee) => Send(HttpMethod.Get, "/parking/waitlist", employee);

    public static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    public void Dispose()
    {
        Client.Dispose();
        _app.Dispose();
        _restarted.Dispose();
    }
}
