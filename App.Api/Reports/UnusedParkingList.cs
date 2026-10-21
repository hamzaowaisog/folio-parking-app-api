namespace App.Api;

public sealed record UnusedParkingList(IReadOnlyList<UnusedParkingRecord> Records, int Total);
