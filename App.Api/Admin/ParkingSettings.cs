using System.ComponentModel.DataAnnotations;

namespace App.Api.Admin;

public sealed class ParkingSettings
{
    [Required, DataType(DataType.Time)]
    public string DailyReleaseTime { get; init; } = string.Empty;
    [Required]
    public ParkingArea[] Areas { get; init; } = [];
    [Required]
    public string[] ReportRecipients { get; init; } = [];
    /// <summary>How long a spot is held for the first person on the waitlist, 5 to 240. Left out of a request, it keeps the stored value (30 until set).</summary>
    public int? WaitlistHoldMinutes { get; init; }
}
