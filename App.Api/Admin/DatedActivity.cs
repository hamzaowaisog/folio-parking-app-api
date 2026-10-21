using App.Api;

namespace App.Api.Admin;

public sealed record DatedActivity(DateOnly Date, DatedActivityKind Kind, ParkingReleaseStatus Status);
