using System.ComponentModel.DataAnnotations;

namespace App.Api;

public sealed class ClaimRequest
{
    [Required]
    public DateOnly Date { get; init; }
    [Required]
    public string Plate { get; init; } = string.Empty;
}
