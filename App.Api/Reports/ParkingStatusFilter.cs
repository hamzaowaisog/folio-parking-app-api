using System.Text.Json.Serialization;

namespace App.Api;

[JsonConverter(typeof(JsonStringEnumConverter<ParkingStatusFilter>))]
public enum ParkingStatusFilter
{
    Dedicated,
    Temporary
}
