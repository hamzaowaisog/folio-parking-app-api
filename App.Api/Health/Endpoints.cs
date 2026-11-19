using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace App.Api;

internal sealed record HealthStatus(string Status, string Database);

internal sealed class HealthEndpoints : IApiEndpointModule
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", GetHealthAsync)
            .WithName("getHealth")
            .AllowAnonymous()
            .Produces<HealthStatus>(StatusCodes.Status200OK)
            .Produces<HealthStatus>(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> GetHealthAsync(AppDb db, CancellationToken cancellationToken)
    {
        // the body is fixed on purpose: no exception text or connection detail goes to an unauthenticated caller
        var reachable = false;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            reachable = await db.Database.CanConnectAsync(timeout.Token);
        }
        catch (Exception)
        {
        }
        return reachable
            ? Results.Json(new HealthStatus("ok", "ok"))
            : Results.Json(new HealthStatus("degraded", "unreachable"), statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
