namespace Sentier.Tests;

public sealed class HorlogeReglable : TimeProvider
{
    public DateTimeOffset Maintenant { get; set; } = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Maintenant;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
