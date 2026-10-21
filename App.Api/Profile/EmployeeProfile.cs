namespace App.Api;

public sealed record EmployeeProfile(
    ProfileEmployee Employee,
    ProfileParking Parking,
    IReadOnlyList<Vehicle> Vehicles,
    IReadOnlyList<string> UnavailableFields,
    IReadOnlyList<string> RetryableFields);
