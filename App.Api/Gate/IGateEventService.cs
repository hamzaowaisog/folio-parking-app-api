namespace App.Api;

public interface IGateEventService
{
    Task<IReadOnlyList<VerificationRecord>> GetRecentEventsAsync(int limit, CancellationToken cancellationToken);
}
