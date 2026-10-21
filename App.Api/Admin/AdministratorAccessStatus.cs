using System.Text.Json.Serialization;

namespace App.Api.Admin;

[JsonConverter(typeof(JsonStringEnumConverter<AdministratorAccessStatus>))]
public enum AdministratorAccessStatus
{
    [JsonStringEnumMemberName("active")]
    Active,
    [JsonStringEnumMemberName("revoked")]
    Revoked
}
