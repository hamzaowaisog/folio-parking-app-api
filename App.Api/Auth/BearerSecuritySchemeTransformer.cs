using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace App.Api;

internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var scheme = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Name = "Authorization"
        };
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes["bearerAuth"] = scheme;
        var securityReference = new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "bearerAuth" }
        };
        document.SecurityRequirements =
        [
            new OpenApiSecurityRequirement { [securityReference] = Array.Empty<string>() }
        ];

        foreach (var path in new[] { "/auth/register", "/auth/login" })
        {
            if (document.Paths.TryGetValue(path, out var pathItem)
                && pathItem.Operations.TryGetValue(OperationType.Post, out var operation))
            {
                operation.Security = [new OpenApiSecurityRequirement()];
            }
        }
        SetDescription(pathItem: document.Paths["/auth/register"], OperationType.Post, "201", "Account created");
        SetDescription(document.Paths["/auth/register"], OperationType.Post, "400", "Invalid request");
        SetDescription(document.Paths["/auth/register"], OperationType.Post, "409", "Request conflicts with current parking state");
        SetDescription(document.Paths["/auth/login"], OperationType.Post, "200", "Authenticated session");
        SetDescription(document.Paths["/auth/login"], OperationType.Post, "401", "Authentication required");
        SetDescription(document.Paths["/auth/login"], OperationType.Post, "403", "Access refused");
        SetDescription(document.Paths["/auth/logout"], OperationType.Post, "204", "Bearer session invalidated");

        return Task.CompletedTask;
    }

    private static void SetDescription(OpenApiPathItem pathItem, OperationType operationType, string statusCode, string description)
    {
        if (pathItem.Operations.TryGetValue(operationType, out var operation)
            && operation.Responses.TryGetValue(statusCode, out var response))
        {
            response.Description = description;
        }
    }
}
