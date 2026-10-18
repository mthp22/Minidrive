using System.Text.RegularExpressions;
using Minidrive.Application.Abstractions.Storage;
using Minidrive.Application.Exceptions;

namespace Minidrive.Application.Common;

/// <summary>
/// Validation rules for the parts reported by a client when completing a multipart upload.
/// </summary>
/// <remarks>
/// The server dictates the part size, so a valid completion must contain exactly the expected
/// number of contiguous parts, numbered 1..N. Because every part except the last is then exactly
/// the configured chunk size — which startup validation keeps at or above the 5 MiB provider
/// minimum — the non-final part size rule holds by construction. The provider's reported object
/// size is additionally checked against the expected size once the upload is completed.
/// </remarks>
public static partial class MultipartPartRules
{
    private const int MaxETagLength = 200;

    [GeneratedRegex("^[!-~]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PrintableAsciiWithoutSpace();

    /// <summary>
    /// Validates the reported parts and returns them ordered by part number with normalised ETags.
    /// </summary>
    /// <exception cref="ValidationFailedException">The parts are missing, duplicated or inconsistent.</exception>
    public static IReadOnlyList<UploadedPart> ValidateAndOrder(
        IReadOnlyList<UploadedPart>? parts,
        int expectedPartCount)
    {
        if (parts is null || parts.Count == 0)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["parts"] = ["At least one part is required."],
            });
        }

        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var part in parts)
        {
            if (part.PartNumber < 1)
            {
                errors["parts"] = ["Part numbers start at 1."];
            }

            if (!IsValidETag(part.ETag))
            {
                errors[$"parts[{part.PartNumber}].etag"] = ["A non-empty ETag is required and must not contain whitespace."];
            }
        }

        var duplicateNumbers = parts
            .GroupBy(part => part.PartNumber)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateNumbers.Length > 0)
        {
            errors["parts"] = [$"Part numbers must be unique; duplicated: {string.Join(", ", duplicateNumbers)}."];
        }

        if (errors.Count > 0)
        {
            throw new ValidationFailedException(errors);
        }

        var ordered = parts.OrderBy(part => part.PartNumber).ToArray();

        for (var index = 0; index < ordered.Length; index++)
        {
            if (ordered[index].PartNumber != index + 1)
            {
                throw new ValidationFailedException(new Dictionary<string, string[]>
                {
                    ["parts"] = ["Part numbers must be contiguous, starting at 1."],
                });
            }
        }

        if (ordered.Length != expectedPartCount)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["parts"] =
                [
                    $"The upload expects {expectedPartCount} part(s) but {ordered.Length} were reported.",
                ],
            });
        }

        return ordered
            .Select(part => new UploadedPart(part.PartNumber, NormalizeETag(part.ETag)))
            .ToArray();
    }

    /// <summary>
    /// Confirms the provider-reported object size is consistent with the minimum non-final part
    /// size: with <paramref name="partCount"/> parts, every part except the last is at least the
    /// minimum, so the object must be at least <c>(partCount - 1) * minimumPartSize</c> bytes.
    /// </summary>
    public static void EnsureMinimumNonFinalPartSizes(long objectSizeBytes, int partCount, long minimumPartSizeBytes)
    {
        if (partCount <= 1)
        {
            return;
        }

        var minimumTotal = (partCount - 1) * minimumPartSizeBytes;
        if (objectSizeBytes < minimumTotal)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["parts"] =
                [
                    "The uploaded object is smaller than the minimum size implied by its part count.",
                ],
            });
        }
    }

    /// <summary>True when the value is usable as an ETag.</summary>
    public static bool IsValidETag(string? etag)
    {
        var normalized = NormalizeETag(etag);
        return normalized.Length is > 0 and <= MaxETagLength && PrintableAsciiWithoutSpace().IsMatch(normalized);
    }

    /// <summary>Removes optional surrounding quotes from an ETag, as returned by S3-compatible providers.</summary>
    public static string NormalizeETag(string? etag)
    {
        if (string.IsNullOrWhiteSpace(etag))
        {
            return string.Empty;
        }

        return etag.Trim().Trim('"');
    }
}
