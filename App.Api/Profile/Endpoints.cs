using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace App.Api;

internal sealed class ProfileEndpoints : IApiEndpointModule
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/me/profile", NotImplemented).RequireAuthorization();
        endpoints.MapPost("/me/profile/retry", NotImplemented).RequireAuthorization();
        endpoints.MapPost("/me/vehicles", NotImplemented).RequireAuthorization();
    }

    private static Task<IResult> NotImplemented() => throw new NotImplementedException();
}
