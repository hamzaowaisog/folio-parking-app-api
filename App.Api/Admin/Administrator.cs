using System.ComponentModel.DataAnnotations;

namespace App.Api.Admin;

public sealed record Administrator(
    int Id,
    [property: Required] string Name,
    [property: Required, EmailAddress] string Email,
    [property: Required] AdministratorAccessStatus AccessStatus);
