using System.Text.Json.Serialization;

namespace App.Api.Admin;

[JsonConverter(typeof(JsonStringEnumConverter<DatedActivityKind>))]
public enum DatedActivityKind
{
    Release,
    Claim
}
