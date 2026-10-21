using System.ComponentModel.DataAnnotations;

namespace App.Api;

public sealed record ProfileEmployee(
    int Id,
    string EmployeeId,
    string Name,
    [property: EmailAddress] string Email,
    string? Department,
    string? Role);
