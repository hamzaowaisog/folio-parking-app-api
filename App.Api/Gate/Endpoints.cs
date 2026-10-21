using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace App.Api;

internal sealed class GateEndpoints : IApiEndpointModule
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/gate/events/live", GetLiveEventsAsync)
            .WithName("getLiveGateEvents")
            .RequireAuthorization("GateOrAdmin")
            .Produces<GateFeed>(StatusCodes.Status200OK)
            .Produces<Error>(StatusCodes.Status401Unauthorized)
            .Produces<Error>(StatusCodes.Status403Forbidden)
            .Produces<Error>(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> GetLiveEventsAsync(
        AppDb db,
        IServiceProvider services,
        ILogger<GateEndpoints> logger,
        CancellationToken cancellationToken,
        int limit = 20)
    {
        var gateEventService = services.GetService<IGateEventService>() ?? new GateEventService(db);
        try
        {
            var events = await gateEventService.GetRecentEventsAsync(limit, cancellationToken);
            var verifications = events.Select(VerificationMapping.ToVerification).ToArray();
            return Results.Ok(new GateFeed(CameraStatus.Online, verifications));
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Gate event service is unavailable while reading live events");
            return Results.Json(new Error("Gate event service is unavailable.", "SERVICE_UNAVAILABLE"),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

}
