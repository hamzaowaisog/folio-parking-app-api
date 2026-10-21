using System.ComponentModel.DataAnnotations;

namespace App.Api.Admin;

public sealed record DirectoryEmployee(
    int Id,
    [property: Required] string Name,
    [property: Required, EmailAddress] string Email);
