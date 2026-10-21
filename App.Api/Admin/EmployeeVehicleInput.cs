using System.ComponentModel.DataAnnotations;

namespace App.Api.Admin;

public sealed class EmployeeVehicleInput
{
    [Required]
    public string Plate { get; init; } = string.Empty;
    public string? Make { get; init; }
    public string? Model { get; init; }
    public string? Color { get; init; }
}
