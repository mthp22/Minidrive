using System.Net;
using Minidrive.Application.Common;
using Minidrive.Application.Models;
using Minidrive.Domain.Enums;
using Xunit;

namespace Minidrive.IntegrationTests;

/// <summary>Folder/node lifecycle: create, list, read, rename, move, delete and pagination.</summary>
public sealed class NodeEndpointsTests : ApiTestBase
{
    public NodeEndpointsTests(IntegrationFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Create_folder_returns_201_with_a_location_header()
    {
        var client = CreateClient(NewUserId());
        var name = $"docs-{Guid.NewGuid():N}";

        var response = await PostJsonAsync(client, "/api/v1/folders", new { parentId = (string?)null, name });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var folder = await ReadAsync<NodeDetails>(response);
        Assert.NotNull(folder);
        Assert.Equal(name, folder.Name);
        Assert.Equal(NodeType.Folder, folder.Type);
        Assert.Null(folder.ParentId);

        Assert.NotNull(response.Headers.Location);
        Assert.Equal($"/api/v1/nodes/{folder.Id}", response.Headers.Location!.ToString());

        var fetched = await ReadAsync<NodeDetails>(
            await client.GetAsync($"/api/v1/nodes/{folder.Id}"));
        Assert.NotNull(fetched);
        Assert.Equal(folder.Id, fetched.Id);
    }

    [Fact]
    public async Task Listing_shows_only_direct_children_of_the_requested_folder()
    {
        var client = CreateClient(NewUserId());
        var unique = Guid.NewGuid().ToString("N")[..8];

        var rootId = await CreateFolderAsync(client, $"root-{unique}");
        var childId = await CreateFolderAsync(client, $"child-{unique}", rootId);
        await CreateFolderAsync(client, $"sibling-{unique}", rootId);
        await CreateFolderAsync(client, $"other-{unique}");

        var rootChildren = await ReadAsync<PagedResult<NodeSummary>>(
            await client.GetAsync($"/api/v1/nodes?parentId={rootId}"));
        Assert.NotNull(rootChildren);
        Assert.Equal(2, rootChildren.Items.Count);
        Assert.All(rootChildren.Items, item => Assert.Equal(rootId, item.ParentId));
        Assert.Contains(rootChildren.Items, item => item.Id == childId);

        var topLevel = await ReadAsync<PagedResult<NodeSummary>>(
            await client.GetAsync("/api/v1/nodes"));
        Assert.NotNull(topLevel);
        Assert.DoesNotContain(topLevel.Items, item => item.Id == childId);
        Assert.Equal(2, topLevel.Items.Count); // root-{unique} and other-{unique}
    }

    [Fact]
    public async Task Invalid_folder_names_are_rejected_with_field_errors()
    {
        var client = CreateClient(NewUserId());

        foreach (var name in new[] { "", "  ", "a/b", "..", new string('x', 256) })
        {
            var response = await PostJsonAsync(client, "/api/v1/folders", new { parentId = (string?)null, name });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var problem = await ReadProblemAsync(response);
            Assert.Equal("request.validation", problem.Code);
            Assert.NotNull(problem.Errors);
            Assert.Contains("name", problem.Errors.Keys);
        }
    }

    [Fact]
    public async Task Duplicate_sibling_name_returns_409()
    {
        var client = CreateClient(NewUserId());
        var name = $"dupe-{Guid.NewGuid():N}";
        await CreateFolderAsync(client, name);

        var response = await PostJsonAsync(client, "/api/v1/folders", new { parentId = (string?)null, name });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await ReadProblemAsync(response);
        Assert.Equal("node.name.conflict", problem.Code);
    }

    [Fact]
    public async Task Creating_a_folder_under_an_unknown_parent_returns_404()
    {
        var client = CreateClient(NewUserId());

        var response = await PostJsonAsync(
            client,
            "/api/v1/folders",
            new { parentId = "0123456789abcdef01234567", name = "orphan" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_and_malformed_node_ids_return_404_not_500()
    {
        var client = CreateClient(NewUserId());

        foreach (var nodeId in new[] { "0123456789abcdef01234567", "not-a-valid-id" })
        {
            var response = await client.GetAsync($"/api/v1/nodes/{nodeId}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            var problem = await ReadProblemAsync(response);
            Assert.Equal("resource.notfound", problem.Code);
        }
    }

    [Fact]
    public async Task Rename_updates_the_name_and_conflicts_with_a_sibling()
    {
        var client = CreateClient(NewUserId());
        var unique = Guid.NewGuid().ToString("N")[..8];

        var folderId = await CreateFolderAsync(client, $"before-{unique}");
        await CreateFolderAsync(client, $"taken-{unique}");

        var renameResponse = await PatchJsonAsync(
            client,
            $"/api/v1/nodes/{folderId}",
            new { name = $"after-{unique}" });

        Assert.Equal(HttpStatusCode.OK, renameResponse.StatusCode);

        var renamed = await ReadAsync<NodeDetails>(renameResponse);
        Assert.NotNull(renamed);
        Assert.Equal($"after-{unique}", renamed.Name);

        var conflictResponse = await PatchJsonAsync(
            client,
            $"/api/v1/nodes/{folderId}",
            new { name = $"taken-{unique}" });

        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
        Assert.Equal("node.name.conflict", (await ReadProblemAsync(conflictResponse)).Code);

        var invalidResponse = await PatchJsonAsync(
            client,
            $"/api/v1/nodes/{folderId}",
            new { name = "still/invalid" });

        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
    }

    [Fact]
    public async Task Move_reparents_a_folder_and_its_descendants_follow()
    {
        var client = CreateClient(NewUserId());
        var unique = Guid.NewGuid().ToString("N")[..8];

        var sourceId = await CreateFolderAsync(client, $"source-{unique}");
        var targetId = await CreateFolderAsync(client, $"target-{unique}");
        var childId = await CreateFolderAsync(client, $"child-{unique}", sourceId);

        var moveResponse = await PostJsonAsync(
            client,
            $"/api/v1/nodes/{childId}/move",
            new { parentId = targetId });

        Assert.Equal(HttpStatusCode.OK, moveResponse.StatusCode);

        var moved = await ReadAsync<NodeDetails>(moveResponse);
        Assert.NotNull(moved);
        Assert.Equal(targetId, moved.ParentId);

        var targetChildren = await ReadAsync<PagedResult<NodeSummary>>(
            await client.GetAsync($"/api/v1/nodes?parentId={targetId}"));
        Assert.NotNull(targetChildren);
        Assert.Contains(targetChildren.Items, item => item.Id == childId);

        var sourceChildren = await ReadAsync<PagedResult<NodeSummary>>(
            await client.GetAsync($"/api/v1/nodes?parentId={sourceId}"));
        Assert.NotNull(sourceChildren);
        Assert.DoesNotContain(sourceChildren.Items, item => item.Id == childId);
    }

    [Fact]
    public async Task Moving_a_folder_into_its_own_descendant_returns_409()
    {
        var client = CreateClient(NewUserId());
        var unique = Guid.NewGuid().ToString("N")[..8];

        var parentId = await CreateFolderAsync(client, $"parent-{unique}");
        var childId = await CreateFolderAsync(client, $"child-{unique}", parentId);

        var response = await PostJsonAsync(client, $"/api/v1/nodes/{parentId}/move", new { parentId = childId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("node.move.invalid", (await ReadProblemAsync(response)).Code);
    }

    [Fact]
    public async Task Moving_into_an_unknown_folder_returns_404()
    {
        var client = CreateClient(NewUserId());
        var folderId = await CreateFolderAsync(client, $"movable-{Guid.NewGuid():N}");

        var response = await PostJsonAsync(
            client,
            $"/api/v1/nodes/{folderId}/move",
            new { parentId = "0123456789abcdef01234567" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_hides_the_node_and_releases_its_name()
    {
        var client = CreateClient(NewUserId());
        var name = $"deleted-{Guid.NewGuid():N}";
        var folderId = await CreateFolderAsync(client, name);

        var deleteResponse = await client.DeleteAsync($"/api/v1/nodes/{folderId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await client.GetAsync($"/api/v1/nodes/{folderId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var listing = await ReadAsync<PagedResult<NodeSummary>>(
            await client.GetAsync("/api/v1/nodes"));
        Assert.NotNull(listing);
        Assert.DoesNotContain(listing.Items, item => item.Id == folderId);

        // The partial unique index only considers live nodes, so the name is available again.
        var recreated = await PostJsonAsync(client, "/api/v1/folders", new { parentId = (string?)null, name });
        Assert.Equal(HttpStatusCode.Created, recreated.StatusCode);

        // Deleting twice is still "not found" (idempotent from the caller's perspective).
        var deleteAgain = await client.DeleteAsync($"/api/v1/nodes/{folderId}");
        Assert.Equal(HttpStatusCode.NotFound, deleteAgain.StatusCode);
    }

    [Fact]
    public async Task Deleting_an_unknown_node_returns_404()
    {
        var client = CreateClient(NewUserId());

        var response = await client.DeleteAsync("/api/v1/nodes/0123456789abcdef01234567");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Listings_are_paginated_with_a_stable_cursor()
    {
        var client = CreateClient(NewUserId());
        var unique = Guid.NewGuid().ToString("N")[..8];

        for (var i = 0; i < 5; i++)
        {
            await CreateFolderAsync(client, $"page-{unique}-{i}");
        }

        var seen = new HashSet<string>();
        string? cursor = null;
        var pageCount = 0;

        do
        {
            var url = $"/api/v1/nodes?pageSize=2{(cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}")}";
            var page = await ReadAsync<PagedResult<NodeSummary>>(await client.GetAsync(url));
            Assert.NotNull(page);

            pageCount++;
            Assert.True(page.Items.Count <= 2);

            foreach (var item in page.Items)
            {
                Assert.True(seen.Add(item.Id), $"Item {item.Id} was returned twice across pages.");
            }

            cursor = page.NextCursor;
            Assert.Equal(cursor is not null, page.HasMore);

            Assert.True(pageCount <= 5, "Pagination did not terminate.");
        }
        while (cursor is not null);

        Assert.Equal(5, seen.Count);
        Assert.Equal(3, pageCount);
    }

    [Fact]
    public async Task Malformed_cursor_falls_back_to_the_first_page()
    {
        // Cursors are opaque: a bad token is treated as "no cursor" rather than an error, so
        // clients can recover by simply retrying the first page.
        var client = CreateClient(NewUserId());
        await CreateFolderAsync(client, $"cursor-{Guid.NewGuid():N}");

        var response = await client.GetAsync("/api/v1/nodes?pageSize=2&cursor=not-a-real-cursor");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ReadAsync<PagedResult<NodeSummary>>(response);
        Assert.NotNull(page);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task Listing_under_an_unknown_parent_returns_404()
    {
        var client = CreateClient(NewUserId());

        var unknown = await client.GetAsync("/api/v1/nodes?parentId=0123456789abcdef01234567");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var problem = await ReadProblemAsync(unknown);
        Assert.Equal("resource.notfound", problem.Code);
    }

    [Fact]
    public async Task Folder_size_is_only_computed_when_requested()
    {
        var client = CreateClient(NewUserId());
        var folderId = await CreateFolderAsync(client, $"sized-{Guid.NewGuid():N}");

        var plain = await ReadAsync<NodeDetails>(await client.GetAsync($"/api/v1/nodes/{folderId}"));
        Assert.NotNull(plain);
        Assert.Null(plain.FolderSizeBytes);

        var sized = await ReadAsync<NodeDetails>(
            await client.GetAsync($"/api/v1/nodes/{folderId}?includeFolderSize=true"));
        Assert.NotNull(sized);
        Assert.Equal(0, sized.FolderSizeBytes);
    }
}
