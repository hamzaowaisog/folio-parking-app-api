using System.ComponentModel.DataAnnotations;

namespace App.Api;

public sealed class WaitlistJoinRequest
{
    [Required]
    public DateOnly Date { get; init; }
}
