namespace App.Api;

public sealed record ReleaseList(IReadOnlyList<ReleaseRecord> Releases);
