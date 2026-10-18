namespace Minidrive.Application.Common;

/// <summary>Clamps requested page sizes to a safe, documented range.</summary>
public static class PaginationLimits
{
    public const int DefaultPageSize = 50;

    public const int MaxPageSize = 200;

    /// <summary>Returns a usable page size for a client-supplied value.</summary>
    public static int Normalize(int? requested) =>
        requested is null or <= 0 ? DefaultPageSize : Math.Min(requested.Value, MaxPageSize);
}
