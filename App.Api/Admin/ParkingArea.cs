using System.ComponentModel.DataAnnotations;

namespace App.Api.Admin;

public sealed class ParkingArea
{
    [Required]
    public string Name { get; init; } = string.Empty;
}
