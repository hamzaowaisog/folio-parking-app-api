using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace App.Api;

[Table("Vehicles")]
[Index(nameof(EmployeeId), nameof(Plate), IsUnique = true)]
public sealed class VehicleEntity : IAppEntity
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public required string Plate { get; set; }
    public string? Make { get; set; }
    public string? Model { get; set; }
    public string? Color { get; set; }
    public Employee Employee { get; set; } = null!;
}
