using System.Text.Json.Serialization;
using NpgsqlTypes;

namespace App.Api;

[JsonConverter(typeof(JsonStringEnumConverter<ParkingClaimStatus>))]
public enum ParkingClaimStatus
{
    [PgName("Claimed")]
    Claimed
}
