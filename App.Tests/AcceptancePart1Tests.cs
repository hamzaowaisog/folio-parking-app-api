using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using App.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace App.Tests.Part1;

public sealed class AcceptancePart1Tests : IClassFixture<AppFactory>
{
    private const string BadRequestMessage = "The submitted values are not valid.";
    private const string UnauthorizedMessage = "Sign in to continue.";
    private const string ForbiddenMessage = "You do not have access to this page.";
    private const string ConflictMessage = "No space could be claimed. The last free space was taken just now.";
    private const string ServiceUnavailableMessage = "Verification is not available right now.";

    private readonly AppFactory _factory;

    public AcceptancePart1Tests(AppFactory factory) => _factory = factory;

    [Fact]
    public async Task AC_1_1_RegistersAnAccountAndReturnsCreatedStatus()
    {
        using var app = _factory.Restarted();
        using var client = app.CreateClient();
        var account = NewAccount();

        using var response = await SendJsonAsync(client, HttpMethod.Post, "/auth/register", new
        {
            name = account.Name,
            email = account.Email,
            password = account.Password
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal(account.Email, body.RootElement.GetProperty("email").GetString());
        Assert.Equal("created", body.RootElement.GetProperty("accountStatus").GetString());
    }

    [Fact]
    public async Task AC_1_2_ReportsConflictForAnAlreadyRegisteredEmail()
    {
        using var app = _factory.Restarted();
        using var client = app.CreateClient();
        var account = NewAccount();
        await RegisterAsync(client, account);

        using var response = await SendJsonAsync(client, HttpMethod.Post, "/auth/register", new
        {
            name = account.Name,
            email = account.Email,
            password = account.Password
        });

        await AssertErrorAsync(response, HttpStatusCode.Conflict, ConflictMessage, "CONFLICT");
    }

    [Fact]
    public async Task AC_1_3_RejectsInvalidEmailAndMissingRequiredValue()
    {
        using var app = _factory.Restarted();
        using var client = app.CreateClient();
        var invalidEmail = NewAccount();

        using (var response = await SendJsonAsync(client, HttpMethod.Post, "/auth/register", new
        {
            name = invalidEmail.Name,
            email = "not-an-email",
            password = invalidEmail.Password
        }))
        {
            await AssertErrorAsync(response, HttpStatusCode.BadRequest, BadRequestMessage, "BAD_REQUEST");
        }

        using (var response = await SendJsonAsync(client, HttpMethod.Post, "/auth/register", new
        {
            name = invalidEmail.Name,
            email = invalidEmail.Email
        }))
        {
            await AssertErrorAsync(response, HttpStatusCode.BadRequest, BadRequestMessage, "BAD_REQUEST");
        }
    }

    [Fact]
    public async Task AC_2_1_ReturnsBearerSessionAndEmployeeForValidCredentials()
    {
        using var app = _factory.Restarted();
        using var client = app.CreateClient();
        var account = NewAccount();
        await RegisterAsync(client, account);

        using var response = await SendJsonAsync(client, HttpMethod.Post, "/auth/login", new
        {
            email = account.Email,
            password = account.Password
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        var root = body.RootElement;
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("accessToken").GetString()));
        Assert.Equal("Bearer", root.GetProperty("tokenType").GetString());
        Assert.True(root.GetProperty("expiresIn").GetInt32() > 0);
        var employee = root.GetProperty("employee");
        Assert.True(employee.GetProperty("id").GetInt32() > 0);
        Assert.Equal(account.Name, employee.GetProperty("name").GetString());
        Assert.Equal(account.Email, employee.GetProperty("email").GetString());
        Assert.Equal("employee", employee.GetProperty("role").GetString());
        Assert.Contains(employee.GetProperty("parkingStatus").GetString(), new[] { "dedicated", "temporary" });
    }

    [Fact]
    public async Task AC_2_2_RejectsUnknownEmailAndIncorrectPassword()
    {
        using var app = _factory.Restarted();
        using var client = app.CreateClient();
        var account = NewAccount();
        await RegisterAsync(client, account);

        using (var response = await SendJsonAsync(client, HttpMethod.Post, "/auth/login", new
        {
            email = $"unknown-{Guid.NewGuid():N}@example.test",
            password = account.Password
        }))
        {
            await AssertErrorAsync(response, HttpStatusCode.Unauthorized, UnauthorizedMessage, "UNAUTHORIZED");
        }

        using (var response = await SendJsonAsync(client, HttpMethod.Post, "/auth/login", new
        {
            email = account.Email,
            password = "NotTheAccountPassword!"
        }))
        {
            await AssertErrorAsync(response, HttpStatusCode.Unauthorized, UnauthorizedMessage, "UNAUTHORIZED");
        }
    }

    [Fact]
    public async Task AC_3_1_LogoutInvalidatesTheBearerToken()
    {
        using var app = _factory.Restarted();
        using var client = app.CreateClient();
        var account = NewAccount();
        await RegisterAsync(client, account);
        var token = await LoginForTokenAsync(client, account);

        using (var logout = await SendAuthorizedAsync(client, HttpMethod.Post, "/auth/logout", token))
        {
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }

        using var profile = await SendAuthorizedAsync(client, HttpMethod.Get, "/me/profile", token);
        Assert.Equal(HttpStatusCode.Unauthorized, profile.StatusCode);
    }

    [Fact]
    public async Task AC_4_1_RejectsEveryProtectedOperationWithoutValidAuthentication()
    {
        using var app = _factory.Restarted();
        using var client = app.CreateClient();
        var account = NewAccount();
        await RegisterAsync(client, account);
        var validToken = await LoginForTokenAsync(client, account);
        using (var logout = await SendAuthorizedAsync(client, HttpMethod.Post, "/auth/logout", validToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }

        var operations = ProtectedOperations();
        foreach (var operation in operations)
        {
            using var noToken = await SendOperationAsync(client, operation, null);
            await AssertErrorAsync(noToken, HttpStatusCode.Unauthorized, UnauthorizedMessage, "UNAUTHORIZED");

            using var invalidToken = await SendOperationAsync(client, operation, "invalid-bearer-token");
            await AssertErrorAsync(invalidToken, HttpStatusCode.Unauthorized, UnauthorizedMessage, "UNAUTHORIZED");

            using var loggedOutToken = await SendOperationAsync(client, operation, validToken);
            await AssertErrorAsync(loggedOutToken, HttpStatusCode.Unauthorized, UnauthorizedMessage, "UNAUTHORIZED");
        }
    }

    [Fact]
    public async Task AC_5_1_RefusesEveryAdminOperationForAnEmployeeAccount()
    {
        using var app = _factory.Restarted();
        using var client = app.CreateClient();
        var account = NewAccount();
        await RegisterAsync(client, account);
        var token = await LoginForTokenAsync(client, account);

        foreach (var operation in AdminOperations())
        {
            using var response = await SendOperationAsync(client, operation, token);
            await AssertErrorAsync(response, HttpStatusCode.Forbidden, ForbiddenMessage, "FORBIDDEN");
        }
    }

    [Fact]
    public async Task AC_5_2_RefusesGateEventsForAnEmployeeWithoutGateAccess()
    {
        using var app = _factory.Restarted();
        using var client = app.CreateClient();
        var account = NewAccount();
        await RegisterAsync(client, account);
        var token = await LoginForTokenAsync(client, account);

        using var response = await SendAuthorizedAsync(client, HttpMethod.Get, "/gate/events/live", token);

        await AssertErrorAsync(response, HttpStatusCode.Forbidden, ForbiddenMessage, "FORBIDDEN");
    }

    [Fact]
    public async Task AC_6_1_ReturnsEmployeeParkingAndAllRegisteredVehiclesInProfile()
    {
        using var app = _factory.Restarted();
        using var client = app.CreateClient();
        var account = NewAccount();
        await RegisterAsync(client, account);
        var token = await LoginForTokenAsync(client, account);
        var vehicles = new[]
        {
            new { plate = NewPlate("P1"), make = "Honda", model = "City", color = "Blue" },
            new { plate = NewPlate("P2"), make = "Toyota", model = "Yaris", color = "Silver" },
            new { plate = NewPlate("P3"), make = "Suzuki", model = "Swift", color = "Red" }
        };

        foreach (var vehicle in vehicles)
        {
            using var created = await SendAuthorizedJsonAsync(client, HttpMethod.Post, "/me/vehicles", token, vehicle);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        }

        using var response = await SendAuthorizedAsync(client, HttpMethod.Get, "/me/profile", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        var root = body.RootElement;
        var employee = root.GetProperty("employee");
        Assert.True(employee.GetProperty("id").GetInt32() > 0);
        Assert.False(string.IsNullOrWhiteSpace(employee.GetProperty("employeeId").GetString()));
        Assert.Equal(account.Name, employee.GetProperty("name").GetString());
        Assert.Equal(account.Email, employee.GetProperty("email").GetString());
        Assert.True(employee.TryGetProperty("department", out _));
        Assert.True(employee.TryGetProperty("role", out _));

        var parking = root.GetProperty("parking");
        Assert.Contains(parking.GetProperty("status").GetString(), new[] { "dedicated", "temporary" });
        Assert.True(parking.TryGetProperty("assignedFloor", out _));

        var actualVehicles = root.GetProperty("vehicles").EnumerateArray()
            .Select(vehicle => new VehicleValue(
                vehicle.GetProperty("plate").GetString()!,
                vehicle.GetProperty("make").GetString(),
                vehicle.GetProperty("model").GetString(),
                vehicle.GetProperty("color").GetString()))
            .ToHashSet();
        var expectedVehicles = vehicles.Select(vehicle => new VehicleValue(vehicle.plate, vehicle.make, vehicle.model, vehicle.color)).ToHashSet();
        Assert.True(expectedVehicles.SetEquals(actualVehicles));
        Assert.All(root.GetProperty("vehicles").EnumerateArray(), vehicle => Assert.True(vehicle.GetProperty("id").GetInt32() > 0));
        Assert.Equal(JsonValueKind.Array, root.GetProperty("unavailableFields").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("retryableFields").ValueKind);
    }

    [Fact]
    public async Task AC_6_2_RetriesRequestedProfileFieldsAndReturnsProfileLists()
    {
        using var app = _factory.Restarted();
        var standIn = new ProfileVerificationHttpClientFactory(
            "{\"department\":\"Verified Engineering\",\"assignedFloor\":\"Verification Floor 3\"}");
        using var configuredApp = app.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(standIn);
        }));
        using var client = configuredApp.CreateClient();
        var account = NewAccount();
        await RegisterAsync(client, account);
        var token = await LoginForTokenAsync(client, account);
        var fields = new[] { "department", "assignedFloor" };

        using var response = await SendAuthorizedJsonAsync(client, HttpMethod.Post, "/me/profile/retry", token, new { fields });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        var root = body.RootElement;
        var employee = root.GetProperty("employee");
        Assert.True(employee.GetProperty("id").GetInt32() > 0);
        Assert.Equal(account.Name, employee.GetProperty("name").GetString());
        Assert.Equal(account.Email, employee.GetProperty("email").GetString());
        Assert.Equal("Verified Engineering", employee.GetProperty("department").GetString());
        Assert.Equal("Verification Floor 3", root.GetProperty("parking").GetProperty("assignedFloor").GetString());
        Assert.True(root.GetProperty("vehicles").ValueKind == JsonValueKind.Array);
        Assert.True(root.GetProperty("unavailableFields").ValueKind == JsonValueKind.Array);
        Assert.True(root.GetProperty("retryableFields").ValueKind == JsonValueKind.Array);
        Assert.Single(standIn.Requests);
        Assert.False(string.IsNullOrWhiteSpace(standIn.Requests[0].Uri));
        Assert.Contains("department", standIn.Requests[0].Uri + standIn.Requests[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("assignedFloor", standIn.Requests[0].Uri + standIn.Requests[0].Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AC_6_3_ReturnsServiceUnavailableWhenProfileVerificationFails()
    {
        using var app = _factory.Restarted();
        var standIn = new ProfileVerificationHttpClientFactory();
        using var configuredApp = app.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHttpClientFactory>();
            services.AddSingleton<IHttpClientFactory>(standIn);
        }));
        using var client = configuredApp.CreateClient();
        var account = NewAccount();
        await RegisterAsync(client, account);
        var token = await LoginForTokenAsync(client, account);

        using var response = await SendAuthorizedJsonAsync(client, HttpMethod.Post, "/me/profile/retry", token,
            new { fields = new[] { "department", "assignedFloor" } });

        await AssertErrorAsync(response, HttpStatusCode.ServiceUnavailable, ServiceUnavailableMessage, "SERVICE_UNAVAILABLE");
        Assert.Single(standIn.Requests);
        Assert.False(string.IsNullOrWhiteSpace(standIn.Requests[0].Uri));
        Assert.Contains("department", standIn.Requests[0].Uri + standIn.Requests[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("assignedFloor", standIn.Requests[0].Uri + standIn.Requests[0].Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AC_6_4_CreatesVehicleAndReturnsAllSubmittedVehicleFields()
    {
        using var app = _factory.Restarted();
        using var client = app.CreateClient();
        var account = NewAccount();
        await RegisterAsync(client, account);
        var token = await LoginForTokenAsync(client, account);
        var vehicle = new { plate = NewPlate("N"), make = "Mazda", model = "3", color = "Green" };

        using var response = await SendAuthorizedJsonAsync(client, HttpMethod.Post, "/me/vehicles", token, vehicle);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        var result = body.RootElement;
        Assert.True(result.GetProperty("id").GetInt32() > 0);
        Assert.Equal(vehicle.plate, result.GetProperty("plate").GetString());
        Assert.Equal(vehicle.make, result.GetProperty("make").GetString());
        Assert.Equal(vehicle.model, result.GetProperty("model").GetString());
        Assert.Equal(vehicle.color, result.GetProperty("color").GetString());
    }

    [Fact]
    public async Task AC_6_5_ReportsConflictWhenTheAccountSubmitsAnExistingVehiclePlate()
    {
        using var app = _factory.Restarted();
        using var client = app.CreateClient();
        var account = NewAccount();
        await RegisterAsync(client, account);
        var token = await LoginForTokenAsync(client, account);
        var vehicle = new { plate = NewPlate("D"), make = "Kia", model = "Rio", color = "Black" };
        using (var first = await SendAuthorizedJsonAsync(client, HttpMethod.Post, "/me/vehicles", token, vehicle))
        {
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        }

        using var duplicate = await SendAuthorizedJsonAsync(client, HttpMethod.Post, "/me/vehicles", token, vehicle);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task CHAR_RootEndpointKeepsItsExistingStatusAndBody()
    {
        using var app = _factory.Restarted();
        using var client = app.CreateClient();

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("API is up", await response.Content.ReadAsStringAsync());
    }

    private static async Task RegisterAsync(HttpClient client, TestAccount account)
    {
        using var response = await SendJsonAsync(client, HttpMethod.Post, "/auth/register", new
        {
            name = account.Name,
            email = account.Email,
            password = account.Password
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<string> LoginForTokenAsync(HttpClient client, TestAccount account)
    {
        using var response = await SendJsonAsync(client, HttpMethod.Post, "/auth/login", new
        {
            email = account.Email,
            password = account.Password
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        var token = body.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        return token!;
    }

    private static async Task<HttpResponseMessage> SendJsonAsync(HttpClient client, HttpMethod method, string path, object value)
    {
        var content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
        return await client.SendAsync(new HttpRequestMessage(method, path) { Content = content });
    }

    private static async Task<HttpResponseMessage> SendAuthorizedJsonAsync(HttpClient client, HttpMethod method, string path, string token, object value)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendAuthorizedAsync(HttpClient client, HttpMethod method, string path, string token)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendOperationAsync(HttpClient client, Operation operation, string? token)
    {
        var request = new HttpRequestMessage(operation.Method, operation.Path);
        if (operation.Body is not null)
        {
            request.Content = new StringContent(operation.Body, Encoding.UTF8, "application/json");
        }
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return await client.SendAsync(request);
    }

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string message, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal(message, body.RootElement.GetProperty("message").GetString());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private static TestAccount NewAccount()
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new TestAccount($"Test Employee {suffix[..8]}", $"test-{suffix}@example.test", "ParkingTest2026!");
    }

    private static string NewPlate(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..8].ToUpperInvariant();

    private static IReadOnlyList<Operation> ProtectedOperations() =>
    [
        new(HttpMethod.Post, "/auth/logout"),
        new(HttpMethod.Get, "/me/profile"),
        new(HttpMethod.Post, "/me/profile/retry", "{\"fields\":[\"department\"]}"),
        new(HttpMethod.Post, "/me/vehicles", "{\"plate\":\"TEST-VEHICLE\"}"),
        new(HttpMethod.Get, "/parking/dashboard"),
        new(HttpMethod.Post, "/parking/releases/today", "{\"officeDate\":\"2026-10-22\"}"),
        new(HttpMethod.Get, "/parking/releases?dateFrom=2026-10-22&dateTo=2026-10-23"),
        new(HttpMethod.Post, "/parking/releases", "{\"dates\":[\"2026-10-22\",\"2026-10-23\"]}"),
        new(HttpMethod.Post, "/parking/releases/1/cancel", "{\"reason\":\"test\"}"),
        new(HttpMethod.Get, "/parking/availability?date=2026-10-22"),
        new(HttpMethod.Post, "/parking/claims", "{\"date\":\"2026-10-22\",\"plate\":\"TEST-VEHICLE\"}"),
        new(HttpMethod.Get, "/gate/events/live?limit=20"),
        new(HttpMethod.Get, "/admin/entry-records"),
        new(HttpMethod.Get, "/admin/reports"),
        new(HttpMethod.Get, "/admin/reports/unused"),
        new(HttpMethod.Get, "/admin/employees"),
        new(HttpMethod.Post, "/admin/employees", "{\"employeeId\":\"EMP-TEST\",\"name\":\"Test Employee\",\"email\":\"employee@example.test\",\"department\":\"Engineering\",\"role\":\"Engineer\",\"parkingStatus\":\"Temporary\",\"assignedFloor\":null,\"vehicles\":[]}"),
        new(HttpMethod.Get, "/admin/employees/1"),
        new(HttpMethod.Put, "/admin/employees/1", "{\"employeeId\":\"EMP-TEST\",\"name\":\"Test Employee\",\"email\":\"employee@example.test\",\"department\":\"Engineering\",\"role\":\"Engineer\",\"parkingStatus\":\"Temporary\",\"assignedFloor\":null,\"vehicles\":[]}"),
        new(HttpMethod.Get, "/admin/settings"),
        new(HttpMethod.Put, "/admin/settings", "{\"dailyReleaseTime\":\"08:00\",\"areas\":[{\"name\":\"Floor 1\"}],\"reportRecipients\":[\"reports@example.test\"]}"),
        new(HttpMethod.Get, "/admin/admins"),
        new(HttpMethod.Post, "/admin/admins", "{\"employeeId\":1}"),
        new(HttpMethod.Get, "/admin/admins/directory?search=test"),
        new(HttpMethod.Delete, "/admin/admins/1")
    ];

    private static IReadOnlyList<Operation> AdminOperations() =>
    [
        new(HttpMethod.Get, "/admin/entry-records"),
        new(HttpMethod.Get, "/admin/reports"),
        new(HttpMethod.Get, "/admin/reports/unused"),
        new(HttpMethod.Get, "/admin/employees"),
        new(HttpMethod.Post, "/admin/employees", "{\"employeeId\":\"EMP-TEST\",\"name\":\"Test Employee\",\"email\":\"employee@example.test\",\"department\":\"Engineering\",\"role\":\"Engineer\",\"parkingStatus\":\"Temporary\",\"assignedFloor\":null,\"vehicles\":[]}"),
        new(HttpMethod.Get, "/admin/employees/1"),
        new(HttpMethod.Put, "/admin/employees/1", "{\"employeeId\":\"EMP-TEST\",\"name\":\"Test Employee\",\"email\":\"employee@example.test\",\"department\":\"Engineering\",\"role\":\"Engineer\",\"parkingStatus\":\"Temporary\",\"assignedFloor\":null,\"vehicles\":[]}"),
        new(HttpMethod.Get, "/admin/settings"),
        new(HttpMethod.Put, "/admin/settings", "{\"dailyReleaseTime\":\"08:00\",\"areas\":[{\"name\":\"Floor 1\"}],\"reportRecipients\":[\"reports@example.test\"]}"),
        new(HttpMethod.Get, "/admin/admins"),
        new(HttpMethod.Post, "/admin/admins", "{\"employeeId\":1}"),
        new(HttpMethod.Get, "/admin/admins/directory?search=test"),
        new(HttpMethod.Delete, "/admin/admins/1")
    ];

    private sealed record TestAccount(string Name, string Email, string Password);
    private sealed record Operation(HttpMethod Method, string Path, string? Body = null);
    private sealed record VehicleValue(string Plate, string? Make, string? Model, string? Color);
    private sealed record RecordedCall(string Name, string? Method, string? Uri, string Body);

    private sealed class ProfileVerificationHttpClientFactory : IHttpClientFactory
    {
        private readonly string? _successfulResponse;

        public ProfileVerificationHttpClientFactory(string? successfulResponse = null) => _successfulResponse = successfulResponse;

        public List<RecordedCall> Requests { get; } = [];

        public HttpClient CreateClient(string name) => new(new RecordingHandler(this, name), disposeHandler: true);

        private sealed class RecordingHandler(ProfileVerificationHttpClientFactory owner, string clientName) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
                owner.Requests.Add(new RecordedCall(
                    clientName,
                    request.Method.Method,
                    request.RequestUri?.ToString(),
                    body));
                if (owner._successfulResponse is null)
                {
                    throw new HttpRequestException("Profile verification service is unavailable.");
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(owner._successfulResponse, Encoding.UTF8, "application/json")
                };
            }
        }
    }
}
