using NpgsqlTypes;

namespace App.Api;

public enum AccountRole
{
    [PgName("employee")]
    Employee,
    [PgName("gateGuard")]
    GateGuard
}
