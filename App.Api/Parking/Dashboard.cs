namespace App.Api;

public sealed record Dashboard(
    DashboardEmployee Employee,
    DateOnly OfficeDate,
    IReadOnlyList<ReleaseRecord> Releases,
    Availability Availability,
    ParkingClaim? CurrentClaim,
    AvailabilityNotice? Notice,
    IReadOnlyList<string> UnavailableFields);
