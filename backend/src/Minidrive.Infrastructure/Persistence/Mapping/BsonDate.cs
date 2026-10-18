namespace Minidrive.Infrastructure.Persistence.Mapping;

/// <summary>
/// Converts between <see cref="DateTimeOffset"/> used by the domain and the UTC
/// <see cref="DateTime"/> values MongoDB stores.
/// </summary>
internal static class BsonDate
{
    public static DateTime ToBson(DateTimeOffset value) => value.UtcDateTime;

    public static DateTime? ToBson(DateTimeOffset? value) => value?.UtcDateTime;

    public static DateTimeOffset ToOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    public static DateTimeOffset? ToOffset(DateTime? value) =>
        value is null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
}
