using System.ComponentModel.DataAnnotations;

namespace App.Api;

public sealed class ReleaseScheduleRequest
{
    [Required]
    public List<DateOnly> Dates { get; init; } = [];
}
