using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace App.Api;

internal sealed class AdminEndpoints : IApiEndpointModule
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/admin/employees", NotImplemented).RequireAuthorization("Administrator");
        endpoints.MapPost("/admin/employees", NotImplemented).RequireAuthorization("Administrator");
        endpoints.MapGet("/admin/employees/{employeeId}", NotImplemented).RequireAuthorization("Administrator");
        endpoints.MapPut("/admin/employees/{employeeId}", NotImplemented).RequireAuthorization("Administrator");
        endpoints.MapGet("/admin/settings", NotImplemented).RequireAuthorization("Administrator");
        endpoints.MapPut("/admin/settings", NotImplemented).RequireAuthorization("Administrator");
        endpoints.MapGet("/admin/admins", NotImplemented).RequireAuthorization("Administrator");
        endpoints.MapPost("/admin/admins", NotImplemented).RequireAuthorization("Administrator");
        endpoints.MapGet("/admin/admins/directory", NotImplemented).RequireAuthorization("Administrator");
        endpoints.MapDelete("/admin/admins/{employeeId}", NotImplemented).RequireAuthorization("Administrator");
    }

    private static Task<IResult> NotImplemented() => throw new NotImplementedException();
}
