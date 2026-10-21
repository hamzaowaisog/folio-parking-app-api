using System.Text.Json.Serialization;

namespace App.Api;

[JsonConverter(typeof(JsonStringEnumConverter<ProfileParkingStatus>))]
public enum ProfileParkingStatus
{
    [JsonStringEnumMemberName("dedicated")]
    Dedicated,
    [JsonStringEnumMemberName("temporary")]
    Temporary
}
