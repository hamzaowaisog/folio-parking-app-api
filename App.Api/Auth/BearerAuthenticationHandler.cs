using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;

namespace App.Api;

internal sealed class BearerAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly AppDb _db;
    private readonly TimeProvider _timeProvider;

    public BearerAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AppDb db,
        TimeProvider timeProvider)
        : base(options, logger, encoder)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var accessToken = authorization["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return AuthenticateResult.Fail("The bearer token is invalid.");
        }

        var tokenHash = SessionTokenHasher.Hash(accessToken);
        var sessionToken = await _db.SessionTokens
            .Include(token => token.Account)
            .ThenInclude(account => account.Employee)
            .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, Context.RequestAborted);
        if (sessionToken is null
            || sessionToken.RevokedAt is not null
            || sessionToken.ExpiresAt <= AuthTime.ToDatabasePrecision(_timeProvider.GetUtcNow()))
        {
            return AuthenticateResult.Fail("The bearer token is invalid or expired.");
        }

        var account = sessionToken.Account;
        var employee = account.Employee;
        var claims = new List<Claim>
        {
            new System.Security.Claims.Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new System.Security.Claims.Claim("accountId", account.Id.ToString()),
            new System.Security.Claims.Claim("employeeId", employee.Id.ToString()),
            new System.Security.Claims.Claim("sessionTokenId", sessionToken.Id.ToString()),
            new System.Security.Claims.Claim(ClaimTypes.Name, employee.Name),
            new System.Security.Claims.Claim(ClaimTypes.Email, employee.Email),
            new System.Security.Claims.Claim("role", account.Role == AccountRole.GateGuard ? "gateGuard" : "employee"),
            new System.Security.Claims.Claim(ClaimTypes.Role, account.Role == AccountRole.GateGuard ? "gateGuard" : "employee")
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        await Response.WriteAsJsonAsync(new Error("Sign in to continue.", "UNAUTHORIZED"), Context.RequestAborted);
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        await Response.WriteAsJsonAsync(new Error("You do not have access to this page.", "FORBIDDEN"), Context.RequestAborted);
    }
}
