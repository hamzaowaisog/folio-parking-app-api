using System.ComponentModel.DataAnnotations;

namespace App.Api;

public sealed record DashboardEmployee(
    int Id,
    string Name,
    [property: EmailAddress] string Email,
    ProfileParkingStatus ParkingStatus);
