using Microsoft.AspNetCore.Http.HttpResults;

namespace App.Api;

internal sealed record HealthStatus(string Status, string Database);

internal sealed class HealthEndpoints : IApiEndpointModule
{
    private static readonly TimeSpan DatabaseTimeout = TimeSpan.FromSeconds(3);

    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", GetHealthAsync)
            .WithName("getHealth")
            .WithTags("Health")
            .AllowAnonymous()
            .Produces<HealthStatus>(StatusCodes.Status200OK)
            .Produces<HealthStatus>(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<Results<Ok<HealthStatus>, JsonHttpResult<HealthStatus>>> GetHealthAsync(
        AppDb db, ILogger<HealthEndpoints> logger, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(DatabaseTimeout);
            if (await db.Database.CanConnectAsync(timeout.Token))
                return TypedResults.Ok(new HealthStatus("ok", "ok"));
            logger.LogWarning("Health check: the database did not accept a connection");
        }
        catch (Exception ex)
        {
            // logged here only: the response body is fixed so an unauthenticated caller learns nothing about the database
            logger.LogWarning(ex, "Health check: the database check failed");
        }
        return TypedResults.Json(new HealthStatus("degraded", "unreachable"), statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
