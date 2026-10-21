using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;

namespace App.Api;

internal sealed class AuthEndpoints : IApiEndpointModule
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/auth/register", async (
                RegisterRequest request,
                AppDb db,
                IPasswordHasher<Account> passwordHasher,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                var email = request.Email?.Trim();
                if (string.IsNullOrWhiteSpace(request.Name)
                    || string.IsNullOrWhiteSpace(email)
                    || !new EmailAddressAttribute().IsValid(email)
                    || string.IsNullOrWhiteSpace(request.Password))
                {
                    return Results.Json(new Error("The submitted values are not valid.", "BAD_REQUEST"), statusCode: StatusCodes.Status400BadRequest);
                }

                var normalizedEmail = email.ToLowerInvariant();
                if (await db.Accounts.AnyAsync(account => account.Email.ToLower() == normalizedEmail, cancellationToken))
                {
                    return Conflict();
                }

                var employee = await db.Employees.SingleOrDefaultAsync(
                    candidate => candidate.Email.ToLower() == normalizedEmail,
                    cancellationToken);
                if (employee is null)
                {
                    employee = new Employee
                    {
                        EmployeeId = $"EMP-{Guid.NewGuid():N}"[..16].ToUpperInvariant(),
                        Name = request.Name.Trim(),
                        Email = email,
                        Role = "Employee",
                        // a dedicated space is assigned by an administrator, never taken at sign-up
                        ParkingStatus = EmployeeParkingStatus.Temporary
                    };
                }
                else if (employee.Account is not null)
                {
                    return Conflict();
                }

                var account = new Account
                {
                    Email = email,
                    PasswordHash = string.Empty,
                    Employee = employee,
                    Role = AccountRole.Employee,
                    CreatedAt = AuthTime.ToDatabasePrecision(timeProvider.GetUtcNow())
                };
                account.PasswordHash = passwordHasher.HashPassword(account, request.Password);
                db.Accounts.Add(account);

                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                {
                    return Conflict();
                }

                return Results.Json(
                    new RegistrationResult("Your account is ready.", email, "created"),
                    statusCode: StatusCodes.Status201Created);
            })
            .WithName("registerAccount")
            .AllowAnonymous()
            .Produces<RegistrationResult>(StatusCodes.Status201Created)
            .Produces<Error>(StatusCodes.Status400BadRequest)
            .Produces<Error>(StatusCodes.Status409Conflict);

        endpoints.MapPost("/auth/login", async (
                LoginRequest request,
                AppDb db,
                IPasswordHasher<Account> passwordHasher,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                var email = request.Email?.Trim();
                if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.Password))
                {
                    return Unauthorized();
                }

                var normalizedEmail = email.ToLowerInvariant();
                var account = await db.Accounts
                    .Include(candidate => candidate.Employee)
                    .SingleOrDefaultAsync(candidate => candidate.Email.ToLower() == normalizedEmail, cancellationToken);
                if (account is null || passwordHasher.VerifyHashedPassword(account, account.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
                {
                    return Unauthorized();
                }

                var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                    .TrimEnd('=')
                    .Replace('+', '-')
                    .Replace('/', '_');
                var expiresAt = AuthTime.ToDatabasePrecision(timeProvider.GetUtcNow().AddHours(1));
                db.SessionTokens.Add(new SessionToken
                {
                    Id = Guid.NewGuid(),
                    AccountId = account.Id,
                    TokenHash = SessionTokenHasher.Hash(token),
                    ExpiresAt = expiresAt
                });
                await db.SaveChangesAsync(cancellationToken);

                var sessionEmployee = new SessionEmployee(
                    account.Employee.Id,
                    account.Employee.Name,
                    account.Employee.Email,
                    await AdministratorCheck.SessionRoleAsync(db, account, cancellationToken),
                    account.Employee.ParkingStatus == EmployeeParkingStatus.Dedicated
                        ? "dedicated"
                        : "temporary");
                return Results.Json(new Session(token, "Bearer", 3600, sessionEmployee));
            })
            .WithName("login")
            .AllowAnonymous()
            .Produces<Session>(StatusCodes.Status200OK)
            .Produces<Error>(StatusCodes.Status401Unauthorized)
            .Produces<Error>(StatusCodes.Status403Forbidden);

        endpoints.MapPost("/auth/logout", async (HttpContext context, AppDb db, TimeProvider timeProvider) =>
            {
                var tokenId = context.User.FindFirst("sessionTokenId")?.Value;
                if (Guid.TryParse(tokenId, out var id))
                {
                    var sessionToken = await db.SessionTokens.SingleOrDefaultAsync(token => token.Id == id, context.RequestAborted);
                    if (sessionToken is not null && sessionToken.RevokedAt is null)
                    {
                        sessionToken.RevokedAt = AuthTime.ToDatabasePrecision(timeProvider.GetUtcNow());
                        await db.SaveChangesAsync(context.RequestAborted);
                    }
                }

                return Results.NoContent();
            })
            .WithName("logout")
            .RequireAuthorization()
            .Produces(StatusCodes.Status204NoContent);
    }

    private static IResult Conflict() => Results.Json(
        new Error("No space could be claimed. The last free space was taken just now.", "CONFLICT"),
        statusCode: StatusCodes.Status409Conflict);

    private static IResult Unauthorized() => Results.Json(
        new Error("Sign in to continue.", "UNAUTHORIZED"),
        statusCode: StatusCodes.Status401Unauthorized);
}
