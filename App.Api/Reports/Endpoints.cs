using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace App.Api;

internal sealed class ReportEndpoints : IApiEndpointModule
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/admin/entry-records", NotImplemented).RequireAuthorization("Administrator");
        endpoints.MapGet("/admin/reports", NotImplemented).RequireAuthorization("Administrator");
        endpoints.MapGet("/admin/reports/unused", NotImplemented).RequireAuthorization("Administrator");
    }

    private static Task<IResult> NotImplemented() => throw new NotImplementedException();
}
