using Microsoft.EntityFrameworkCore;

namespace App.Api;

public sealed class GateEventService(AppDb db) : IGateEventService
{
    public async Task<IReadOnlyList<VerificationRecord>> GetRecentEventsAsync(int limit, CancellationToken cancellationToken) =>
        await db.Set<VerificationRecord>()
            .AsNoTracking()
            .OrderByDescending(record => record.Date)
            .ThenByDescending(record => record.Time)
            .ThenByDescending(record => record.Id)
            .Take(Math.Max(0, limit))
            .ToListAsync(cancellationToken);
}
