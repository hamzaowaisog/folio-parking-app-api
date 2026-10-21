namespace App.Api;

public sealed record ReportRow(
    DateOnly Date,
    string Week,
    int SpacesReleased,
    int TemporaryClaims,
    float UtilizationPercent);
