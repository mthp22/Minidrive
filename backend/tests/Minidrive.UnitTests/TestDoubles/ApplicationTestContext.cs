using Minidrive.Application.Options;
using Minidrive.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Minidrive.UnitTests.TestDoubles;

/// <summary>Wires the application services to in-memory doubles for use-case tests.</summary>
internal sealed class ApplicationTestContext
{
    public ApplicationTestContext(UploadOptions? options = null)
    {
        Options = options ?? new UploadOptions();
        OptionsAccessor = Microsoft.Extensions.Options.Options.Create(Options);
    }

    public InMemoryNodeRepository Nodes { get; } = new();

    public InMemoryUploadSessionRepository Sessions { get; } = new();

    public FakeObjectStorage Storage { get; } = new();

    public InMemoryStorageCleanupQueue CleanupQueue { get; } = new();

    public FixedIdGenerator Ids { get; } = new();

    public TestTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    public UploadOptions Options { get; }

    public IOptions<UploadOptions> OptionsAccessor { get; }

    public NodeQueryService QueryService => new(Nodes, Storage, OptionsAccessor);

    public NodeCommandService CommandService => new(Nodes, CleanupQueue, Ids, Clock);

    public UploadService UploadService => new(
        Nodes,
        Sessions,
        Storage,
        CleanupQueue,
        Ids,
        Clock,
        OptionsAccessor,
        NullLogger<UploadService>.Instance);

    public MaintenanceService MaintenanceService => new(
        Sessions,
        Nodes,
        Storage,
        CleanupQueue,
        Clock,
        NullLogger<MaintenanceService>.Instance);
}
