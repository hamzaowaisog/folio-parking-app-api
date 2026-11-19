using System.Text.Json.Serialization;
using NpgsqlTypes;

namespace App.Api;

[JsonConverter(typeof(JsonStringEnumConverter<WaitlistEntryStatus>))]
public enum WaitlistEntryStatus
{
    [PgName("Waiting")]
    Waiting,
    [PgName("Offered")]
    Offered,
    [PgName("Claimed")]
    Claimed,
    [PgName("Expired")]
    Expired,
    [PgName("Left")]
    Left
}
