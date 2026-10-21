namespace App.Api;

public sealed class SessionToken : IAppEntity
{
    public Guid Id { get; set; }
    public int AccountId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Account Account { get; set; } = null!;
}
