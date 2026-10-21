using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using App.Tests;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace App.Tests.Part3;

public sealed class AcceptancePart3Tests : IClassFixture<AppFactory>
{
    private readonly AppFactory _factory;

    public AcceptancePart3Tests(AppFactory factory) => _factory = factory;

    [Fact]
    public async Task AC_9_1_FiltersEmployeesBySearchAndParkingStatus()
    {
        using var app = Restarted();
        using var client = CreateClient(app);
        await InsertEmployeeAsync(app, 91101, "EMP-P3-91101", "Needle Person One", "needle.one.part3@example.test", "Dedicated", "North");
        await InsertEmployeeAsync(app, 91102, "EMP-P3-91102", "Needle Person Two", "needle.two.part3@example.test", "Temporary", null);
        await InsertEmployeeAsync(app, 91103, "EMP-P3-91103", "Outside Person", "outside.part3@example.test", "Dedicated", "South");

        using var bothFilters = await client.GetAsync("/admin/employees?search=Needle&parkingStatus=Dedicated");
        Assert.Equal(HttpStatusCode.OK, bothFilters.StatusCode);
        var both = await bothFilters.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(1, both.GetProperty("total").GetInt32());
        Assert.Equal(new[] { "EMP-P3-91101" }, EmployeeIds(both));

        using var searchOnly = await client.GetAsync("/admin/employees?search=Needle");
        Assert.Equal(HttpStatusCode.OK, searchOnly.StatusCode);
        var searched = await searchOnly.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(2, searched.GetProperty("total").GetInt32());
        Assert.True(new HashSet<string> { "EMP-P3-91101", "EMP-P3-91102" }.SetEquals(EmployeeIds(searched)));

        using var statusOnly = await client.GetAsync("/admin/employees?parkingStatus=Dedicated");
        Assert.Equal(HttpStatusCode.OK, statusOnly.StatusCode);
        var dedicated = await statusOnly.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(2, dedicated.GetProperty("total").GetInt32());
        Assert.True(new HashSet<string> { "EMP-P3-91101", "EMP-P3-91103" }.SetEquals(EmployeeIds(dedicated)));
    }

    [Fact]
    public async Task AC_9_2_CreatesEmployeeAndReturnsDocumentedRecordFields()
    {
        using var app = Restarted();
        using var client = CreateClient(app);
        var input = EmployeeInput("EMP-P3-CREATE-912", "Create Person", "create.person.part3@example.test", "Operations", "Coordinator", "Temporary", null,
            new object[]
            {
                new { plate = "P3-CREATE-912-A", make = "Honda", model = "City", color = "White" },
                new { plate = "P3-CREATE-912-B", make = "Toyota", model = "Yaris", color = "Silver" },
                new { plate = "P3-CREATE-912-C", make = "Kia", model = "Rio", color = "Blue" }
            });

        using var response = await client.PostAsJsonAsync("/admin/employees", input);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var root = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var employee = root.GetProperty("employee");
        Assert.True(employee.GetProperty("id").GetInt32() > 0);
        Assert.Equal("EMP-P3-CREATE-912", employee.GetProperty("employeeId").GetString());
        Assert.Equal("Create Person", employee.GetProperty("name").GetString());
        Assert.Equal("create.person.part3@example.test", employee.GetProperty("email").GetString());
        Assert.Equal("Operations", employee.GetProperty("department").GetString());
        Assert.Equal("Coordinator", employee.GetProperty("role").GetString());
        Assert.Equal("Temporary", employee.GetProperty("parkingStatus").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, employee.GetProperty("assignedFloor").ValueKind);
        var vehicles = employee.GetProperty("vehicles").EnumerateArray().ToArray();
        Assert.Equal(3, vehicles.Length);
        Assert.True(new HashSet<string>
        {
            "P3-CREATE-912-A|Honda|City|White",
            "P3-CREATE-912-B|Toyota|Yaris|Silver",
            "P3-CREATE-912-C|Kia|Rio|Blue"
        }.SetEquals(vehicles.Select(vehicle => $"{vehicle.GetProperty("plate").GetString()}|{vehicle.GetProperty("make").GetString()}|{vehicle.GetProperty("model").GetString()}|{vehicle.GetProperty("color").GetString()}")));
        Assert.Empty(employee.GetProperty("datedActivity").EnumerateArray());
    }

    [Fact]
    public async Task AC_9_3_ReturnsConflictForDuplicateEmployeeIdentity()
    {
        using var app = Restarted();
        using var client = CreateClient(app);
        await InsertEmployeeAsync(app, 93001, "EMP-P3-DUPLICATE", "Existing Person", "existing.part3@example.test");
        var input = EmployeeInput("EMP-P3-DUPLICATE", "Different Person", "different.part3@example.test", "Support", "Analyst", "Temporary", null);

        using var response = await client.PostAsJsonAsync("/admin/employees", input);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AC_9_4_ReturnsEmployeeVehiclesAndDatedActivity()
    {
        using var app = Restarted();
        using var client = CreateClient(app);
        await InsertEmployeeAsync(app, 94001, "EMP-P3-DETAIL", "Detail Person", "detail.part3@example.test", "Dedicated", "West", "Engineering", "Principal Engineer");
        await InsertVehicleAsync(app, 94001, 94001, "P3-DETAIL-914-A", "Volvo", "XC40", "Blue");
        await InsertVehicleAsync(app, 94002, 94001, "P3-DETAIL-914-B", "Audi", "Q3", "Gray");
        await InsertVehicleAsync(app, 94003, 94001, "P3-DETAIL-914-C", "BMW", "X1", "Red");
        await InsertReleaseAsync(app, 94001, 94001, new DateOnly(2026, 3, 5));
        await InsertReleaseAsync(app, 94002, 94001, new DateOnly(2026, 3, 7));
        await InsertClaimAsync(app, 94001, 94001, new DateOnly(2026, 3, 6), "P3-DETAIL-914-A");

        using var response = await client.GetAsync("/admin/employees/94001");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var employee = root.GetProperty("employee");
        Assert.Equal(94001, employee.GetProperty("id").GetInt32());
        Assert.Equal("EMP-P3-DETAIL", employee.GetProperty("employeeId").GetString());
        Assert.Equal("Detail Person", employee.GetProperty("name").GetString());
        Assert.Equal("detail.part3@example.test", employee.GetProperty("email").GetString());
        Assert.Equal("Engineering", employee.GetProperty("department").GetString());
        Assert.Equal("Principal Engineer", employee.GetProperty("role").GetString());
        Assert.Equal("Dedicated", employee.GetProperty("parkingStatus").GetString());
        Assert.Equal("West", employee.GetProperty("assignedFloor").GetString());
        var vehicles = employee.GetProperty("vehicles").EnumerateArray().ToArray();
        Assert.Equal(3, vehicles.Length);
        Assert.True(new HashSet<string>
        {
            "P3-DETAIL-914-A|Volvo|XC40|Blue",
            "P3-DETAIL-914-B|Audi|Q3|Gray",
            "P3-DETAIL-914-C|BMW|X1|Red"
        }.SetEquals(vehicles.Select(vehicle => $"{vehicle.GetProperty("plate").GetString()}|{vehicle.GetProperty("make").GetString()}|{vehicle.GetProperty("model").GetString()}|{vehicle.GetProperty("color").GetString()}")));
        var activity = employee.GetProperty("datedActivity").EnumerateArray().ToArray();
        Assert.Equal(3, activity.Length);
        Assert.Contains(activity, item => item.GetProperty("date").GetString() == "2026-03-05" &&
            item.GetProperty("kind").GetString() == "Release" && item.GetProperty("status").GetString() == "Released");
        Assert.Contains(activity, item => item.GetProperty("date").GetString() == "2026-03-06" &&
            item.GetProperty("kind").GetString() == "Claim" && item.GetProperty("status").GetString() == "Claimed");
        Assert.Contains(activity, item => item.GetProperty("date").GetString() == "2026-03-07" &&
            item.GetProperty("kind").GetString() == "Release" && item.GetProperty("status").GetString() == "Released");
    }

    [Fact]
    public async Task AC_9_5_ReturnsNotFoundCodeForMissingEmployee()
    {
        using var app = Restarted();
        using var client = CreateClient(app);

        using var response = await client.GetAsync("/admin/employees/91599");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("NOT_FOUND", root.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC_9_6_UpdatesExistingEmployeeAndReturnsSubmittedValues()
    {
        using var app = Restarted();
        using var client = CreateClient(app);
        await InsertEmployeeAsync(app, 96001, "EMP-P3-UPDATE", "Old Person", "old.part3@example.test", "Dedicated", "East");
        await InsertVehicleAsync(app, 96001, 96001, "P3-OLD-916", "Ford", "Focus", "Black");
        var input = EmployeeInput("EMP-P3-UPDATE", "Updated Person", "updated.part3@example.test", "Finance", "Manager", "Temporary", null,
            new object[]
            {
                new { plate = "P3-NEW-916-A", make = "Mazda", model = "3", color = "Silver" },
                new { plate = "P3-NEW-916-B", make = "Hyundai", model = "Elantra", color = "White" },
                new { plate = "P3-NEW-916-C", make = "Nissan", model = "Sentra", color = "Blue" }
            });

        using var response = await client.PutAsJsonAsync("/admin/employees/96001", input);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var employee = root.GetProperty("employee");
        Assert.Equal(96001, employee.GetProperty("id").GetInt32());
        Assert.Equal("EMP-P3-UPDATE", employee.GetProperty("employeeId").GetString());
        Assert.Equal("Updated Person", employee.GetProperty("name").GetString());
        Assert.Equal("updated.part3@example.test", employee.GetProperty("email").GetString());
        Assert.Equal("Finance", employee.GetProperty("department").GetString());
        Assert.Equal("Manager", employee.GetProperty("role").GetString());
        Assert.Equal("Temporary", employee.GetProperty("parkingStatus").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, employee.GetProperty("assignedFloor").ValueKind);
        var vehicles = employee.GetProperty("vehicles").EnumerateArray().ToArray();
        Assert.Equal(3, vehicles.Length);
        Assert.True(new HashSet<string>
        {
            "P3-NEW-916-A|Mazda|3|Silver",
            "P3-NEW-916-B|Hyundai|Elantra|White",
            "P3-NEW-916-C|Nissan|Sentra|Blue"
        }.SetEquals(vehicles.Select(vehicle => $"{vehicle.GetProperty("plate").GetString()}|{vehicle.GetProperty("make").GetString()}|{vehicle.GetProperty("model").GetString()}|{vehicle.GetProperty("color").GetString()}")));
    }

    [Fact]
    public async Task AC_9_7_ReturnsNotFoundCodeWhenUpdatingMissingEmployee()
    {
        using var app = Restarted();
        using var client = CreateClient(app);
        var input = EmployeeInput("EMP-P3-MISSING-917", "Missing Person", "missing.part3@example.test", "Support", "Analyst", "Temporary", null);

        using var response = await client.PutAsJsonAsync("/admin/employees/91799", input);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("NOT_FOUND", root.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC_9_8_ReturnsCurrentParkingConfiguration()
    {
        using var app = Restarted();
        using var client = CreateClient(app);
        var settings = Settings("07:45:00", "Current North", "Current South", "Current West",
            "current.north.part3@example.test", "current.south.part3@example.test", "current.west.part3@example.test");
        using var setup = await client.PutAsJsonAsync("/admin/settings", settings);
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);

        using var response = await client.GetAsync("/admin/settings");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        AssertTime("07:45:00", root.GetProperty("dailyReleaseTime").GetString());
        Assert.True(new HashSet<string> { "Current North", "Current South", "Current West" }.SetEquals(
            root.GetProperty("areas").EnumerateArray().Select(area => area.GetProperty("name").GetString()!)));
        Assert.True(new HashSet<string> { "current.north.part3@example.test", "current.south.part3@example.test", "current.west.part3@example.test" }.SetEquals(
            root.GetProperty("reportRecipients").EnumerateArray().Select(email => email.GetString()!)));
    }

    [Fact]
    public async Task AC_9_9_SavesAndReturnsAllParkingSettingsValues()
    {
        using var app = Restarted();
        using var client = CreateClient(app);
        var settings = Settings("08:35:00", "East Lot", "West Lot", "Visitor Lot",
            "east.reports.part3@example.test", "west.reports.part3@example.test", "visitor.reports.part3@example.test");

        using var response = await client.PutAsJsonAsync("/admin/settings", settings);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        AssertTime("08:35:00", root.GetProperty("dailyReleaseTime").GetString());
        Assert.True(new HashSet<string> { "East Lot", "West Lot", "Visitor Lot" }.SetEquals(
            root.GetProperty("areas").EnumerateArray().Select(area => area.GetProperty("name").GetString()!)));
        Assert.True(new HashSet<string> { "east.reports.part3@example.test", "west.reports.part3@example.test", "visitor.reports.part3@example.test" }.SetEquals(
            root.GetProperty("reportRecipients").EnumerateArray().Select(email => email.GetString()!)));
    }

    [Fact]
    public async Task AC_9_10_ReturnsBadRequestCodeForInvalidParkingConfiguration()
    {
        using var app = Restarted();
        using var client = CreateClient(app);
        var invalid = new
        {
            dailyReleaseTime = "08:35:00",
            areas = Array.Empty<object>(),
            reportRecipients = new[] { "valid.part3@example.test" }
        };

        using var response = await client.PutAsJsonAsync("/admin/settings", invalid);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("BAD_REQUEST", root.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC_9_11_ListsActiveAndRevokedAdministratorsWithTotal()
    {
        using var app = Restarted();
        using var client = CreateClient(app);
        await InsertEmployeeAsync(app, 911101, "EMP-P3-ADMIN-A", "Admin Active One", "admin.active.one.part3@example.test");
        await InsertEmployeeAsync(app, 911102, "EMP-P3-ADMIN-B", "Admin Revoked", "admin.revoked.part3@example.test");
        await InsertEmployeeAsync(app, 911103, "EMP-P3-ADMIN-C", "Admin Active Two", "admin.active.two.part3@example.test");
        await InsertAdministratorAccessAsync(app, 911101, 911101, "active");
        await InsertAdministratorAccessAsync(app, 911102, 911102, "revoked");
        await InsertAdministratorAccessAsync(app, 911103, 911103, "active");

        using var response = await client.GetAsync("/admin/admins");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var administrators = root.GetProperty("administrators").EnumerateArray().ToArray();
        Assert.Equal(await CountAdministratorAccessAsync(app), root.GetProperty("total").GetInt32());
        Assert.Equal("active", FindAdministrator(administrators, 911101).GetProperty("accessStatus").GetString());
        Assert.Equal("Admin Active One", FindAdministrator(administrators, 911101).GetProperty("name").GetString());
        Assert.Equal("admin.active.one.part3@example.test", FindAdministrator(administrators, 911101).GetProperty("email").GetString());
        Assert.Equal("revoked", FindAdministrator(administrators, 911102).GetProperty("accessStatus").GetString());
        Assert.Equal("Admin Revoked", FindAdministrator(administrators, 911102).GetProperty("name").GetString());
        Assert.Equal("admin.revoked.part3@example.test", FindAdministrator(administrators, 911102).GetProperty("email").GetString());
        Assert.Equal("active", FindAdministrator(administrators, 911103).GetProperty("accessStatus").GetString());
        Assert.Equal("Admin Active Two", FindAdministrator(administrators, 911103).GetProperty("name").GetString());
        Assert.Equal("admin.active.two.part3@example.test", FindAdministrator(administrators, 911103).GetProperty("email").GetString());
    }

    [Fact]
    public async Task AC_9_12_GrantsAdministratorAccessToEligibleEmployee()
    {
        using var app = Restarted();
        using var client = CreateClient(app);
        await InsertEmployeeAsync(app, 91201, "EMP-P3-GRANT", "Grant Person", "grant.person.part3@example.test");

        using var response = await client.PostAsJsonAsync("/admin/admins", new { employeeId = 91201 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var employee = root.GetProperty("employee");
        Assert.Equal(91201, employee.GetProperty("id").GetInt32());
        Assert.Equal("Grant Person", employee.GetProperty("name").GetString());
        Assert.Equal("grant.person.part3@example.test", employee.GetProperty("email").GetString());
        Assert.Equal("active", employee.GetProperty("accessStatus").GetString());
        Assert.Equal("Grant Person is now an administrator.", root.GetProperty("message").GetString());
    }

    [Fact]
    public async Task AC_9_13_ReturnsConflictWhenAdministratorAccessIsAlreadyActive()
    {
        using var app = Restarted();
        using var client = CreateClient(app);
        await InsertEmployeeAsync(app, 91301, "EMP-P3-ALREADY-ADMIN", "Already Admin", "already.admin.part3@example.test");
        await InsertAdministratorAccessAsync(app, 91301, 91301, "active");

        using var response = await client.PostAsJsonAsync("/admin/admins", new { employeeId = 91301 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AC_9_14_SearchesDirectoryAndReturnsMatchingEmployeesAndTotal()
    {
        using var app = Restarted();
        using var client = CreateClient(app);
        await InsertEmployeeAsync(app, 91401, "EMP-P3-DIR-1", "DirectoryNeedle Person One", "directory.one.part3@example.test");
        await InsertEmployeeAsync(app, 91402, "EMP-P3-DIR-2", "DirectoryNeedle Person Two", "directory.two.part3@example.test");
        await InsertEmployeeAsync(app, 91403, "EMP-P3-DIR-3", "Different Person", "different.directory.part3@example.test");

        using var response = await client.GetAsync("/admin/admins/directory?search=DirectoryNeedle");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(2, root.GetProperty("total").GetInt32());
        var employees = root.GetProperty("employees").EnumerateArray().ToArray();
        Assert.Equal(2, employees.Length);
        Assert.True(new HashSet<int> { 91401, 91402 }.SetEquals(employees.Select(employee => employee.GetProperty("id").GetInt32())));
        Assert.Equal("DirectoryNeedle Person One", FindDirectoryEmployee(employees, 91401).GetProperty("name").GetString());
        Assert.Equal("directory.one.part3@example.test", FindDirectoryEmployee(employees, 91401).GetProperty("email").GetString());
        Assert.Equal("DirectoryNeedle Person Two", FindDirectoryEmployee(employees, 91402).GetProperty("name").GetString());
        Assert.Equal("directory.two.part3@example.test", FindDirectoryEmployee(employees, 91402).GetProperty("email").GetString());
    }

    [Fact]
    public async Task AC_9_15_RevokesAdministratorAccessAndReturnsRevokedEmployee()
    {
        using var app = Restarted();
        using var client = CreateClient(app);
        await InsertEmployeeAsync(app, 91501, "EMP-P3-REVOKE", "Revoke Person", "revoke.person.part3@example.test");
        await InsertAdministratorAccessAsync(app, 91501, 91501, "active");

        using var response = await client.DeleteAsync("/admin/admins/91501");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var employee = root.GetProperty("employee");
        Assert.Equal(91501, employee.GetProperty("id").GetInt32());
        Assert.Equal("Revoke Person", employee.GetProperty("name").GetString());
        Assert.Equal("revoke.person.part3@example.test", employee.GetProperty("email").GetString());
        Assert.Equal("revoked", employee.GetProperty("accessStatus").GetString());
        Assert.Equal("Access removed for Revoke Person.", root.GetProperty("message").GetString());

        using var listResponse = await client.GetAsync("/admin/admins");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("revoked", FindAdministrator(list.GetProperty("administrators").EnumerateArray().ToArray(), 91501)
            .GetProperty("accessStatus").GetString());
    }

    [Fact]
    public async Task AC_9_16_ReturnsNotFoundCodeWhenEmployeeHasNoAdministratorRecord()
    {
        using var app = Restarted();
        using var client = CreateClient(app);
        await InsertEmployeeAsync(app, 91601, "EMP-P3-NO-ADMIN", "No Admin Person", "no.admin.part3@example.test");

        using var response = await client.DeleteAsync("/admin/admins/91601");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var root = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("NOT_FOUND", root.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AC_9_17_ReturnsDocumentedErrorsForInvalidAndConflictingRequests()
    {
        using var app = Restarted();
        using var client = CreateClient(app);
        var invalidEmployee = EmployeeInput("EMP-P3-INVALID", "Invalid Person", "not-an-email", "Support", "Analyst", "Temporary", null);

        using var invalidResponse = await client.PostAsJsonAsync("/admin/employees", invalidEmployee);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        var invalidError = await invalidResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("The submitted values are not valid.", invalidError.GetProperty("message").GetString());
        Assert.Equal("BAD_REQUEST", invalidError.GetProperty("code").GetString());

        await InsertEmployeeAsync(app, 91701, "EMP-P3-CONFLICT-ADMIN", "Conflict Admin", "conflict.admin.part3@example.test");
        await InsertAdministratorAccessAsync(app, 91701, 91701, "active");
        using var conflictResponse = await client.PostAsJsonAsync("/admin/admins", new { employeeId = 91701 });
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
        var conflictError = await conflictResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(conflictError.GetProperty("message").GetString()));
        Assert.Equal("CONFLICT", conflictError.GetProperty("code").GetString());
    }

    private AppFactory Restarted() => _factory.Restarted();

    private static Part3AdminClient CreateClient(AppFactory app)
    {
        var host = app.WithWebHostBuilder(ConfigureTestAuthentication);
        return new Part3AdminClient(host, host.CreateClient());
    }

    private static void ConfigureTestAuthentication(IWebHostBuilder builder) => builder.ConfigureTestServices(services =>
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "Part3Test";
            options.DefaultChallengeScheme = "Part3Test";
            options.DefaultForbidScheme = "Part3Test";
        }).AddScheme<AuthenticationSchemeOptions, Part3AdminAuthenticationHandler>("Part3Test", _ => { }));

    private static object EmployeeInput(string employeeId, string name, string email, string department, string role,
        string parkingStatus, string? assignedFloor, object[]? vehicles = null) => new
    {
        employeeId,
        name,
        email,
        department,
        role,
        parkingStatus,
        assignedFloor,
        vehicles = vehicles ?? Array.Empty<object>()
    };

    private static object Settings(string dailyReleaseTime, string areaOne, string areaTwo, string areaThree,
        string recipientOne, string recipientTwo, string recipientThree) => new
    {
        dailyReleaseTime,
        areas = new[] { new { name = areaOne }, new { name = areaTwo }, new { name = areaThree } },
        reportRecipients = new[] { recipientOne, recipientTwo, recipientThree }
    };

    private static string[] EmployeeIds(System.Text.Json.JsonElement root) =>
        root.GetProperty("employees").EnumerateArray().Select(employee => employee.GetProperty("employeeId").GetString()!).ToArray();

    private static System.Text.Json.JsonElement FindAdministrator(System.Text.Json.JsonElement[] administrators, int id) =>
        Assert.Single(administrators, employee => employee.GetProperty("id").GetInt32() == id);

    private static System.Text.Json.JsonElement FindDirectoryEmployee(System.Text.Json.JsonElement[] employees, int id) =>
        Assert.Single(employees, employee => employee.GetProperty("id").GetInt32() == id);

    private static void AssertTime(string expected, string? actual)
    {
        Assert.True(TimeOnly.TryParse(actual, out var parsed), $"Expected a valid time value but received '{actual}'.");
        Assert.Equal(TimeOnly.Parse(expected), parsed);
    }

    private static async Task InsertEmployeeAsync(AppFactory app, int id, string employeeId, string name, string email,
        string parkingStatus = "Temporary", string? assignedFloor = null, string department = "Operations", string role = "Specialist")
    {
        await using var connection = new NpgsqlConnection(app.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO "Employees" ("Id", "EmployeeId", "Name", "Email", "Department", "Role", "ParkingStatus", "AssignedFloor")
            VALUES (@id, @employeeId, @name, @email, @department, @role, @parkingStatus, @assignedFloor)
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("employeeId", employeeId);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("department", department);
        command.Parameters.AddWithValue("role", role);
        command.Parameters.AddWithValue("parkingStatus", parkingStatus);
        command.Parameters.AddWithValue("assignedFloor", (object?)assignedFloor ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertVehicleAsync(AppFactory app, int id, int employeeId, string plate, string make, string model, string color)
    {
        await using var connection = new NpgsqlConnection(app.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO "Vehicles" ("Id", "EmployeeId", "Plate", "Make", "Model", "Color")
            VALUES (@id, @employeeId, @plate, @make, @model, @color)
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("employeeId", employeeId);
        command.Parameters.AddWithValue("plate", plate);
        command.Parameters.AddWithValue("make", make);
        command.Parameters.AddWithValue("model", model);
        command.Parameters.AddWithValue("color", color);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertReleaseAsync(AppFactory app, int id, int employeeId, DateOnly date)
    {
        await using var connection = new NpgsqlConnection(app.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO "ParkingReleases" ("Id", "EmployeeId", "Date", "Status", "Area", "CreatedByEmployeeId", "CreatedAt",
                "LastChangedByEmployeeId", "LastChangedAt", "ClaimableAt", "IsCancelled", "CancellationReason")
            VALUES (@id, @employeeId, @date, 'Released', 'North', @employeeId, @changedAt,
                @employeeId, @changedAt, @claimableAt, FALSE, NULL)
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("employeeId", employeeId);
        command.Parameters.AddWithValue("date", date);
        command.Parameters.AddWithValue("changedAt", new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc));
        command.Parameters.AddWithValue("claimableAt", new DateTime(2026, 3, 5, 8, 0, 0, DateTimeKind.Utc));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertClaimAsync(AppFactory app, int id, int employeeId, DateOnly date, string plate)
    {
        await using var connection = new NpgsqlConnection(app.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO "ParkingClaims" ("Id", "EmployeeId", "ReleaseId", "Date", "VehiclePlate", "Area", "Status",
                "CreatedByEmployeeId", "CreatedAt", "LastChangedByEmployeeId", "LastChangedAt")
            VALUES (@id, @employeeId, NULL, @date, @plate, 'South', 'Claimed', @employeeId, @changedAt, @employeeId, @changedAt)
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("employeeId", employeeId);
        command.Parameters.AddWithValue("date", date);
        command.Parameters.AddWithValue("plate", plate);
        command.Parameters.AddWithValue("changedAt", new DateTime(2026, 3, 2, 9, 0, 0, DateTimeKind.Utc));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertAdministratorAccessAsync(AppFactory app, int id, int employeeId, string status)
    {
        await using var connection = new NpgsqlConnection(app.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO "AdministratorAccess" ("Id", "EmployeeId", "AccessStatus", "ChangedByEmployeeId", "ChangedAt")
            VALUES (@id, @employeeId, @status, @employeeId, @changedAt)
            """, connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("employeeId", employeeId);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("changedAt", new DateTime(2026, 3, 3, 8, 0, 0, DateTimeKind.Utc));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountAdministratorAccessAsync(AppFactory app)
    {
        await using var connection = new NpgsqlConnection(app.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM \"AdministratorAccess\"", connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }
}

internal sealed class Part3AdminClient(WebApplicationFactory<Program> host, HttpClient client) : IDisposable
{
    public Task<HttpResponseMessage> GetAsync(string path) => client.GetAsync(path);
    public Task<HttpResponseMessage> DeleteAsync(string path) => client.DeleteAsync(path);
    public Task<HttpResponseMessage> PostAsJsonAsync(string path, object body) => client.PostAsJsonAsync(path, body);
    public Task<HttpResponseMessage> PutAsJsonAsync(string path, object body) => client.PutAsJsonAsync(path, body);

    public void Dispose()
    {
        client.Dispose();
        host.Dispose();
    }
}

internal sealed class Part3AdminAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public Part3AdminAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        UrlEncoder encoder) : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "part3-admin"),
            new Claim(ClaimTypes.Name, "Part 3 Test Administrator"),
            new Claim(ClaimTypes.Role, "Administrator"),
            new Claim(ClaimTypes.Role, "admin"),
            new Claim("role", "administrator")
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name, ClaimTypes.Name, ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}
