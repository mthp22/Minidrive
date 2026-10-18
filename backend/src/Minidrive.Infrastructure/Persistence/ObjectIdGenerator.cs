using Minidrive.Application.Abstractions;
using MongoDB.Bson;

namespace Minidrive.Infrastructure.Persistence;

/// <summary>
/// Generates MongoDB ObjectId values in their 24-character hexadecimal form. ObjectIds are
/// roughly time-ordered, which keeps cursor pagination stable and index-friendly.
/// </summary>
public sealed class ObjectIdGenerator : IIdGenerator
{
    public string NewId() => ObjectId.GenerateNewId().ToString();
}
