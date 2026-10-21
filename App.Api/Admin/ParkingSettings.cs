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
}
