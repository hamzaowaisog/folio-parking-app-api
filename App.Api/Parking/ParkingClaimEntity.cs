using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace App.Api;

[Table("ParkingClaims")]
[Index(nameof(EmployeeId), nameof(Date), IsUnique = true)]
[Index(nameof(ReleaseId), IsUnique = true)]
public sealed class ParkingClaimEntity : IAppEntity
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    [ForeignKey(nameof(EmployeeId))]
    [DeleteBehavior(DeleteBehavior.Restrict)]
    public Employee Employee { get; set; } = null!;
    public int? ReleaseId { get; set; }
    [ForeignKey(nameof(ReleaseId))]
    [DeleteBehavior(DeleteBehavior.SetNull)]
    public ParkingRelease? Release { get; set; }
    public DateOnly Date { get; set; }
    public required string VehiclePlate { get; set; }
    public required string Area { get; set; }
    public ParkingClaimStatus Status { get; set; }
    public int CreatedByEmployeeId { get; set; }
    [ForeignKey(nameof(CreatedByEmployeeId))]
    [DeleteBehavior(DeleteBehavior.Restrict)]
    public Employee CreatedByEmployee { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public int LastChangedByEmployeeId { get; set; }
    [ForeignKey(nameof(LastChangedByEmployeeId))]
    [DeleteBehavior(DeleteBehavior.Restrict)]
    public Employee LastChangedByEmployee { get; set; } = null!;
    public DateTimeOffset LastChangedAt { get; set; }
}
