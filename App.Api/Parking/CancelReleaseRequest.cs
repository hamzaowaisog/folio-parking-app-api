using System.ComponentModel.DataAnnotations;

namespace App.Api;

public sealed class CancelReleaseRequest
{
    [Required]
    public string Reason { get; init; } = string.Empty;
}
