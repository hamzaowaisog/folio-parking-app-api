using System.ComponentModel.DataAnnotations;

namespace App.Api.Admin;

public sealed class EmployeeVehicle
{
    public int? Id { get; init; }
    [Required]
    public string Plate { get; init; } = string.Empty;
    [Required]
    public string? Make { get; init; }
    [Required]
    public string? Model { get; init; }
    [Required]
    public string? Color { get; init; }

    public EmployeeVehicle(int id, string plate, string? make, string? model, string? color)
    {
        Id = id;
        Plate = plate;
        Make = make;
        Model = model;
        Color = color;
    }
}
