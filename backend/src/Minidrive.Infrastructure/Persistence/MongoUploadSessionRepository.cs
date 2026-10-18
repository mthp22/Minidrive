using Minidrive.Application.Abstractions.Persistence;
using Minidrive.Application.Common;
using Minidrive.Domain.Entities;
using Minidrive.Domain.Enums;
using Minidrive.Infrastructure.Persistence.Documents;
using Minidrive.Infrastructure.Persistence.Mapping;
using MongoDB.Driver;

namespace Minidrive.Infrastructure.Persistence;

/// <summary>
/// MongoDB implementation of <see cref="IUploadSessionRepository"/>. State changes are applied with
/// a conditional update that matches the expected status, so concurrent completion, abort and
/// expiry attempts cannot both succeed.
/// </summary>
public sealed class MongoUploadSessionRepository : IUploadSessionRepository
{
    private static readonly FilterDefinitionBuilder<UploadSessionDocument> Filters = Builders<UploadSessionDocument>.Filter;
    private static readonly SortDefinitionBuilder<UploadSessionDocument> Sorts = Builders<UploadSessionDocument>.Sort;
    private static readonly UpdateDefinitionBuilder<UploadSessionDocument> Updates = Builders<UploadSessionDocument>.Update;

    private readonly IMongoCollection<UploadSessionDocument> _collection;

    public MongoUploadSessionRepository(IMongoDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _collection = database.GetCollection<UploadSessionDocument>(MongoCollections.UploadSessions);
    }

    public async Task InsertAsync(UploadSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        await _collection.InsertOneAsync(UploadSessionMapper.ToDocument(session), cancellationToken: cancellationToken);
    }

    public async Task<UploadSession?> FindAsync(string userId, string sessionId, CancellationToken cancellationToken)
    {
        if (!IdFormat.IsValidObjectId(sessionId))
        {
            return null;
        }

        var filter = Filters.And(Filters.Eq(document => document.Id, sessionId), Filters.Eq(document => document.UserId, userId));
        var document = await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : UploadSessionMapper.ToDomain(document);
    }

    public async Task<UploadSession?> TransitionAsync(
        UploadSessionTransition transition,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transition);

        if (!IdFormat.IsValidObjectId(transition.SessionId))
        {
            return null;
        }

        var filter = Filters.And(
            Filters.Eq(document => document.Id, transition.SessionId),
            Filters.Eq(document => document.UserId, transition.UserId),
            Filters.Eq(document => document.Status, transition.ExpectedStatus));

        var update = Updates
            .Set(document => document.Status, transition.NewStatus)
            .Set(document => document.UpdatedAtUtc, BsonDate.ToBson(now));

        if (!string.IsNullOrEmpty(transition.ProviderUploadId))
        {
            update = update.Set(document => document.ProviderUploadId, transition.ProviderUploadId);
        }

        if (transition.FinalETag is not null)
        {
            update = update.Set(document => document.FinalETag, transition.FinalETag);
        }

        if (transition.FailureReason is not null)
        {
            update = update.Set(document => document.FailureReason, transition.FailureReason);
        }

        if (transition.CompletedAtUtc is not null)
        {
            update = update.Set(document => document.CompletedAtUtc, BsonDate.ToBson(transition.CompletedAtUtc));
        }

        var options = new FindOneAndUpdateOptions<UploadSessionDocument> { ReturnDocument = ReturnDocument.After };
        var document = await _collection.FindOneAndUpdateAsync(filter, update, options, cancellationToken);
        return document is null ? null : UploadSessionMapper.ToDomain(document);
    }

    public async Task<IReadOnlyList<UploadSession>> ListExpiredAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken)
    {
        var filter = Filters.And(
            Filters.In(document => document.Status, [UploadStatus.Initiating, UploadStatus.Active]),
            Filters.Lte(document => document.ExpiresAtUtc, BsonDate.ToBson(now)));

        var documents = await _collection
            .Find(filter)
            .Sort(Sorts.Ascending(document => document.ExpiresAtUtc))
            .Limit(limit)
            .ToListAsync(cancellationToken);

        return documents.Select(UploadSessionMapper.ToDomain).ToList();
    }
}
