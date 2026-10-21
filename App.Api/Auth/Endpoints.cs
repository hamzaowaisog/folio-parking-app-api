using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace App.Api;

internal sealed class AuthEndpoints : IApiEndpointModule
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/auth/register", NotImplemented).AllowAnonymous();
        endpoints.MapPost("/auth/login", NotImplemented).AllowAnonymous();
        endpoints.MapPost("/auth/logout", NotImplemented).RequireAuthorization();
    }

    private static Task<IResult> NotImplemented() => throw new NotImplementedException();
}
