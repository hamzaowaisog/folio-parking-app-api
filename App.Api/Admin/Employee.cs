using System.ComponentModel.DataAnnotations;

namespace App.Api.Admin;

public sealed record Employee(
    int Id,
    [property: Required] string EmployeeId,
    [property: Required] string Name,
    [property: Required, EmailAddress] string Email,
    [property: Required] string Department,
    [property: Required] string Role,
    [property: Required] ParkingStatusFilter ParkingStatus,
    [property: Required] string? AssignedFloor,
    [property: Required] EmployeeVehicle[] Vehicles,
    [property: Required] DatedActivity[] DatedActivity);
