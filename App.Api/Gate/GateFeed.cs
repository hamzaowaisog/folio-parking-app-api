namespace App.Api;

public sealed record GateFeed(
    CameraStatus CameraStatus,
    IReadOnlyList<Verification> Events);
