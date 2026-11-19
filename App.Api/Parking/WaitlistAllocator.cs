using Microsoft.EntityFrameworkCore;

namespace App.Api;

/// <summary>Who may take which released spot, and who is in line. See docs/adr/0001.</summary>
internal static class WaitlistAllocator
{
    public static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(now.UtcDateTime);

    /// <summary>Released spots for the date that the employee could claim right now: not cancelled, claimable, unclaimed, and not held for somebody else.</summary>
    public static IQueryable<ParkingRelease> ClaimableFor(AppDb db, DateOnly date, DateTimeOffset now, int employeeId) =>
        db.Set<ParkingRelease>().Where(release => release.Date == date
            && !release.IsCancelled
            && release.ClaimableAt <= now
            && (release.Status == ParkingReleaseStatus.Released || release.Status == ParkingReleaseStatus.Reserved)
            && release.Area != null
            && !db.Set<ParkingClaimEntity>().Any(claim => claim.ReleaseId == release.Id)
            && !db.Set<WaitlistEntry>().Any(entry => entry.ReleaseId == release.Id
                && entry.Status == WaitlistEntryStatus.Offered && entry.EmployeeId != employeeId));

    /// <summary>1-based place among Waiting and Offered entries of the date, in queue (Id) order.</summary>
    public static Task<int> PositionOf(AppDb db, WaitlistEntry entry, CancellationToken cancellationToken) =>
        db.Set<WaitlistEntry>().CountAsync(other => other.Date == entry.Date && other.Id < entry.Id
            && (other.Status == WaitlistEntryStatus.Waiting || other.Status == WaitlistEntryStatus.Offered), cancellationToken)
            .ContinueWith(task => task.Result + 1, cancellationToken);
}
