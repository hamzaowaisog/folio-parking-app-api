using System.ComponentModel.DataAnnotations;

namespace App.Api;

public sealed class TodayReleaseRequest
{
    [Required]
    public DateOnly OfficeDate { get; init; }
}
