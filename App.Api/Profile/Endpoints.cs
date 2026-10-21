using System.Globalization;
using System.Text.Json;
using System.Net.Http.Json;
using App.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace App.Api;

internal sealed class ProfileEndpoints : IApiEndpointModule
{
    private const string ProfileVerificationClient = "ProfileVerification";
    private static readonly Uri ProfileVerificationUri = new("https://profile-verification.invalid/retry");

    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/me/profile", GetProfile)
            .WithName("getMyProfile")
            .RequireAuthorization()
            .Produces<EmployeeProfile>(StatusCodes.Status200OK)
            .Produces<Error>(StatusCodes.Status401Unauthorized);

        endpoints.MapPost("/me/profile/retry", RetryProfileFields)
            .WithName("retryMyProfileFields")
            .RequireAuthorization()
            .Produces<EmployeeProfile>(StatusCodes.Status200OK)
            .Produces<Error>(StatusCodes.Status401Unauthorized)
            .Produces<Error>(StatusCodes.Status503ServiceUnavailable);

        endpoints.MapPost("/me/vehicles", AddVehicle)
            .WithName("addMyVehicle")
            .RequireAuthorization()
            .Produces<Vehicle>(StatusCodes.Status201Created)
            .Produces<Error>(StatusCodes.Status400BadRequest)
            .Produces<Error>(StatusCodes.Status401Unauthorized)
            .Produces<Error>(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> GetProfile(HttpContext context, AppDb db, CancellationToken cancellationToken)
    {
        var employeeId = GetEmployeeId(context);
        var employee = await db.Employees.SingleAsync(candidate => candidate.Id == employeeId, cancellationToken);
        var vehicles = await db.Set<VehicleEntity>()
            .Where(vehicle => vehicle.EmployeeId == employeeId)
            .OrderBy(vehicle => vehicle.Id)
            .Select(vehicle => new Vehicle(vehicle.Id, vehicle.Plate, vehicle.Make, vehicle.Model, vehicle.Color))
            .ToListAsync(cancellationToken);

        return Results.Ok(CreateProfile(employee, vehicles));
    }

    private static async Task<IResult> RetryProfileFields(
        ProfileRetryRequest request,
        HttpContext context,
        AppDb db,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (request.Fields is null)
        {
            return BadRequest();
        }

        var employeeId = GetEmployeeId(context);
        var employee = await db.Employees.SingleAsync(candidate => candidate.Id == employeeId, cancellationToken);
        var httpClientFactory = services.GetService<IHttpClientFactory>();
        if (httpClientFactory is null)
        {
            var currentVehicles = await db.Set<VehicleEntity>()
                .Where(vehicle => vehicle.EmployeeId == employeeId)
                .OrderBy(vehicle => vehicle.Id)
                .Select(vehicle => new Vehicle(vehicle.Id, vehicle.Plate, vehicle.Make, vehicle.Model, vehicle.Color))
                .ToListAsync(cancellationToken);
            return Results.Ok(CreateProfile(employee, currentVehicles));
        }

        using var client = httpClientFactory.CreateClient(ProfileVerificationClient);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ProfileVerificationUri)
        {
            Content = JsonContent.Create(new { fields = request.Fields })
        };

        try
        {
            using var response = await client.SendAsync(httpRequest, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return ServiceUnavailable();
            }

            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
            ApplyVerifiedFields(employee, request.Fields, document.RootElement);
            await db.SaveChangesAsync(cancellationToken);

            var vehicles = await db.Set<VehicleEntity>()
                .Where(vehicle => vehicle.EmployeeId == employeeId)
                .OrderBy(vehicle => vehicle.Id)
                .Select(vehicle => new Vehicle(vehicle.Id, vehicle.Plate, vehicle.Make, vehicle.Model, vehicle.Color))
                .ToListAsync(cancellationToken);
            return Results.Ok(CreateProfile(employee, vehicles));
        }
        catch (HttpRequestException)
        {
            return ServiceUnavailable();
        }
        catch (JsonException)
        {
            return ServiceUnavailable();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ServiceUnavailable();
        }
    }

    private static async Task<IResult> AddVehicle(
        VehicleInput request,
        HttpContext context,
        AppDb db,
        CancellationToken cancellationToken)
    {
        var plate = request.Plate?.Trim();
        if (string.IsNullOrWhiteSpace(plate))
        {
            return BadRequest();
        }

        var employeeId = GetEmployeeId(context);
        var normalizedPlate = plate.ToUpperInvariant();
        if (await db.Set<VehicleEntity>().AnyAsync(
                vehicle => vehicle.EmployeeId == employeeId && vehicle.Plate.ToUpper() == normalizedPlate,
                cancellationToken))
        {
            return Conflict();
        }

        var vehicle = new VehicleEntity
        {
            EmployeeId = employeeId,
            Plate = plate,
            Make = NormalizeOptional(request.Make),
            Model = NormalizeOptional(request.Model),
            Color = NormalizeOptional(request.Color)
        };
        db.Set<VehicleEntity>().Add(vehicle);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Conflict();
        }

        return Results.Json(
            new Vehicle(vehicle.Id, vehicle.Plate, vehicle.Make, vehicle.Model, vehicle.Color),
            statusCode: StatusCodes.Status201Created);
    }

    private static EmployeeProfile CreateProfile(Employee employee, IReadOnlyList<Vehicle> vehicles)
    {
        var unavailableFields = new List<string>();
        if (string.IsNullOrWhiteSpace(employee.Department))
        {
            unavailableFields.Add("department");
        }

        if (string.IsNullOrWhiteSpace(employee.AssignedFloor))
        {
            unavailableFields.Add("assignedFloor");
        }

        return new EmployeeProfile(
            new ProfileEmployee(employee.Id, employee.EmployeeId, employee.Name, employee.Email, employee.Department, employee.Role),
            new ProfileParking(
                employee.ParkingStatus == EmployeeParkingStatus.Dedicated
                    ? ProfileParkingStatus.Dedicated
                    : ProfileParkingStatus.Temporary,
                employee.AssignedFloor),
            vehicles,
            unavailableFields,
            unavailableFields.ToArray());
    }

    private static void ApplyVerifiedFields(Employee employee, IReadOnlyCollection<string> fields, JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("The profile verification response must be a JSON object.");
        }

        foreach (var field in fields)
        {
            if (string.Equals(field, "department", StringComparison.OrdinalIgnoreCase)
                && TryReadField(payload, "department", out var department))
            {
                employee.Department = department;
            }
            else if (string.Equals(field, "assignedFloor", StringComparison.OrdinalIgnoreCase)
                     && TryReadField(payload, "assignedFloor", out var assignedFloor))
            {
                employee.AssignedFloor = assignedFloor;
            }
        }
    }

    private static bool TryReadField(JsonElement payload, string fieldName, out string? value)
    {
        foreach (var property in payload.EnumerateObject())
        {
            if (string.Equals(property.Name, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Null => null,
                    _ => throw new JsonException($"The verified {fieldName} value must be a string or null.")
                };
                return true;
            }
        }

        value = null;
        return false;
    }

    private static int GetEmployeeId(HttpContext context) =>
        int.Parse(context.User.FindFirst("employeeId")!.Value, CultureInfo.InvariantCulture);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IResult BadRequest() => Results.Json(
        new Error("The submitted values are not valid.", "BAD_REQUEST"),
        statusCode: StatusCodes.Status400BadRequest);

    private static IResult Conflict() => Results.Json(
        new Error("No space could be claimed. The last free space was taken just now.", "CONFLICT"),
        statusCode: StatusCodes.Status409Conflict);

    private static IResult ServiceUnavailable() => Results.Json(
        new Error("Verification is not available right now.", "SERVICE_UNAVAILABLE"),
        statusCode: StatusCodes.Status503ServiceUnavailable);
}
