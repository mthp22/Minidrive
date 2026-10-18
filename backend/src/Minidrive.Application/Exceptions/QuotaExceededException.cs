namespace Minidrive.Application.Exceptions;

/// <summary>Thrown when an upload would exceed the configured per-user storage quota.</summary>
public sealed class QuotaExceededException : AppException
{
    public const string ErrorCode = "quota.exceeded";

    public QuotaExceededException(long quotaBytes, long usedBytes, long requestedBytes)
        : base(
            ErrorCode,
            "The upload would exceed the storage quota for this account.")
    {
        QuotaBytes = quotaBytes;
        UsedBytes = usedBytes;
        RequestedBytes = requestedBytes;
    }

    public long QuotaBytes { get; }

    public long UsedBytes { get; }

    public long RequestedBytes { get; }
}
