namespace App.Api;

public sealed record WaitlistList(IReadOnlyList<WaitlistRecord> Entries);
