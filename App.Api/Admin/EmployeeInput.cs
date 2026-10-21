using System.ComponentModel.DataAnnotations;

namespace App.Api.Admin;

public sealed class EmployeeInput
{
    [Required]
    public string EmployeeId { get; init; } = string.Empty;
    [Required]
    public string Name { get; init; } = string.Empty;
    [Required, EmailAddress]
    public string Email { get; init; } = string.Empty;
    [Required]
    public string Department { get; init; } = string.Empty;
    [Required]
    public string Role { get; init; } = string.Empty;
    [Required]
    public ParkingStatusFilter ParkingStatus { get; init; }
    [Required]
    public string? AssignedFloor { get; init; }
    [Required]
    public EmployeeVehicleInput[] Vehicles { get; init; } = [];
}
