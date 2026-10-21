using Microsoft.AspNetCore.Routing;

namespace App.Api;

internal interface IApiEndpointModule
{
    void Map(IEndpointRouteBuilder endpoints);
}
