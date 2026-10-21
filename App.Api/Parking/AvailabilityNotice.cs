using System.ComponentModel.DataAnnotations;

namespace App.Api;

public sealed record AvailabilityNotice(string Message, [property: EmailAddress] string Email);
