using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace App.Api;

/// <summary>One person's place in the queue for a date. Queue order is Id. Waiting and Offered entries are "active".</summary>
[Table("WaitlistEntries")]
public sealed class WaitlistEntry : IAppEntity
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    [ForeignKey(nameof(EmployeeId))]
    [DeleteBehavior(DeleteBehavior.Restrict)]
    public Employee Employee { get; set; } = null!;
    public DateOnly Date { get; set; }
    public WaitlistEntryStatus Status { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    /// <summary>The spot held for this person while Offered.</summary>
    public int? ReleaseId { get; set; }
    [ForeignKey(nameof(ReleaseId))]
    [DeleteBehavior(DeleteBehavior.SetNull)]
    public ParkingRelease? Release { get; set; }
    public DateTimeOffset? OfferedAt { get; set; }
    public DateTimeOffset? HeldUntil { get; set; }
}
