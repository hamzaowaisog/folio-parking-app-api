using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;

namespace App.Api;

internal sealed class AuthSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;
        if (type == typeof(RegisterRequest))
        {
            SetFormat(schema, "email", "email");
            SetFormat(schema, "password", "password");
        }
        else if (type == typeof(LoginRequest))
        {
            SetFormat(schema, "email", "email");
            SetFormat(schema, "password", "password");
        }
        else if (type == typeof(RegistrationResult))
        {
            SetFormat(schema, "email", "email");
            SetEnum(schema, "accountStatus", "created");
        }
        else if (type == typeof(SessionEmployee))
        {
            SetFormat(schema, "email", "email");
            SetEnum(schema, "role", "employee", "gateGuard", "parkingAdministrator");
            SetEnum(schema, "parkingStatus", "dedicated", "temporary");
        }

        return Task.CompletedTask;
    }

    private static void SetFormat(OpenApiSchema schema, string propertyName, string format)
    {
        if (schema.Properties.TryGetValue(propertyName, out var property))
        {
            property.Format = format;
        }
    }

    private static void SetEnum(OpenApiSchema schema, string propertyName, params string[] values)
    {
        if (schema.Properties.TryGetValue(propertyName, out var property))
        {
            property.Type = "string";
            property.Reference = null;
            property.Enum = values.Select(value => (IOpenApiAny)new OpenApiString(value)).ToList();
        }
    }
}
