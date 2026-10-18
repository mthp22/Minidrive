using Minidrive.Domain.Entities;
using Minidrive.Infrastructure.Persistence.Documents;

namespace Minidrive.Infrastructure.Persistence.Mapping;

/// <summary>Maps between the domain <see cref="UploadSession"/> and its persistence document.</summary>
internal static class UploadSessionMapper
{
    public static UploadSessionDocument ToDocument(UploadSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new UploadSessionDocument
        {
            Id = session.Id,
            UserId = session.UserId,
            FileNodeId = session.FileNodeId,
            ParentId = session.ParentId,
            StorageKey = session.StorageKey,
            ProviderUploadId = session.ProviderUploadId,
            ContentType = session.ContentType,
            ExpectedSizeBytes = session.ExpectedSizeBytes,
            ChunkSizeBytes = session.ChunkSizeBytes,
            ExpectedPartCount = session.ExpectedPartCount,
            Status = session.Status,
            CreatedAtUtc = BsonDate.ToBson(session.CreatedAtUtc),
            UpdatedAtUtc = BsonDate.ToBson(session.UpdatedAtUtc),
            ExpiresAtUtc = BsonDate.ToBson(session.ExpiresAtUtc),
            CompletedAtUtc = BsonDate.ToBson(session.CompletedAtUtc),
            FinalETag = session.FinalETag,
            FailureReason = session.FailureReason,
        };
    }

    public static UploadSession ToDomain(UploadSessionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new UploadSession(
            id: document.Id,
            userId: document.UserId,
            fileNodeId: document.FileNodeId,
            parentId: document.ParentId,
            storageKey: document.StorageKey,
            providerUploadId: document.ProviderUploadId,
            contentType: document.ContentType,
            expectedSizeBytes: document.ExpectedSizeBytes,
            chunkSizeBytes: document.ChunkSizeBytes,
            expectedPartCount: document.ExpectedPartCount,
            status: document.Status,
            createdAtUtc: BsonDate.ToOffset(document.CreatedAtUtc),
            updatedAtUtc: BsonDate.ToOffset(document.UpdatedAtUtc),
            expiresAtUtc: BsonDate.ToOffset(document.ExpiresAtUtc),
            completedAtUtc: BsonDate.ToOffset(document.CompletedAtUtc),
            finalETag: document.FinalETag,
            failureReason: document.FailureReason);
    }
}
