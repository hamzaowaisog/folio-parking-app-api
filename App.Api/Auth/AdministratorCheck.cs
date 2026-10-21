using Microsoft.EntityFrameworkCore;

namespace App.Api;

/// <summary>Whether an employee holds active parking-administrator access (the AdministratorAccess table).</summary>
public static class AdministratorCheck
{
    public static Task<bool> IsActiveAsync(AppDb db, int employeeId, CancellationToken cancellationToken) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (
                SELECT 1 FROM "AdministratorAccess"
                WHERE "EmployeeId" = {employeeId} AND "AccessStatus"::text = 'active'
            ) AS "Value"
            """).SingleAsync(cancellationToken);

    /// <summary>The session role the contract names: gateGuard, parkingAdministrator or employee.</summary>
    public static async Task<string> SessionRoleAsync(AppDb db, Account account, CancellationToken cancellationToken) =>
        account.Role == AccountRole.GateGuard ? "gateGuard"
        : await IsActiveAsync(db, account.EmployeeId, cancellationToken) ? "parkingAdministrator"
        : "employee";
}
