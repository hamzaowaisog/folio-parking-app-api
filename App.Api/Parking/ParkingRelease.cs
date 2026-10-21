using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace App.Api;

[Table("ParkingReleases")]
[Index(nameof(EmployeeId), nameof(Date), IsUnique = true)]
public sealed class ParkingRelease : IAppEntity
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    [ForeignKey(nameof(EmployeeId))]
    [DeleteBehavior(DeleteBehavior.Restrict)]
    public Employee Employee { get; set; } = null!;
    public DateOnly Date { get; set; }
    public ParkingReleaseStatus Status { get; set; }
    public string? Area { get; set; }
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
    public DateTimeOffset ClaimableAt { get; set; }
    public bool IsCancelled { get; set; }
    public string? CancellationReason { get; set; }
}
