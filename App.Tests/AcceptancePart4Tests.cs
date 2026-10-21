using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace App.Tests.Part4;

public sealed class PersistenceWriteTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _factory;
    public PersistenceWriteTests(AppFactory factory) => _factory = factory;

    [Fact]
    public async Task AC_10_1_SettingsUpdateIsReadBackFromPostgres()
    {
        using var host = Part4Support.CreateHost(_factory);
        using var client = host.CreateClient();
        var adminId = await Part4Support.InsertEmployee(_factory.ConnectionString, "EMP-P4-ADMIN", "Part Four Admin", "admin10-1@example.test", "Facilities", "Administrator", "Dedicated", "Floor 1");
        await Part4Support.GrantAdministrator(_factory.ConnectionString, adminId);
        Part4Support.SetIdentity(client, adminId);

        var request = new
        {
            dailyReleaseTime = "07:45:00",
            areas = new[] { new { name = "North Lot" }, new { name = "East Garage" } },
            reportRecipients = new[] { "parking-ops10-1@example.test", "facilities10-1@example.test" }
        };
        var response = await Part4Support.PutJson(client, "/admin/settings", request, HttpStatusCode.OK);
        Assert.Equal("07:45:00", response.GetProperty("dailyReleaseTime").GetString());
        Assert.Equal(new[] { "East Garage", "North Lot" }, response.GetProperty("areas").EnumerateArray().Select(a => a.GetProperty("name").GetString()).OrderBy(x => x).ToArray());
        Assert.Equal(new[] { "facilities10-1@example.test", "parking-ops10-1@example.test" }, response.GetProperty("reportRecipients").EnumerateArray().Select(x => x.GetString()).OrderBy(x => x).ToArray());

        await using var db = Part4Support.NewDb(_factory.ConnectionString);
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(string_agg(\"Value\"::text, ' '), '') FROM \"ParkingSettings\"";
        var stored = (string)(await command.ExecuteScalarAsync())!;
        Assert.Contains("07:45:00", stored);
        Assert.Contains("North Lot", stored);
        Assert.Contains("East Garage", stored);
        Assert.Contains("parking-ops10-1@example.test", stored);
        Assert.Contains("facilities10-1@example.test", stored);
    }
}

public sealed class EmployeeRestartTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _factory;
    public EmployeeRestartTests(AppFactory factory) => _factory = factory;

    [Fact]
    public async Task AC_10_2_CreatedEmployeeValuesSurviveRestartAndFetch()
    {
        int createdId;
        using (var host = Part4Support.CreateHost(_factory))
        using (var client = host.CreateClient())
        {
            var adminId = await Part4Support.InsertEmployee(_factory.ConnectionString, "EMP-P4-ADMIN", "Part Four Admin", "admin10-2@example.test", "Facilities", "Administrator", "Dedicated", "Floor 1");
            await Part4Support.GrantAdministrator(_factory.ConnectionString, adminId);
            Part4Support.SetIdentity(client, adminId);

            var input = Part4Support.EmployeeInput("EMP-P4-NEW", "Daniyal Part Four", "daniyal10-2@example.test", "Operations", "Coordinator", "Temporary", null);
            var created = await Part4Support.PostJson(client, "/admin/employees", input, HttpStatusCode.Created);
            var employee = created.GetProperty("employee");
            Part4Support.AssertEmployee(employee, "EMP-P4-NEW", "Daniyal Part Four", "daniyal10-2@example.test", "Operations", "Coordinator", "Temporary", null);
            createdId = employee.GetProperty("id").GetInt32();
        }

        using var restarted = _factory.Restarted();
        using var restartedHost = Part4Support.CreateHost(restarted);
        using var restartedClient = restartedHost.CreateClient();
        var admin = await Part4Support.FindEmployeeId(_factory.ConnectionString, "EMP-P4-ADMIN");
        Part4Support.SetIdentity(restartedClient, admin);
        var fetched = await Part4Support.GetJson(restartedClient, $"/admin/employees/{createdId}", HttpStatusCode.OK);
        var persisted = fetched.GetProperty("employee");
        AssertEmployeeId(persisted, createdId);
        Part4Support.AssertEmployee(persisted, "EMP-P4-NEW", "Daniyal Part Four", "daniyal10-2@example.test", "Operations", "Coordinator", "Temporary", null);

        static void AssertEmployeeId(JsonElement employee, int expected) => Assert.Equal(expected, employee.GetProperty("id").GetInt32());
    }
}

public sealed class EmployeeUpdateRestartTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _factory;
    public EmployeeUpdateRestartTests(AppFactory factory) => _factory = factory;

    [Fact]
    public async Task AC_10_3_EmployeeUpdateIsPersistedAfterRestart()
    {
        int adminId;
        int employeeId;
        using (var host = Part4Support.CreateHost(_factory))
        using (var client = host.CreateClient())
        {
            adminId = await Part4Support.InsertEmployee(_factory.ConnectionString, "EMP-P4-ADMIN", "Part Four Admin", "admin10-3@example.test", "Facilities", "Administrator", "Dedicated", "Floor 1");
            await Part4Support.GrantAdministrator(_factory.ConnectionString, adminId);
            employeeId = await Part4Support.InsertEmployee(_factory.ConnectionString, "EMP-P4-OLD", "Old Employee Name", "old10-3@example.test", "Old Department", "Analyst", "Temporary", null);
            Part4Support.SetIdentity(client, adminId);

            var update = Part4Support.EmployeeInput("EMP-P4-OLD", "Updated Employee Name", "updated10-3@example.test", "New Department", "Senior Analyst", "Dedicated", "Floor 7");
            var result = await Part4Support.PutJson(client, $"/admin/employees/{employeeId}", update, HttpStatusCode.OK);
            Part4Support.AssertEmployee(result.GetProperty("employee"), "EMP-P4-OLD", "Updated Employee Name", "updated10-3@example.test", "New Department", "Senior Analyst", "Dedicated", "Floor 7");
        }

        using var restarted = _factory.Restarted();
        using var restartedHost = Part4Support.CreateHost(restarted);
        using var restartedClient = restartedHost.CreateClient();
        Part4Support.SetIdentity(restartedClient, adminId);
        var row = await Part4Support.ReadEmployee(_factory.ConnectionString, employeeId);
        Assert.Equal("EMP-P4-OLD", row.EmployeeId);
        Assert.Equal("Updated Employee Name", row.Name);
        Assert.Equal("updated10-3@example.test", row.Email);
        Assert.Equal("New Department", row.Department);
        Assert.Equal("Senior Analyst", row.Role);
        Assert.Equal("Dedicated", row.ParkingStatus);
        Assert.Equal("Floor 7", row.AssignedFloor);
    }
}

public sealed class ExplicitFixtureTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _factory;
    public ExplicitFixtureTests(AppFactory factory) => _factory = factory;

    [Fact]
    public async Task AC_10_4_EmployeeOperationUsesOnlyTheExplicitRequestAndFixture()
    {
        using var host = Part4Support.CreateHost(_factory, demoSeed: false);
        using var client = host.CreateClient();
        foreach (var table in Part4Support.AllTables)
            Assert.Equal(0, await Part4Support.TableCount(_factory.ConnectionString, table));
        var adminId = await Part4Support.InsertEmployee(_factory.ConnectionString, "EMP-P4-ADMIN", "Part Four Admin", "admin10-4@example.test", "Facilities", "Administrator", "Dedicated", "Floor 1");
        await Part4Support.GrantAdministrator(_factory.ConnectionString, adminId);
        Part4Support.SetIdentity(client, adminId);

        var input = Part4Support.EmployeeInput("EMP-P4-EXPLICIT", "Explicit Fixture Employee", "explicit10-4@example.test", "Research", "Technician", "Dedicated", "Floor 4");
        var created = await Part4Support.PostJson(client, "/admin/employees", input, HttpStatusCode.Created);
        var employee = created.GetProperty("employee");
        Part4Support.AssertEmployee(employee, "EMP-P4-EXPLICIT", "Explicit Fixture Employee", "explicit10-4@example.test", "Research", "Technician", "Dedicated", "Floor 4");
        var row = await Part4Support.ReadEmployeeByCode(_factory.ConnectionString, "EMP-P4-EXPLICIT");
        Assert.Equal("Explicit Fixture Employee", row.Name);
        Assert.Equal("explicit10-4@example.test", row.Email);
        Assert.Equal("Research", row.Department);
        Assert.Equal("Technician", row.Role);
        Assert.Equal("Dedicated", row.ParkingStatus);
        Assert.Equal("Floor 4", row.AssignedFloor);
    }
}

public sealed class DemoSeedTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _factory;
    public DemoSeedTests(AppFactory factory) => _factory = factory;

    [Fact]
    public async Task AC_11_1_DemoSeedCreatesRepresentativeRowsAndPopulatesContractLists()
    {
        using var host = Part4Support.CreateHost(_factory, demoSeed: true);
        using var client = host.CreateClient();

        foreach (var table in Part4Support.DemoTables)
        {
            var count = await Part4Support.TableCount(_factory.ConnectionString, table);
            Assert.InRange(count, 2, 4);
        }

        Assert.True(await Part4Support.Exists(_factory.ConnectionString, "SELECT EXISTS(SELECT 1 FROM \"Employees\" WHERE \"Name\" = 'Ayesha Malik')"));
        var employeeId = await Part4Support.FindEmployeeId(_factory.ConnectionString, "EMP-1042");
        Part4Support.SetIdentity(client, employeeId);

        await Part4Support.AssertNonEmptyList(client, "/parking/releases", "releases");
        await Part4Support.AssertNonEmptyList(client, "/parking/availability?date=2025-10-14", "areas");
        await Part4Support.AssertNonEmptyList(client, "/gate/events/live", "events");
        await Part4Support.AssertNonEmptyList(client, "/admin/entry-records", "records");
        await Part4Support.AssertNonEmptyList(client, "/admin/reports?period=daily", "rows");
        await Part4Support.AssertNonEmptyList(client, "/admin/reports/unused", "records");
        await Part4Support.AssertNonEmptyList(client, "/admin/employees", "employees");
        await Part4Support.AssertNonEmptyList(client, "/admin/admins", "administrators");
        await Part4Support.AssertNonEmptyList(client, "/admin/admins/directory?search=Ayesha", "employees");
    }
}

public sealed class SeedIdempotenceTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _factory;
    public SeedIdempotenceTests(AppFactory factory) => _factory = factory;

    [Fact]
    public async Task AC_11_2_SeedRunIsIdempotentAndPreservesExistingRows()
    {
        await using (var schema = Part4Support.NewDb(_factory.ConnectionString))
            await schema.Database.EnsureCreatedAsync();
        await Part4Support.RunSeedData(_factory.ConnectionString);
        await Part4Support.InsertEmployee(_factory.ConnectionString, "EMP-P4-NONSEED", "Nonseed Employee Must Stay", "nonseed11-2@example.test", "Existing Department", "Existing Role", "Temporary", null);

        var beforeCounts = await Part4Support.TableCounts(_factory.ConnectionString, Part4Support.AllTables);
        var beforeRows = await Part4Support.TableSnapshots(_factory.ConnectionString, Part4Support.AllTables);
        await Part4Support.RunSeedData(_factory.ConnectionString);
        var afterCounts = await Part4Support.TableCounts(_factory.ConnectionString, Part4Support.AllTables);
        var afterRows = await Part4Support.TableSnapshots(_factory.ConnectionString, Part4Support.AllTables);

        foreach (var table in Part4Support.AllTables)
        {
            Assert.Equal(beforeCounts[table], afterCounts[table]);
            Assert.Equal(beforeRows[table], afterRows[table]);
        }
        Assert.Equal(0, await Part4Support.DuplicateGroups(_factory.ConnectionString, "Employees", "\"EmployeeId\""));
        Assert.Equal(0, await Part4Support.DuplicateGroups(_factory.ConnectionString, "Accounts", "\"EmployeeId\""));
        Assert.Equal(0, await Part4Support.DuplicateGroups(_factory.ConnectionString, "SessionTokens", "\"TokenHash\""));
        Assert.Equal(0, await Part4Support.DuplicateGroups(_factory.ConnectionString, "Vehicles", "\"EmployeeId\", \"Plate\""));
        Assert.Equal(0, await Part4Support.DuplicateGroups(_factory.ConnectionString, "ParkingReleases", "\"EmployeeId\", \"Date\""));
        Assert.Equal(0, await Part4Support.DuplicateGroups(_factory.ConnectionString, "ParkingClaims", "\"EmployeeId\", \"Date\""));
        Assert.Equal(0, await Part4Support.DuplicateGroups(_factory.ConnectionString, "ParkingSettings", "\"Key\""));
        Assert.Equal(0, await Part4Support.DuplicateGroups(_factory.ConnectionString, "VerificationRecords", "\"Date\", \"Time\", \"Plate\""));
        Assert.Equal(0, await Part4Support.DuplicateGroups(_factory.ConnectionString, "AdministratorAccess", "\"EmployeeId\""));
        var nonseed = await Part4Support.ReadEmployeeByCode(_factory.ConnectionString, "EMP-P4-NONSEED");
        Assert.Equal("Nonseed Employee Must Stay", nonseed.Name);
        Assert.Equal("nonseed11-2@example.test", nonseed.Email);
        Assert.Equal("Existing Department", nonseed.Department);
        Assert.Equal("Existing Role", nonseed.Role);
        Assert.Equal("Temporary", nonseed.ParkingStatus);
    }
}

public sealed class NoDemoSeedTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _factory;
    public NoDemoSeedTests(AppFactory factory) => _factory = factory;

    [Fact]
    public async Task AC_11_3_DisabledDemoSeedLeavesTablesEmptyForExplicitOperations()
    {
        using var host = Part4Support.CreateHost(_factory, demoSeed: false);
        using var client = host.CreateClient();
        foreach (var table in Part4Support.AllTables)
            Assert.Equal(0, await Part4Support.TableCount(_factory.ConnectionString, table));

        var adminId = await Part4Support.InsertEmployee(_factory.ConnectionString, "EMP-P4-ADMIN", "Explicit Admin", "admin11-3@example.test", "Facilities", "Administrator", "Dedicated", "Floor 1");
        await Part4Support.GrantAdministrator(_factory.ConnectionString, adminId);
        Part4Support.SetIdentity(client, adminId);
        var input = Part4Support.EmployeeInput("EMP-P4-ONLY", "Explicit Operation", "explicit11-3@example.test", "Facilities", "Coordinator", "Temporary", null);
        var result = await Part4Support.PostJson(client, "/admin/employees", input, HttpStatusCode.Created);
        Part4Support.AssertEmployee(result.GetProperty("employee"), "EMP-P4-ONLY", "Explicit Operation", "explicit11-3@example.test", "Facilities", "Coordinator", "Temporary", null);
        var row = await Part4Support.ReadEmployeeByCode(_factory.ConnectionString, "EMP-P4-ONLY");
        Assert.Equal("Explicit Operation", row.Name);
        Assert.Equal("explicit11-3@example.test", row.Email);
        Assert.Equal(0, await Part4Support.TableCount(_factory.ConnectionString, "ParkingClaims"));
        Assert.Equal(0, await Part4Support.TableCount(_factory.ConnectionString, "VerificationRecords"));
    }
}

public sealed class ContractConformanceTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _factory;
    public ContractConformanceTests(AppFactory factory) => _factory = factory;

    [Fact]
    public async Task AC_12_1_AllDocumentedOperationsReturnTheirSuccessSchemas()
    {
        using var host = Part4Support.CreateHost(_factory, demoSeed: false);
        using var client = host.CreateClient();
        var adminId = await Part4Support.InsertEmployee(_factory.ConnectionString, "EMP-P4-ADMIN", "Part Four Admin", "admin12-1@example.test", "Facilities", "Administrator", "Dedicated", "Floor 2");
        await Part4Support.GrantAdministrator(_factory.ConnectionString, adminId);
        var temporaryId = await Part4Support.InsertEmployee(_factory.ConnectionString, "EMP-P4-TEMP", "Temporary Driver", "temp12-1@example.test", "Operations", "Coordinator", "Temporary", null);
        await Part4Support.InsertVehicle(_factory.ConnectionString, temporaryId, "P4-CLAIM-01", "Honda", "City", "Blue");
        await Part4Support.InsertEmployee(_factory.ConnectionString, "EMP-P4-REGISTER", "Contract Test User", "contract12-1@example.test", "Compliance", "Reviewer", "Temporary", null);
        await Part4Support.InsertRelease(_factory.ConnectionString, temporaryId, adminId, new DateOnly(2025, 10, 14), "Floor 3", new DateTime(2025, 10, 14, 2, 0, 0, DateTimeKind.Utc));
        await Part4Support.InsertVerification(_factory.ConnectionString, "P4-GATE-01", "Contract Driver", new DateOnly(2025, 10, 14), new TimeOnly(8, 25), "Denied", "Denied", "off", "No valid authorisation today", false);
        Part4Support.SetIdentity(client, adminId);

        var registrationRequest = new { name = "Contract Test User", email = "contract12-1@example.test", password = "Parking2025!" };
        var registration = await Part4Support.PostJson(client, "/auth/register", registrationRequest, HttpStatusCode.Created, "RegistrationResult");
        Assert.Equal("contract12-1@example.test", registration.GetProperty("email").GetString());
        Assert.Equal("created", registration.GetProperty("accountStatus").GetString());
        var session = await Part4Support.PostJson(client, "/auth/login", new { email = registrationRequest.email, password = registrationRequest.password }, HttpStatusCode.OK, "Session");
        var accessToken = session.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        await Part4Support.GetJson(client, "/me/profile", HttpStatusCode.OK, "EmployeeProfile");
        await Part4Support.PostJson(client, "/me/profile/retry", new { fields = new[] { "department", "assignedFloor" } }, HttpStatusCode.OK, "EmployeeProfile");
        await Part4Support.PostJson(client, "/me/vehicles", new { plate = "P4-ADMIN-01", make = "Toyota", model = "Corolla", color = "Silver" }, HttpStatusCode.Created, "Vehicle");
        await Part4Support.GetJson(client, "/parking/dashboard", HttpStatusCode.OK, "Dashboard");
        await Part4Support.PostJson(client, "/parking/releases/today", new { officeDate = "2025-10-14" }, HttpStatusCode.OK, "ReleaseRecord");
        await Part4Support.GetJson(client, "/parking/releases", HttpStatusCode.OK, "ReleaseList");
        var schedule = await Part4Support.PostJson(client, "/parking/releases", new { dates = new[] { "2025-10-22", "2025-10-23" } }, HttpStatusCode.Created, "ReleaseList");
        var scheduledReleaseId = schedule.GetProperty("releases").EnumerateArray().First().GetProperty("id").GetInt32();
        await Part4Support.PostJson(client, $"/parking/releases/{scheduledReleaseId}/cancel", new { reason = "Contract conformance test cancellation" }, HttpStatusCode.OK, "ReleaseActionResult");
        await Part4Support.GetJson(client, "/parking/availability?date=2025-10-14", HttpStatusCode.OK, "Availability");

        Part4Support.SetIdentity(client, temporaryId);
        await Part4Support.PostJson(client, "/parking/claims", new { date = "2025-10-14", plate = "P4-CLAIM-01" }, HttpStatusCode.Created, "ParkingClaim");
        await Part4Support.GetJson(client, "/gate/events/live?limit=20", HttpStatusCode.OK, "GateFeed");
        Part4Support.SetIdentity(client, adminId);
        await Part4Support.GetJson(client, "/admin/entry-records", HttpStatusCode.OK, "VerificationList");
        await Part4Support.GetJson(client, "/admin/reports?period=daily", HttpStatusCode.OK, "Report");
        await Part4Support.GetJson(client, "/admin/reports/unused", HttpStatusCode.OK, "UnusedParkingList");
        await Part4Support.GetJson(client, "/admin/employees", HttpStatusCode.OK, "EmployeeList");

        var createdEmployee = await Part4Support.PostJson(client, "/admin/employees", Part4Support.EmployeeInput("EMP-P4-CONFORMANCE", "Conformance Employee", "conformance12-1@example.test", "Compliance", "Reviewer", "Temporary", null), HttpStatusCode.Created, "EmployeeRecord");
        var newEmployeeId = createdEmployee.GetProperty("employee").GetProperty("id").GetInt32();
        await Part4Support.GetJson(client, $"/admin/employees/{newEmployeeId}", HttpStatusCode.OK, "EmployeeRecord");
        await Part4Support.PutJson(client, $"/admin/employees/{newEmployeeId}", Part4Support.EmployeeInput("EMP-P4-CONFORMANCE", "Updated Conformance Employee", "conformance12-1@example.test", "Compliance", "Senior Reviewer", "Dedicated", "Floor 8"), HttpStatusCode.OK, "EmployeeRecord");

        await Part4Support.PutJson(client, "/admin/settings", new
        {
            dailyReleaseTime = "08:15:00",
            areas = new[] { new { name = "Contract Floor A" }, new { name = "Contract Floor B" } },
            reportRecipients = new[] { "contract-reports12-1@example.test" }
        }, HttpStatusCode.OK, "ParkingSettings");
        await Part4Support.GetJson(client, "/admin/settings", HttpStatusCode.OK, "ParkingSettings");
        await Part4Support.GetJson(client, "/admin/admins", HttpStatusCode.OK, "AdministratorList");
        await Part4Support.PostJson(client, "/admin/admins", new { employeeId = temporaryId }, HttpStatusCode.OK, "AdminAccessResult");
        await Part4Support.GetJson(client, "/admin/admins/directory?search=Temporary", HttpStatusCode.OK, "DirectorySearchResult");
        await Part4Support.DeleteJson(client, $"/admin/admins/{temporaryId}", HttpStatusCode.OK, "AdminAccessResult");

        using var logout = await client.PostAsync("/auth/logout", content: null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
    }
}

public sealed class DatabaseFailureTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _factory;
    public DatabaseFailureTests(AppFactory factory) => _factory = factory;

    [Fact]
    public async Task AC_13_1_DatabaseFailureReturnsRedactedServerError()
    {
        var interceptor = new Part4Support.FailConnectionInterceptor();
        using var host = Part4Support.CreateHost(_factory, demoSeed: false, failConnections: interceptor);
        using var client = host.CreateClient();
        interceptor.Fail = true;
        using var response = await client.PostAsJsonAsync("/auth/register", new
        {
            name = "Failure Probe",
            email = "failure13-1@example.test",
            password = "Parking2025!"
        });

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("db.internal.invalid", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SELECT", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQL", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("query", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password=Part4Secret", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("NpgsqlException", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("host=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("port=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection refused", body, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class Part4Support
{
    public static readonly string[] AllTables =
    [
        "Employees", "Accounts", "SessionTokens", "Vehicles", "ParkingReleases", "ParkingClaims",
        "ParkingSettings", "VerificationRecords", "AdministratorAccess"
    ];

    public static readonly string[] DemoTables = AllTables;

    public static WebApplicationFactory<Program> CreateHost(AppFactory factory, bool demoSeed = false, FailConnectionInterceptor? failConnections = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seed:Demo"] = demoSeed ? "true" : "false"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(new DateTimeOffset(2025, 10, 14, 3, 0, 0, TimeSpan.Zero)));
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = Part4AuthHandler.SchemeName;
                        options.DefaultChallengeScheme = Part4AuthHandler.SchemeName;
                        options.DefaultScheme = Part4AuthHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, Part4AuthHandler>(Part4AuthHandler.SchemeName, _ => { });
                services.AddAuthorization(options =>
                {
                    options.AddPolicy("Administrator", policy => policy.RequireRole("Administrator", "parkingAdministrator"));
                    options.AddPolicy("GateOrAdmin", policy => policy.RequireRole("GateGuard", "gateGuard", "Administrator", "parkingAdministrator"));
                });

                if (failConnections is not null)
                {
                    foreach (var descriptor in services.Where(d => d.ServiceType.IsGenericType &&
                        d.ServiceType.GetGenericTypeDefinition().FullName == "Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration`1" &&
                        d.ServiceType.GenericTypeArguments[0] == typeof(AppDb)).ToArray())
                        services.Remove(descriptor);
                    services.RemoveAll<AppDb>();
                    services.RemoveAll<DbContextOptions<AppDb>>();
                    services.AddDbContext<AppDb>(options => options.UseNpgsql(factory.ConnectionString).AddInterceptors(failConnections));
                }
            });
        });

    public static AppDb NewDb(string connectionString) => new(new DbContextOptionsBuilder<AppDb>().UseNpgsql(connectionString).Options);

    public static async Task<int> InsertEmployee(string connectionString, string employeeCode, string name, string email, string department, string role, string parkingStatus, string? assignedFloor)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO \"Employees\" (\"EmployeeId\", \"Name\", \"Email\", \"Department\", \"Role\", \"ParkingStatus\", \"AssignedFloor\") VALUES (@code, @name, @email, @department, @role, @status, @floor) RETURNING \"Id\"";
        command.Parameters.AddWithValue("code", employeeCode);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("department", department);
        command.Parameters.AddWithValue("role", role);
        command.Parameters.AddWithValue("status", parkingStatus);
        command.Parameters.AddWithValue("floor", (object?)assignedFloor ?? DBNull.Value);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public static async Task GrantAdministrator(string connectionString, int employeeId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO \"AdministratorAccess\" (\"EmployeeId\", \"AccessStatus\", \"ChangedByEmployeeId\", \"ChangedAt\") VALUES (@id, 'active', @id, @at)";
        command.Parameters.AddWithValue("id", employeeId);
        command.Parameters.AddWithValue("at", new DateTime(2025, 10, 14, 3, 0, 0, DateTimeKind.Utc));
        await command.ExecuteNonQueryAsync();
    }

    public static async Task InsertVehicle(string connectionString, int employeeId, string plate, string make, string model, string color)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO \"Vehicles\" (\"EmployeeId\", \"Plate\", \"Make\", \"Model\", \"Color\") VALUES (@employee, @plate, @make, @model, @color)";
        command.Parameters.AddWithValue("employee", employeeId);
        command.Parameters.AddWithValue("plate", plate);
        command.Parameters.AddWithValue("make", make);
        command.Parameters.AddWithValue("model", model);
        command.Parameters.AddWithValue("color", color);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task InsertRelease(string connectionString, int employeeId, int changedBy, DateOnly date, string area, DateTime claimableAt)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO \"ParkingReleases\" (\"EmployeeId\", \"Date\", \"Status\", \"Area\", \"CreatedByEmployeeId\", \"CreatedAt\", \"LastChangedByEmployeeId\", \"LastChangedAt\", \"ClaimableAt\", \"IsCancelled\") VALUES (@employee, @date, 'Released', @area, @changedBy, @at, @changedBy, @at, @claimable, false)";
        command.Parameters.AddWithValue("employee", employeeId);
        command.Parameters.AddWithValue("date", date);
        command.Parameters.AddWithValue("area", area);
        command.Parameters.AddWithValue("changedBy", changedBy);
        command.Parameters.AddWithValue("at", new DateTime(2025, 10, 14, 3, 0, 0, DateTimeKind.Utc));
        command.Parameters.AddWithValue("claimable", claimableAt);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task InsertVerification(string connectionString, string plate, string driver, DateOnly date, TimeOnly time, string result, string entryStatus, string indicatorState, string reason, bool permanentSticker)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO \"VerificationRecords\" (\"Plate\", \"Driver\", \"Date\", \"Time\", \"Result\", \"EntryStatus\", \"IndicatorState\", \"Reason\", \"PermanentSticker\") VALUES (@plate, @driver, @date, @time, @result, @entry, @indicator, @reason, @sticker)";
        command.Parameters.AddWithValue("plate", plate);
        command.Parameters.AddWithValue("driver", driver);
        command.Parameters.AddWithValue("date", date);
        command.Parameters.AddWithValue("time", time);
        command.Parameters.AddWithValue("result", result);
        command.Parameters.AddWithValue("entry", entryStatus);
        command.Parameters.AddWithValue("indicator", indicatorState);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("sticker", permanentSticker);
        await command.ExecuteNonQueryAsync();
    }

    public static void SetIdentity(HttpClient client, int employeeId)
    {
        client.DefaultRequestHeaders.Remove("X-Test-Employee-Id");
        client.DefaultRequestHeaders.Add("X-Test-Employee-Id", employeeId.ToString());
    }

    public static object EmployeeInput(string employeeId, string name, string email, string department, string role, string parkingStatus, string? floor) => new
    {
        employeeId,
        name,
        email,
        department,
        role,
        parkingStatus,
        assignedFloor = floor,
        vehicles = Array.Empty<object>()
    };

    public static void AssertEmployee(JsonElement employee, string employeeCode, string name, string email, string department, string role, string parkingStatus, string? floor)
    {
        Assert.Equal(employeeCode, employee.GetProperty("employeeId").GetString());
        Assert.Equal(name, employee.GetProperty("name").GetString());
        Assert.Equal(email, employee.GetProperty("email").GetString());
        Assert.Equal(department, employee.GetProperty("department").GetString());
        Assert.Equal(role, employee.GetProperty("role").GetString());
        Assert.Equal(parkingStatus, employee.GetProperty("parkingStatus").GetString());
        if (floor is null) Assert.Equal(JsonValueKind.Null, employee.GetProperty("assignedFloor").ValueKind);
        else Assert.Equal(floor, employee.GetProperty("assignedFloor").GetString());
        Assert.Empty(employee.GetProperty("vehicles").EnumerateArray());
        Assert.Empty(employee.GetProperty("datedActivity").EnumerateArray());
    }

    public static async Task<JsonElement> GetJson(HttpClient client, string path, HttpStatusCode status, string? schema = null)
    {
        using var response = await client.GetAsync(path);
        return await ReadBody(response, status, schema);
    }

    public static async Task<JsonElement> PostJson(HttpClient client, string path, object body, HttpStatusCode status, string? schema = null)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        return await ReadBody(response, status, schema);
    }

    public static async Task<JsonElement> PutJson(HttpClient client, string path, object body, HttpStatusCode status, string? schema = null)
    {
        using var response = await client.PutAsJsonAsync(path, body);
        return await ReadBody(response, status, schema);
    }

    public static async Task<JsonElement> DeleteJson(HttpClient client, string path, HttpStatusCode status, string? schema = null)
    {
        using var response = await client.DeleteAsync(path);
        return await ReadBody(response, status, schema);
    }

    private static async Task<JsonElement> ReadBody(HttpResponseMessage response, HttpStatusCode status, string? schema)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        if (schema is not null) ContractSchema.Assert(body, schema);
        return body;
    }

    public static async Task AssertNonEmptyList(HttpClient client, string path, string property)
    {
        var body = await GetJson(client, path, HttpStatusCode.OK);
        Assert.True(body.TryGetProperty(property, out var list), $"Expected contract collection '{property}' in {path}.");
        Assert.Equal(JsonValueKind.Array, list.ValueKind);
        Assert.NotEmpty(list.EnumerateArray());
    }

    public static Task RunSeedData(string connectionString)
    {
        using var db = NewDb(connectionString);
        SeedData.Run(db);
        return Task.CompletedTask;
    }

    public static async Task<int> TableCount(string connectionString, string table)
    {
        var counts = await TableCounts(connectionString, [table]);
        return counts[table];
    }

    public static async Task<Dictionary<string, int>> TableCounts(string connectionString, IEnumerable<string> tables)
    {
        await using var db = NewDb(connectionString);
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        var result = new Dictionary<string, int>();
        foreach (var table in tables)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
            result.Add(table, Convert.ToInt32(await command.ExecuteScalarAsync()));
        }
        return result;
    }

    public static async Task<Dictionary<string, string[]>> TableSnapshots(string connectionString, IEnumerable<string> tables)
    {
        await using var db = NewDb(connectionString);
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        var result = new Dictionary<string, string[]>();
        foreach (var table in tables)
        {
            var rows = new List<string>();
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT to_jsonb(t)::text FROM \"{table}\" AS t ORDER BY \"Id\"";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
            result.Add(table, rows.ToArray());
        }
        return result;
    }

    public static async Task<int> DuplicateGroups(string connectionString, string table, string columns)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM (SELECT {columns} FROM \"{table}\" GROUP BY {columns} HAVING COUNT(*) > 1) duplicate_rows";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public static async Task<bool> Exists(string connectionString, string query)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = query;
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    public static async Task<int> FindEmployeeId(string connectionString, string employeeCode)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"Id\" FROM \"Employees\" WHERE \"EmployeeId\" = @code";
        command.Parameters.AddWithValue("code", employeeCode);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public static async Task<EmployeeRow> ReadEmployee(string connectionString, int id)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"EmployeeId\", \"Name\", \"Email\", \"Department\", \"Role\", \"ParkingStatus\", \"AssignedFloor\" FROM \"Employees\" WHERE \"Id\" = @id";
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new EmployeeRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6));
    }

    public static async Task<EmployeeRow> ReadEmployeeByCode(string connectionString, string employeeCode)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT \"EmployeeId\", \"Name\", \"Email\", \"Department\", \"Role\", \"ParkingStatus\", \"AssignedFloor\" FROM \"Employees\" WHERE \"EmployeeId\" = @code";
        command.Parameters.AddWithValue("code", employeeCode);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new EmployeeRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6));
    }

    private static readonly DateTimeOffset FixedInstant = new(2025, 10, 14, 3, 0, 0, TimeSpan.Zero);

    public sealed record EmployeeRow(string EmployeeId, string Name, string Email, string Department, string Role, string ParkingStatus, string? AssignedFloor);

    public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    public sealed class FailConnectionInterceptor : DbConnectionInterceptor
    {
        public bool Fail { get; set; }

        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        {
            ThrowWhenEnabled();
            return result;
        }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            ThrowWhenEnabled();
            return ValueTask.FromResult(result);
        }

        private void ThrowWhenEnabled()
        {
            if (Fail) throw new NpgsqlException("connection to db.internal.invalid failed; SQL SELECT part4_secret; password=Part4Secret; connection refused");
        }
    }
}

internal sealed class Part4AuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Part4TestAuthentication";

    public Part4AuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, Microsoft.Extensions.Logging.ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var id = Request.Headers["X-Test-Employee-Id"].FirstOrDefault() ?? "1";
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, id), new("sub", id), new("employeeId", id), new("employee_id", id),
            new(ClaimTypes.Name, "Part Four Test User"),
            new(ClaimTypes.Role, "Administrator"), new(ClaimTypes.Role, "parkingAdministrator"),
            new(ClaimTypes.Role, "GateGuard"), new(ClaimTypes.Role, "gateGuard"), new(ClaimTypes.Role, "employee"),
            new("role", "Administrator"), new("role", "parkingAdministrator"), new("role", "GateGuard")
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}

internal static class ContractSchema
{
    private static readonly Dictionary<string, (string Name, string Type)[]> Required = new()
    {
        ["RegistrationResult"] = [("message", "string"), ("email", "string"), ("accountStatus", "string")],
        ["Session"] = [("accessToken", "string"), ("tokenType", "string"), ("expiresIn", "int"), ("employee", "schema:SessionEmployee")],
        ["SessionEmployee"] = [("id", "int"), ("name", "string"), ("email", "string"), ("role", "string"), ("parkingStatus", "string")],
        ["EmployeeProfile"] = [("employee", "schema:ProfileEmployee"), ("parking", "schema:ProfileParking"), ("vehicles", "array:Vehicle"), ("unavailableFields", "array:string"), ("retryableFields", "array:string")],
        ["ProfileEmployee"] = [("id", "int"), ("employeeId", "string"), ("name", "string"), ("email", "string"), ("department", "nullableString"), ("role", "nullableString")],
        ["ProfileParking"] = [("status", "string"), ("assignedFloor", "nullableString")],
        ["Vehicle"] = [("id", "int"), ("plate", "string"), ("make", "nullableString"), ("model", "nullableString"), ("color", "nullableString")],
        ["Dashboard"] = [("employee", "schema:DashboardEmployee"), ("officeDate", "string"), ("releases", "array:ReleaseRecord"), ("availability", "schema:Availability"), ("currentClaim", "nullable:ParkingClaim"), ("notice", "nullable:AvailabilityNotice"), ("unavailableFields", "array:string")],
        ["DashboardEmployee"] = [("id", "int"), ("name", "string"), ("email", "string"), ("parkingStatus", "string")],
        ["AvailabilityNotice"] = [("message", "string"), ("email", "string")],
        ["ReleaseRecord"] = [("id", "int"), ("date", "string"), ("status", "string"), ("area", "nullableString"), ("createdBy", "string"), ("createdAt", "string"), ("lastChangedBy", "string"), ("lastChangedAt", "string"), ("claimableAt", "string")],
        ["ReleaseList"] = [("releases", "array:ReleaseRecord")],
        ["ReleaseActionResult"] = [("message", "string"), ("release", "schema:ReleaseRecord")],
        ["Availability"] = [("date", "string"), ("count", "int"), ("areas", "array:AreaCount"), ("notYetClaimable", "array:ScheduledArea")],
        ["AreaCount"] = [("name", "string"), ("count", "int")],
        ["ScheduledArea"] = [("area", "string"), ("count", "int"), ("claimableAt", "string")],
        ["ParkingClaim"] = [("id", "int"), ("date", "string"), ("plate", "string"), ("area", "string"), ("status", "string"), ("createdBy", "string"), ("createdAt", "string"), ("lastChangedBy", "string"), ("lastChangedAt", "string")],
        ["GateFeed"] = [("cameraStatus", "string"), ("events", "array:Verification")],
        ["Verification"] = [("id", "int"), ("plate", "string"), ("driver", "string"), ("date", "string"), ("time", "string"), ("result", "string"), ("entryStatus", "string"), ("indicatorState", "string"), ("reason", "string"), ("permanentSticker", "bool")],
        ["VerificationList"] = [("records", "array:Verification"), ("total", "int")],
        ["Report"] = [("period", "string"), ("rows", "array:ReportRow"), ("dailyStatistics", "schema:ReportStatistics"), ("weeklyStatistics", "schema:ReportStatistics")],
        ["ReportRow"] = [("date", "string"), ("week", "string"), ("spacesReleased", "int"), ("temporaryClaims", "int"), ("utilizationPercent", "number")],
        ["ReportStatistics"] = [("releasedSpaces", "int"), ("temporaryClaims", "int"), ("utilizationPercent", "number")],
        ["UnusedParkingList"] = [("records", "array:UnusedParkingRecord"), ("total", "int")],
        ["UnusedParkingRecord"] = [("id", "int"), ("category", "string"), ("employee", "string"), ("vehicle", "string"), ("date", "string")],
        ["EmployeeList"] = [("employees", "array:Employee"), ("total", "int")],
        ["EmployeeRecord"] = [("employee", "schema:Employee")],
        ["Employee"] = [("id", "int"), ("employeeId", "string"), ("name", "string"), ("email", "string"), ("department", "string"), ("role", "string"), ("parkingStatus", "string"), ("assignedFloor", "nullableString"), ("vehicles", "array:EmployeeVehicle"), ("datedActivity", "array:DatedActivity")],
        ["EmployeeVehicle"] = [("plate", "string"), ("make", "nullableString"), ("model", "nullableString"), ("color", "nullableString")],
        ["DatedActivity"] = [("date", "string"), ("kind", "string"), ("status", "string")],
        ["ParkingSettings"] = [("dailyReleaseTime", "string"), ("areas", "array:ParkingArea"), ("reportRecipients", "array:email")],
        ["ParkingArea"] = [("name", "string")],
        ["AdministratorList"] = [("administrators", "array:Administrator"), ("total", "int")],
        ["Administrator"] = [("id", "int"), ("name", "string"), ("email", "string"), ("accessStatus", "string")],
        ["AdminAccessResult"] = [("employee", "schema:Administrator"), ("message", "string")],
        ["DirectorySearchResult"] = [("employees", "array:DirectoryEmployee"), ("total", "int")],
        ["DirectoryEmployee"] = [("id", "int"), ("name", "string"), ("email", "string")]
    };

    public static void Assert(JsonElement element, string schema) => Validate(element, schema);

    private static void Validate(JsonElement element, string schema)
    {
        Xunit.Assert.Equal(JsonValueKind.Object, element.ValueKind);
        if (!Required.TryGetValue(schema, out var fields)) throw new InvalidOperationException($"No contract shape registered for {schema}.");
        foreach (var (name, type) in fields)
        {
            Xunit.Assert.True(element.TryGetProperty(name, out var value), $"{schema} is missing required property '{name}'.");
            ValidateType(value, type, $"{schema}.{name}");
            ValidateFormat(value, FormatFor(schema, name), $"{schema}.{name}");
            var allowed = AllowedValues(schema, name);
            if (allowed is not null) Xunit.Assert.Contains(value.GetString(), allowed);
        }
    }

    private static void ValidateType(JsonElement value, string type, string path)
    {
        if (type.StartsWith("schema:", StringComparison.Ordinal))
        {
            Validate(value, type[7..]);
            return;
        }
        if (type.StartsWith("nullable:", StringComparison.Ordinal))
        {
            if (value.ValueKind != JsonValueKind.Null) Validate(value, type[9..]);
            return;
        }
        if (type.StartsWith("array:", StringComparison.Ordinal))
        {
            Xunit.Assert.Equal(JsonValueKind.Array, value.ValueKind);
            var elementType = type[6..];
            foreach (var item in value.EnumerateArray())
            {
                if (elementType == "string") Xunit.Assert.Equal(JsonValueKind.String, item.ValueKind);
                else if (elementType == "email")
                {
                    Xunit.Assert.Equal(JsonValueKind.String, item.ValueKind);
                    ValidateFormat(item, "email", path);
                }
                else Validate(item, elementType);
            }
            return;
        }

        var expected = type switch
        {
            "string" => JsonValueKind.String,
            "nullableString" => value.ValueKind,
            "int" or "number" => JsonValueKind.Number,
            "bool" => value.ValueKind,
            _ => throw new InvalidOperationException($"Unknown schema type '{type}' at {path}.")
        };
        if (type == "nullableString") Xunit.Assert.True(value.ValueKind is JsonValueKind.Null or JsonValueKind.String, $"{path} must be a string or null.");
        else if (type == "int") Xunit.Assert.True(value.TryGetInt32(out _), $"{path} must be an integer.");
        else if (type == "bool") Xunit.Assert.True(value.ValueKind is JsonValueKind.True or JsonValueKind.False, $"{path} must be boolean.");
        else Xunit.Assert.Equal(expected, value.ValueKind);
    }

    private static string[]? AllowedValues(string schema, string property) => (schema, property) switch
    {
        ("RegistrationResult", "accountStatus") => ["created"],
        ("SessionEmployee", "role") => ["employee", "gateGuard", "parkingAdministrator"],
        ("SessionEmployee", "parkingStatus") => ["dedicated", "temporary"],
        ("ProfileParking", "status") => ["dedicated", "temporary"],
        ("DashboardEmployee", "parkingStatus") => ["dedicated", "temporary"],
        ("ReleaseRecord", "status") => ["Reserved", "Released", "Claimed"],
        ("ParkingClaim", "status") => ["Claimed"],
        ("GateFeed", "cameraStatus") => ["online", "unavailable"],
        ("Verification", "result") => ["Authorised", "Existing process", "Denied"],
        ("Verification", "indicatorState") => ["green", "off", "not used"],
        ("Report", "period") => ["daily", "weekly"],
        ("UnusedParkingRecord", "category") => ["Unused Dedicated Parking", "Unused Temporary Parking"],
        ("Employee", "parkingStatus") => ["Dedicated", "Temporary"],
        ("DatedActivity", "kind") => ["Release", "Claim"],
        ("DatedActivity", "status") => ["Reserved", "Released", "Claimed"],
        ("Administrator", "accessStatus") => ["active", "revoked"],
        _ => null
    };

    private static string? FormatFor(string schema, string property) => (schema, property) switch
    {
        (_, "email") => "email",
        (_, "officeDate") => "date",
        (_, "date") => "date",
        (_, "dailyReleaseTime") => "time",
        (_, "time") => "time",
        (_, "createdAt") => "date-time",
        (_, "lastChangedAt") => "date-time",
        (_, "claimableAt") => "date-time",
        _ => null
    };

    private static void ValidateFormat(JsonElement value, string? format, string path)
    {
        if (format is null) return;
        var text = value.GetString();
        var valid = format switch
        {
            "email" => text is not null && System.Net.Mail.MailAddress.TryCreate(text, out var address) && address.Address == text,
            "date" => DateOnly.TryParseExact(text, "yyyy-MM-dd", out _),
            "time" => TimeOnly.TryParse(text, out _),
            "date-time" => DateTimeOffset.TryParse(text, out _),
            _ => false
        };
        Xunit.Assert.True(valid, $"{path} does not match the OpenAPI {format} format.");
    }
}

