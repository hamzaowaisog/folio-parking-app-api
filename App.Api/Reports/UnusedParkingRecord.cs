namespace App.Api;

public sealed record UnusedParkingRecord(
    int Id,
    UnusedParkingCategory Category,
    string Employee,
    string Vehicle,
    DateOnly Date);
