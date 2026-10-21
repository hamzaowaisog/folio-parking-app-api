namespace App.Api;

public sealed record Availability(
    DateOnly Date,
    int Count,
    IReadOnlyList<AreaCount> Areas,
    IReadOnlyList<ScheduledArea> NotYetClaimable);
