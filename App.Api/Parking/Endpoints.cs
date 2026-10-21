using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace App.Api;

internal sealed class ParkingEndpoints : IApiEndpointModule
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/parking/dashboard", NotImplemented).RequireAuthorization();
        endpoints.MapPost("/parking/releases/today", NotImplemented).RequireAuthorization();
        endpoints.MapGet("/parking/releases", NotImplemented).RequireAuthorization();
        endpoints.MapPost("/parking/releases", NotImplemented).RequireAuthorization();
        endpoints.MapPost("/parking/releases/{releaseId}/cancel", NotImplemented).RequireAuthorization();
        endpoints.MapGet("/parking/availability", NotImplemented).RequireAuthorization();
        endpoints.MapPost("/parking/claims", NotImplemented).RequireAuthorization();
    }

    private static Task<IResult> NotImplemented() => throw new NotImplementedException();
}
