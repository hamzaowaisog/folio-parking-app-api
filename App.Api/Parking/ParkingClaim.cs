namespace App.Api;

public sealed record ParkingClaim(
    int Id,
    DateOnly Date,
    string Plate,
    string Area,
    ParkingClaimStatus Status,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    string LastChangedBy,
    DateTimeOffset LastChangedAt);
