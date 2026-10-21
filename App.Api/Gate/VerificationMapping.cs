namespace App.Api;

public static class VerificationMapping
{
    public static Verification ToVerification(VerificationRecord record) => new(
        checked((int)record.Id),
        record.Plate,
        record.Driver,
        record.Date,
        record.Time,
        ParseResult(record.Result),
        record.EntryStatus,
        ParseIndicatorState(record.IndicatorState),
        record.Reason,
        record.PermanentSticker);

    public static string ToStoredResult(VerificationResult result) => result switch
    {
        VerificationResult.Authorised => "Authorised",
        VerificationResult.ExistingProcess => "Existing process",
        VerificationResult.Denied => "Denied",
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Unsupported verification result.")
    };

    public static bool TryParseResult(string result, out VerificationResult verificationResult)
    {
        verificationResult = result switch
        {
            "Authorised" => VerificationResult.Authorised,
            "Existing process" => VerificationResult.ExistingProcess,
            "Denied" => VerificationResult.Denied,
            _ => default
        };
        return result is "Authorised" or "Existing process" or "Denied";
    }

    private static VerificationResult ParseResult(string result) => result switch
    {
        "Authorised" => VerificationResult.Authorised,
        "Existing process" => VerificationResult.ExistingProcess,
        "Denied" => VerificationResult.Denied,
        _ => throw new InvalidOperationException($"Unsupported verification result '{result}'.")
    };

    private static IndicatorState ParseIndicatorState(string state) => state switch
    {
        "green" => IndicatorState.Green,
        "off" => IndicatorState.Off,
        "not used" => IndicatorState.NotUsed,
        _ => throw new InvalidOperationException($"Unsupported indicator state '{state}'.")
    };
}
