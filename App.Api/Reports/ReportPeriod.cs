using System.Text.Json.Serialization;

namespace App.Api;

[JsonConverter(typeof(JsonStringEnumConverter<ReportPeriod>))]
public enum ReportPeriod
{
    [JsonStringEnumMemberName("daily")]
    Daily,
    [JsonStringEnumMemberName("weekly")]
    Weekly
}
