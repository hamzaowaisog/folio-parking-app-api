using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace App.Tests;

// Review R-1: the acceptance tests swap in their own sign-in, so none of them proved that a real signed-in administrator
// reaches /admin. These go through /auth/register and /auth/login and the app's own bearer handler.
public class AdministratorSignInTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _factory;
    public AdministratorSignInTests(AppFactory factory) => _factory = factory;

    [Fact]
    public async Task SignedInAdministratorReachesAdminPages()
    {
        var client = _factory.CreateClient();
        var session = await SignUpAndLogIn(client, "admin.signin@example.test", makeAdministrator: true);

        Assert.Equal("parkingAdministrator", session.GetProperty("employee").GetProperty("role").GetString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        using var response = await client.GetAsync("/admin/employees");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SignedInEmployeeIsRefusedAdminPagesAndStartsWithoutADedicatedSpace()
    {
        var client = _factory.CreateClient();
        var session = await SignUpAndLogIn(client, "employee.signin@example.test", makeAdministrator: false);

        Assert.Equal("employee", session.GetProperty("employee").GetProperty("role").GetString());
        Assert.Equal("temporary", session.GetProperty("employee").GetProperty("parkingStatus").GetString());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        using var response = await client.GetAsync("/admin/employees");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<JsonElement> SignUpAndLogIn(HttpClient client, string email, bool makeAdministrator)
    {
        const string password = "test-only-pass";
        using (var registered = await client.PostAsJsonAsync("/auth/register", new { name = "Sign In Test", email, password }))
            Assert.Equal(HttpStatusCode.Created, registered.StatusCode);

        if (makeAdministrator)
        {
            await using var connection = new NpgsqlConnection(_factory.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                INSERT INTO "AdministratorAccess" ("EmployeeId", "AccessStatus", "ChangedByEmployeeId", "ChangedAt")
                SELECT "Id", 'active', "Id", now() FROM "Employees" WHERE "Email" = @email
                """, connection);
            command.Parameters.AddWithValue("email", email);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        using var login = await client.PostAsJsonAsync("/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return await login.Content.ReadFromJsonAsync<JsonElement>();
    }
}
