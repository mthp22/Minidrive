using System.Net;
using Minidrive.Application.Models;
using Minidrive.Domain.Enums;
using Xunit;

namespace Minidrive.IntegrationTests;

/// <summary>
/// Trash lifecycle: delete lists an item with its purge time, restore revives it, permanent delete
/// and empty-trash remove it, and everything stays scoped to the owner.
/// </summary>
public sealed class TrashEndpointsTests : ApiTestBase
{
    public TrashEndpointsTests(IntegrationFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Delete_then_list_shows_the_item_with_a_future_purge_time()
    {
        var client = CreateClient(NewUserId());
        var folderId = await CreateFolderAsync(client, $"trash-{Guid.NewGuid():N}");

        var delete = await client.DeleteAsync($"/api/v1/nodes/{folderId}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var trash = await ReadAsync<List<TrashItem>>(await client.GetAsync("/api/v1/trash"));
        Assert.NotNull(trash);
        var item = Assert.Single(trash);
        Assert.Equal(folderId, item.Id);
        Assert.Equal(NodeType.Folder, item.Type);
        Assert.True(item.PurgeAfterUtc > item.DeletedAtUtc);
    }

    [Fact]
    public async Task Restore_returns_the_item_to_the_listing_and_removes_it_from_the_trash()
    {
        var client = CreateClient(NewUserId());
        var folderId = await CreateFolderAsync(client, $"revive-{Guid.NewGuid():N}");

        await client.DeleteAsync($"/api/v1/nodes/{folderId}");
        var restore = await client.PostAsync($"/api/v1/trash/{folderId}/restore", null);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);

        var restored = await ReadAsync<NodeDetails>(restore);
        Assert.NotNull(restored);
        Assert.Equal(folderId, restored.Id);

        // It is live again and no longer in the trash.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/nodes/{folderId}")).StatusCode);
        var trash = await ReadAsync<List<TrashItem>>(await client.GetAsync("/api/v1/trash"));
        Assert.NotNull(trash);
        Assert.DoesNotContain(trash, item => item.Id == folderId);
    }

    [Fact]
    public async Task Permanent_delete_removes_the_item_from_the_trash_forever()
    {
        var client = CreateClient(NewUserId());
        var folderId = await CreateFolderAsync(client, $"gone-{Guid.NewGuid():N}");

        await client.DeleteAsync($"/api/v1/nodes/{folderId}");
        var purge = await client.DeleteAsync($"/api/v1/trash/{folderId}");
        Assert.Equal(HttpStatusCode.NoContent, purge.StatusCode);

        var trash = await ReadAsync<List<TrashItem>>(await client.GetAsync("/api/v1/trash"));
        Assert.NotNull(trash);
        Assert.DoesNotContain(trash, item => item.Id == folderId);

        // Restoring a purged node is a 404: it no longer exists.
        var restore = await client.PostAsync($"/api/v1/trash/{folderId}/restore", null);
        Assert.Equal(HttpStatusCode.NotFound, restore.StatusCode);
    }

    [Fact]
    public async Task EmptyTrash_clears_every_trash_item_for_the_caller()
    {
        var client = CreateClient(NewUserId());
        var firstId = await CreateFolderAsync(client, $"one-{Guid.NewGuid():N}");
        var secondId = await CreateFolderAsync(client, $"two-{Guid.NewGuid():N}");
        _ = await CreateFolderAsync(client, $"three-{Guid.NewGuid():N}");

        await client.DeleteAsync($"/api/v1/nodes/{firstId}");
        await client.DeleteAsync($"/api/v1/nodes/{secondId}");

        var trash = await ReadAsync<List<TrashItem>>(await client.GetAsync("/api/v1/trash"));
        Assert.NotNull(trash);
        Assert.Equal(2, trash.Count);

        var empty = await client.DeleteAsync("/api/v1/trash");
        Assert.Equal(HttpStatusCode.NoContent, empty.StatusCode);

        var after = await ReadAsync<List<TrashItem>>(await client.GetAsync("/api/v1/trash"));
        Assert.NotNull(after);
        Assert.Empty(after);
    }

    [Fact]
    public async Task Trash_is_scoped_to_the_owner()
    {
        var owner = CreateClient(NewUserId());
        var stranger = CreateClient(NewUserId());
        var folderId = await CreateFolderAsync(owner, $"private-{Guid.NewGuid():N}");
        await owner.DeleteAsync($"/api/v1/nodes/{folderId}");

        // The stranger sees an empty trash and cannot restore or purge the owner's item.
        var trash = await ReadAsync<List<TrashItem>>(await stranger.GetAsync("/api/v1/trash"));
        Assert.NotNull(trash);
        Assert.Empty(trash);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await stranger.PostAsync($"/api/v1/trash/{folderId}/restore", null)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await stranger.DeleteAsync($"/api/v1/trash/{folderId}")).StatusCode);
    }
}
