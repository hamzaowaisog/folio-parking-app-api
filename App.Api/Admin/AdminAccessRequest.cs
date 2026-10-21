using System.ComponentModel.DataAnnotations;

namespace App.Api.Admin;

public sealed class AdminAccessRequest
{
    [Required]
    public int EmployeeId { get; init; }
}
