namespace Minidrive.UnitTests.TestDoubles;

/// <summary>Deterministic clock so time-based assertions are reproducible.</summary>
public sealed class TestTimeProvider(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => UtcNow;

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
