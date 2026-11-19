using Microsoft.EntityFrameworkCore;
using Npgsql;

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

    public static async Task<int> HoldMinutes(AppDb db, CancellationToken cancellationToken)
    {
        var stored = await db.Set<Admin.ParkingSetting>().AsNoTracking()
            .Where(setting => setting.Key == "waitlistHoldMinutes").Select(setting => setting.Value).SingleOrDefaultAsync(cancellationToken);
        return stored is null ? 30 : stored.RootElement.GetInt32();
    }

    /// <summary>
    /// Expires lapsed holds and holds claimable spots for the next people in line, for one date. The caller holds a Serializable
    /// transaction and saves. Every hold starts where the previous one ended (or when the spot became claimable or was given
    /// back), never at the time of the request, so the outcome does not depend on which request gets here first.
    /// </summary>
    public static async Task Evaluate(AppDb db, DateOnly date, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var entries = await db.Set<WaitlistEntry>().Include(entry => entry.Release)
            .Where(entry => entry.Date == date).OrderBy(entry => entry.Id).ToListAsync(cancellationToken);
        if (!entries.Any(entry => entry.Status is WaitlistEntryStatus.Waiting or WaitlistEntryStatus.Offered)) return;

        // a hold on a spot that was cancelled goes back to the queue, keeping its place
        foreach (var entry in entries.Where(entry => entry.Status == WaitlistEntryStatus.Offered && entry.Release is { IsCancelled: true }))
        {
            entry.Status = WaitlistEntryStatus.Waiting;
            entry.ReleaseId = null;
            entry.OfferedAt = null;
            entry.HeldUntil = null;
        }

        var waiting = new Queue<WaitlistEntry>(entries.Where(entry => entry.Status == WaitlistEntryStatus.Waiting));
        if (waiting.Count == 0 && !entries.Any(entry => entry.Status == WaitlistEntryStatus.Offered)) return;

        var holdMinutes = await HoldMinutes(db, cancellationToken);
        var endOfDate = new DateTimeOffset(date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var releases = await db.Set<ParkingRelease>().Where(release => release.Date == date
                && !release.IsCancelled
                && release.ClaimableAt <= now
                && (release.Status == ParkingReleaseStatus.Released || release.Status == ParkingReleaseStatus.Reserved)
                && release.Area != null
                && !db.Set<ParkingClaimEntity>().Any(claim => claim.ReleaseId == release.Id))
            .OrderBy(release => release.Id).ToListAsync(cancellationToken);

        foreach (var release in releases)
        {
            var holder = entries.SingleOrDefault(entry => entry.ReleaseId == release.Id && entry.Status == WaitlistEntryStatus.Offered);
            // when the spot last stopped being held: the end of the latest earlier hold, else when it became claimable
            var at = entries.Where(entry => entry.ReleaseId == release.Id && entry.Status != WaitlistEntryStatus.Offered && entry.HeldUntil != null)
                .Select(entry => entry.HeldUntil!.Value).DefaultIfEmpty(release.ClaimableAt).Max();
            while (true)
            {
                if (holder is not null)
                {
                    if (holder.HeldUntil > now) break;
                    holder.Status = WaitlistEntryStatus.Expired;
                    at = holder.HeldUntil!.Value;
                    holder = null;
                }

                if (!waiting.TryDequeue(out var next)) break;
                next.Status = WaitlistEntryStatus.Offered;
                next.ReleaseId = release.Id;
                next.OfferedAt = at;
                next.HeldUntil = Min(at.AddMinutes(holdMinutes), endOfDate);
                holder = next;
            }
        }
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    /// <summary>Evaluate for a request that only reads: its own transaction, and a clash with another request is fine because that one did the same work.</summary>
    public static async Task EvaluateForRead(AppDb db, DateOnly date, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!await db.Set<WaitlistEntry>().AnyAsync(entry => entry.Date == date
                && (entry.Status == WaitlistEntryStatus.Waiting || entry.Status == WaitlistEntryStatus.Offered), cancellationToken))
            return;

        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        try
        {
            await Evaluate(db, date, now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (IsClash(exception))
        {
            db.ChangeTracker.Clear();
        }
    }

    /// <summary>A unique-index or serialization failure, however deeply the provider wrapped it.</summary>
    public static bool IsClash(Exception exception)
    {
        for (var inner = exception; inner is not null; inner = inner.InnerException)
        {
            if (inner is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure }) return true;
        }

        return false;
    }
}
