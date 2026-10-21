namespace App.Api;

public sealed record ReleaseRecord(
    int Id,
    DateOnly Date,
    ParkingReleaseStatus Status,
    string? Area,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string LastChangedBy,
    DateTimeOffset LastChangedAt,
    DateTimeOffset ClaimableAt);
