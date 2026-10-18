using Minidrive.Application.Abstractions;

namespace Minidrive.UnitTests.TestDoubles;

/// <summary>Deterministic id generator producing valid 24-character ObjectId hex strings.</summary>
public sealed class FixedIdGenerator : IIdGenerator
{
    private int _counter;

    public string NewId() => Interlocked.Increment(ref _counter).ToString("x24");

    /// <summary>Pins the next generated id so tests can reference it.</summary>
    public string NextId => (_counter + 1).ToString("x24");
}
