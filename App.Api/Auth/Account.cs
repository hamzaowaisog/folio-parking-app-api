namespace App.Api;

public sealed class Account : IAppEntity
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public AccountRole Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public required Employee Employee { get; set; }
    public ICollection<SessionToken> SessionTokens { get; set; } = new List<SessionToken>();
}
