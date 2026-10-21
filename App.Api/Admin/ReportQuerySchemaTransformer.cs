using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace App.Api.Admin;

internal sealed class ReportQuerySchemaTransformer : IOpenApiDocumentTransformer, IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type == typeof(EmployeeVehicle)
            && schema.Properties.TryGetValue("id", out var id))
        {
            id.Nullable = false;
        }

        return Task.CompletedTask;
    }

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        SetStringEnum(document, "/admin/reports", "period", "daily", "weekly");
        SetStringEnum(document, "/admin/reports/unused", "category", "Dedicated", "Temporary");
        return Task.CompletedTask;
    }

    private static void SetStringEnum(OpenApiDocument document, string path, string name, params string[] values)
    {
        if (!document.Paths.TryGetValue(path, out var pathItem)
            || !pathItem.Operations.TryGetValue(OperationType.Get, out var operation))
        {
            return;
        }

        var parameter = operation.Parameters.FirstOrDefault(candidate => candidate.Name == name && candidate.In == ParameterLocation.Query);
        if (parameter is null) return;

        parameter.Schema.Reference = null;
        parameter.Schema.Type = "string";
        parameter.Schema.Format = null;
        parameter.Schema.Enum = values.Select(value => (IOpenApiAny)new OpenApiString(value)).ToList();
    }
}
