using System.Net;
using System.Text.RegularExpressions;
using Minidrive.Application.Models;
using Xunit;

namespace Minidrive.IntegrationTests;

/// <summary>Name search: literal (regex-escaped) case-insensitive matching, scoping and validation.</summary>
public sealed class SearchEndpointsTests : ApiTestBase
{
    public SearchEndpointsTests(IntegrationFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Search_is_case_insensitive_and_finds_matches_across_the_tree()
    {
        var client = CreateClient(NewUserId());
        var parentId = await CreateFolderAsync(client, "Work");
        await CreateFolderAsync(client, "Reports 2026", parentId);
        await CreateFolderAsync(client, "holiday", null);

        var response = await client.GetAsync("/api/v1/search?q=REPORT");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var results = await ReadAsync<List<NodeDetails>>(response);
        Assert.NotNull(results);
        Assert.Equal(["Reports 2026"], results.Select(result => result.Name));
    }

    [Fact]
    public async Task Search_treats_regex_metacharacters_literally()
    {
        var client = CreateClient(NewUserId());
        await CreateFolderAsync(client, "report(1)");
        await CreateFolderAsync(client, "reportx1x");
        await CreateFolderAsync(client, "notes");

        // "(1)" must match the literal name only — as a regex group it would also match "x1x",
        // and an unescaped "(" would be an invalid pattern and a 500.
        var group = await ReadAsync<List<NodeDetails>>(
            await client.GetAsync($"/api/v1/search?q={Uri.EscapeDataString("(1)")}")!);
        Assert.NotNull(group);
        Assert.Equal(["report(1)"], group.Select(result => result.Name));

        // "." must be literal too: it must not turn into "any character" matching "notes".
        var dot = await ReadAsync<List<NodeDetails>>(
            await client.GetAsync($"/api/v1/search?q={Uri.EscapeDataString(".es")}")!);
        Assert.NotNull(dot);
        Assert.Empty(dot);
    }

    [Fact]
    public async Task Search_never_returns_other_users_or_unfinished_uploads()
    {
        var mine = CreateClient(NewUserId());
        var theirs = CreateClient(NewUserId());

        await CreateFolderAsync(mine, "shared-report");
        await CreateFolderAsync(theirs, "shared-report-theirs");

        // Pending files reserve their names but must not be searchable before completion.
        var pending = await PostJsonAsync(
            mine,
            "/api/v1/uploads/initiate",
            new { parentId = (string?)null, fileName = "shared-report-pending.bin", contentType = (string?)null, sizeBytes = 100L });
        Assert.Equal(HttpStatusCode.Created, pending.StatusCode);

        var response = await mine.GetAsync("/api/v1/search?q=shared-report");
        var results = await ReadAsync<List<NodeDetails>>(response);

        Assert.NotNull(results);
        Assert.Equal(["shared-report"], results.Select(result => result.Name));
    }

    [Fact]
    public async Task Search_requires_a_query_and_caps_the_result_count()
    {
        var client = CreateClient(NewUserId());
        for (var i = 0; i < 6; i++)
        {
            await CreateFolderAsync(client, $"notes-{i}");
        }

        foreach (var url in new[] { "/api/v1/search", "/api/v1/search?q=", "/api/v1/search?q=%20%20" })
        {
            var missing = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            Assert.Contains("q", (await ReadProblemAsync(missing)).Errors!.Keys);
        }

        var capped = await ReadAsync<List<NodeDetails>>(
            await client.GetAsync("/api/v1/search?q=notes&pageSize=3")!);
        Assert.NotNull(capped);
        Assert.Equal(3, capped.Count);
    }

    [Fact]
    public async Task Search_requires_authentication()
    {
        var anonymous = CreateAnonymousClient();

        var response = await anonymous.GetAsync("/api/v1/search?q=anything");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Search_results_are_ordered_by_name()
    {
        var client = CreateClient(NewUserId());
        foreach (var name in new[] { "zebra-file", "alpha-file", "middle-file" })
        {
            await CreateFolderAsync(client, name);
        }

        var results = await ReadAsync<List<NodeDetails>>(
            await client.GetAsync("/api/v1/search?q=-file")!);

        Assert.NotNull(results);
        Assert.Equal(["alpha-file", "middle-file", "zebra-file"], results.Select(result => result.Name).Order());
        Assert.All(results, result => Assert.True(Regex.IsMatch(result.Name, "-file", RegexOptions.IgnoreCase)));
    }
}
