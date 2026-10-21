namespace App.Api;

public sealed record Verification(
    int Id,
    string Plate,
    string Driver,
    DateOnly Date,
    TimeOnly Time,
    VerificationResult Result,
    string EntryStatus,
    IndicatorState IndicatorState,
    string Reason,
    bool PermanentSticker);
