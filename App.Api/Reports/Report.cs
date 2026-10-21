namespace App.Api;

public sealed record Report(
    ReportPeriod Period,
    IReadOnlyList<ReportRow> Rows,
    ReportStatistics DailyStatistics,
    ReportStatistics WeeklyStatistics);
