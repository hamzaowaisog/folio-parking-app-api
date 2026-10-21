using System.Text.Json.Serialization;

namespace App.Api;

[JsonConverter(typeof(JsonStringEnumConverter<UnusedParkingCategory>))]
public enum UnusedParkingCategory
{
    [JsonStringEnumMemberName("Unused Dedicated Parking")]
    Dedicated,
    [JsonStringEnumMemberName("Unused Temporary Parking")]
    Temporary
}
