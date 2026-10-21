namespace App.Api;

internal static class AuthTime
{
    public static DateTimeOffset ToDatabasePrecision(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Ticks - utc.Ticks % 10, TimeSpan.Zero);
    }
}
