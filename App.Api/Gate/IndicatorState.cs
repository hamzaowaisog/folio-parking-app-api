using System.Text.Json.Serialization;

namespace App.Api;

[JsonConverter(typeof(JsonStringEnumConverter<IndicatorState>))]
public enum IndicatorState
{
    [JsonStringEnumMemberName("green")]
    Green,
    [JsonStringEnumMemberName("off")]
    Off,
    [JsonStringEnumMemberName("not used")]
    NotUsed
}
