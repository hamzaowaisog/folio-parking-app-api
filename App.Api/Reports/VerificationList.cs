namespace App.Api;

public sealed record VerificationList(IReadOnlyList<Verification> Records, int Total);
