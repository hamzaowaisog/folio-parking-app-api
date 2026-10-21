using System.Text.Json.Serialization;

namespace App.Api;

[JsonConverter(typeof(JsonStringEnumConverter<VerificationResult>))]
public enum VerificationResult
{
    Authorised,
    [JsonStringEnumMemberName("Existing process")]
    ExistingProcess,
    Denied
}
