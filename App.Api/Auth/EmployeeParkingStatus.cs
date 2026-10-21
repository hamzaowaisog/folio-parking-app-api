using NpgsqlTypes;

namespace App.Api;

public enum EmployeeParkingStatus
{
    [PgName("Dedicated")]
    Dedicated,
    [PgName("Temporary")]
    Temporary
}
