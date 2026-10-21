using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace App.Api;

internal sealed class GateEndpoints : IApiEndpointModule
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/gate/events/live", NotImplemented).RequireAuthorization("GateOrAdmin");
    }

    private static Task<IResult> NotImplemented() => throw new NotImplementedException();
}
