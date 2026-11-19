namespace App.Api;

/// <summary>Offered counts every entry that was ever held a spot; StillWaiting counts entries still in the Waiting state after the lapsed holds were settled.</summary>
public sealed record WaitlistReportRow(DateOnly Date, int Joined, int Offered, int Claimed, int Expired, int StillWaiting);

public sealed record WaitlistReport(IReadOnlyList<WaitlistReportRow> Days);
