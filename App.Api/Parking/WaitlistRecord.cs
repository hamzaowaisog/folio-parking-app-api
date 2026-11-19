namespace App.Api;

/// <summary>Position is 1-based among the people still in line for the date; it is null once the entry is Claimed, Expired or Left.</summary>
public sealed record WaitlistRecord(DateOnly Date, int? Position, WaitlistEntryStatus Status, DateTimeOffset? HeldUntil, string? Area);
