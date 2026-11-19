using System.Text.Json;
using Xunit;

namespace App.Tests;

public class WaitlistContractTests(AppFactory factory) : IClassFixture<AppFactory>
{
    // operationId -> (method, path, statuses the endpoint must declare, needs authorization)
    private static readonly (string Op, string Method, string Path, string[] Statuses)[] New =
    [
        ("joinParkingWaitlist", "post", "/parking/waitlist", ["201", "400", "401", "403", "409"]),
        ("listMyWaitlist", "get", "/parking/waitlist", ["200", "401"]),
        ("leaveParkingWaitlist", "delete", "/parking/waitlist/{date}", ["204", "400", "401", "404"]),
        ("getWaitlistReport", "get", "/admin/reports/waitlist", ["200", "400", "401", "403"]),
    ];

    private static string Repo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "contracts", "openapi.yaml"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("contracts/openapi.yaml not found");
    }

    [Fact]
    public async Task EveryNewEndpointDeclaresItsStatusesAndIsInTheContract()
    {
        using var client = factory.CreateClient();
        using var doc = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var contract = await File.ReadAllTextAsync(Path.Combine(Repo(), "contracts", "openapi.yaml"));

        foreach (var (op, method, path, statuses) in New)
        {
            var operation = doc.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method);
            Assert.Equal(op, operation.GetProperty("operationId").GetString());
            var declared = operation.GetProperty("responses").EnumerateObject().Select(r => r.Name).Order().ToArray();
            Assert.Equal(statuses.Order(), declared);
            Assert.Contains($"operationId: {op}", contract);
            Assert.Contains($"  {path}:", contract);
        }
    }

    [Fact]
    public async Task SettingsAndDataModelDescribeTheWaitlist()
    {
        var contract = await File.ReadAllTextAsync(Path.Combine(Repo(), "contracts", "openapi.yaml"));
        var model = await File.ReadAllTextAsync(Path.Combine(Repo(), "contracts", "data-model.yaml"));
        Assert.Contains("waitlistHoldMinutes:", contract);
        foreach (var status in new[] { "Waiting", "Offered", "Claimed", "Expired", "Left" }) Assert.Contains($"- {status}", model);
        Assert.Contains("name: WaitlistEntries", model);
    }

    [Fact]
    public async Task EveryNewEndpointRequiresAuthorization()
    {
        using var client = factory.CreateClient();
        foreach (var (_, method, path, _) in New)
        {
            using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path.Replace("{date}", "2030-01-01")));
            Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
