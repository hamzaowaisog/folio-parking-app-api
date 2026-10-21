using System.Text.Json.Serialization;

namespace App.Api;

[JsonConverter(typeof(JsonStringEnumConverter<CameraStatus>))]
public enum CameraStatus
{
    [JsonStringEnumMemberName("online")]
    Online,
    [JsonStringEnumMemberName("unavailable")]
    Unavailable
}
