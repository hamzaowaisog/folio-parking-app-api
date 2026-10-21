using System.Text.Json.Serialization;
using NpgsqlTypes;

namespace App.Api;

[JsonConverter(typeof(JsonStringEnumConverter<ParkingReleaseStatus>))]
public enum ParkingReleaseStatus
{
    [PgName("Reserved")]
    Reserved,
    [PgName("Released")]
    Released,
    [PgName("Claimed")]
    Claimed
}
