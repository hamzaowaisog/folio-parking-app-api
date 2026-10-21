namespace App.Api;

public sealed record ScheduledArea(string Area, int Count, DateTimeOffset ClaimableAt);
