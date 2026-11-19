using System.Net;
using Npgsql;
using Xunit;

namespace App.Tests;

public class HealthEndpointTests : IClassFixture<AppFactory>
{
    private readonly AppFactory _factory;
    public HealthEndpointTests(AppFactory factory) => _factory = factory;

    [Fact]
    public async Task ReturnsOkWithoutAuthenticationWhenDatabaseIsReachable()
    {
        var res = await _factory.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("""{"status":"ok","database":"ok"}""", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Returns503WithFixedBodyWhenDatabaseIsUnreachable()
    {
        using var factory = new AppFactory();
        var client = factory.CreateClient(); // starts the app and creates its tables
        var db = new NpgsqlConnectionStringBuilder(factory.ConnectionString).Database;
        using (var admin = new NpgsqlConnection(Environment.GetEnvironmentVariable("ConnectionStrings__App")))
        {
            admin.Open();
            using var drop = new NpgsqlCommand($"DROP DATABASE \"{db}\" WITH (FORCE)", admin);
            drop.ExecuteNonQuery();
        }

        var res = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        Assert.Equal("""{"status":"degraded","database":"unreachable"}""", await res.Content.ReadAsStringAsync());
    }
}
