namespace App.Api;

public sealed class VehicleInput
{
    public required string Plate { get; init; }
    public string? Make { get; init; }
    public string? Model { get; init; }
    public string? Color { get; init; }
}
