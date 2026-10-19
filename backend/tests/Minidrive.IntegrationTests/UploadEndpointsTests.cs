using System.Net;
using Minidrive.Application.Common;
using Minidrive.Application.Models;
using Minidrive.Domain.Enums;
using Xunit;

namespace Minidrive.IntegrationTests;

/// <summary>The three-phase multipart upload pipeline: initiate, pre-sign parts, complete, abort.</summary>
public sealed class UploadEndpointsTests : ApiTestBase
{
    private const long TenMiB = 10L * 1024 * 1024;

    public UploadEndpointsTests(IntegrationFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Full_single_part_flow_produces_a_ready_file_with_a_download_url()
    {
        var client = CreateClient(NewUserId());
        var folderId = await CreateFolderAsync(client, $"upload-{Guid.NewGuid():N}");
        var fileName = "report.txt";
        const long sizeBytes = 1000;

        var initiate = await PostJsonAsync(
            client,
            "/api/v1/uploads/initiate",
            new { parentId = folderId, fileName, contentType = "text/plain", sizeBytes });

        Assert.Equal(HttpStatusCode.Created, initiate.StatusCode);

        var session = await ReadAsync<InitiateUploadResponse>(initiate);
        Assert.NotNull(session);
        Assert.Equal("Active", session.Status);
        Assert.Equal(1, session.ExpectedPartCount);
        Assert.Equal(1, session.PartNumberMinimum);
        Assert.Equal(1, session.PartNumberMaximum);
        Assert.True(session.ChunkSizeBytes >= 5 * 1024 * 1024, "Chunk size must respect the 5 MiB provider minimum.");
        Assert.NotEqual(session.UploadId, session.FileId); // session id ≠ node id

        // A pending file must not appear in listings before completion.
        var beforeComplete = await ReadAsync<PagedResult<NodeSummary>>(
            await client.GetAsync($"/api/v1/nodes?parentId={folderId}"));
        Assert.NotNull(beforeComplete);
        Assert.Empty(beforeComplete.Items);

        var partUrl = await ReadAsync<PartUploadUrlResponse>(
            await PostJsonAsync(client, $"/api/v1/uploads/{session.UploadId}/parts/1/url", new { }));

        Assert.NotNull(partUrl);
        Assert.Equal(1, partUrl.PartNumber);
        Assert.True(partUrl.Url.IsAbsoluteUri);
        Assert.True(partUrl.ExpiresAtUtc > DateTimeOffset.UtcNow);

        var complete = await PostJsonAsync(
            client,
            $"/api/v1/uploads/{session.UploadId}/complete",
            new { parts = new[] { new { partNumber = 1, eTag = "\"etag-single\"" } } });

        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        var file = await ReadAsync<NodeDetails>(complete);
        Assert.NotNull(file);
        Assert.Equal(NodeType.File, file.Type);
        Assert.NotNull(file.ETag); // a ready file carries its ETag; pending nodes do not
        Assert.Equal(sizeBytes, file.SizeBytes);
        Assert.Equal(folderId, file.ParentId);
        Assert.NotNull(file.ETag);
        // The ETag is the provider's completion result, not the client-reported part ETag.
        Assert.False(string.IsNullOrWhiteSpace(file.ETag));

        // The file is now visible, and the folder reports its recursive size.
        var afterComplete = await ReadAsync<PagedResult<NodeSummary>>(
            await client.GetAsync($"/api/v1/nodes?parentId={folderId}"));
        Assert.NotNull(afterComplete);
        Assert.Contains(afterComplete.Items, item => item.Id == file.Id);

        var folderDetails = await ReadAsync<NodeDetails>(
            await client.GetAsync($"/api/v1/nodes/{folderId}?includeFolderSize=true"));
        Assert.NotNull(folderDetails);
        Assert.Equal(sizeBytes, folderDetails.FolderSizeBytes);

        var download = await ReadAsync<DownloadUrlResponse>(
            await client.GetAsync($"/api/v1/nodes/{file.Id}/download-url"));

        Assert.NotNull(download);
        Assert.Equal(file.Id, download.FileId);
        Assert.Equal(fileName, download.FileName);
        Assert.True(download.Url.IsAbsoluteUri);
        Assert.True(download.ExpiresAtUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Repeated_completion_is_idempotent()
    {
        var client = CreateClient(NewUserId());
        var session = await InitiateAsync(client, $"idem-{Guid.NewGuid():N}");

        var parts = new { parts = new[] { new { partNumber = 1, eTag = "etag-one" } } };

        var first = await PostJsonAsync(client, $"/api/v1/uploads/{session.UploadId}/complete", parts);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var file = await ReadAsync<NodeDetails>(first);
        Assert.NotNull(file);

        var second = await PostJsonAsync(client, $"/api/v1/uploads/{session.UploadId}/complete", parts);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var again = await ReadAsync<NodeDetails>(second);
        Assert.NotNull(again);
        Assert.Equal(file.Id, again.Id);
        Assert.Equal(file.SizeBytes, again.SizeBytes);
    }

    [Fact]
    public async Task Two_part_upload_completes_with_contiguous_parts_in_any_order()
    {
        var client = CreateClient(NewUserId());
        var longSize = TenMiB + 1; // forces two parts at the default 10 MiB chunk

        var initiate = await PostJsonAsync(
            client,
            "/api/v1/uploads/initiate",
            new { parentId = (string?)null, fileName = "big.bin", contentType = "application/octet-stream", sizeBytes = longSize });

        Assert.Equal(HttpStatusCode.Created, initiate.StatusCode);
        var session = await ReadAsync<InitiateUploadResponse>(initiate);
        Assert.NotNull(session);
        Assert.Equal(2, session.ExpectedPartCount);

        foreach (var partNumber in new[] { 1, 2 })
        {
            var url = await PostJsonAsync(client, $"/api/v1/uploads/{session.UploadId}/parts/{partNumber}/url", new { });
            Assert.Equal(HttpStatusCode.OK, url.StatusCode);
        }

        // Parts reported out of order on purpose: the server normalises them.
        var complete = await PostJsonAsync(
            client,
            $"/api/v1/uploads/{session.UploadId}/complete",
            new
            {
                parts = new[]
                {
                    new { partNumber = 2, eTag = "etag-two" },
                    new { partNumber = 1, eTag = "etag-one" },
                },
            });

        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        var file = await ReadAsync<NodeDetails>(complete);
        Assert.NotNull(file);
        Assert.Equal(longSize, file.SizeBytes);
    }

    [Fact]
    public async Task Initiating_rejects_invalid_requests()
    {
        var client = CreateClient(NewUserId());

        var oversized = await PostJsonAsync(
            client,
            "/api/v1/uploads/initiate",
            new { parentId = (string?)null, fileName = "huge.bin", contentType = (string?)null, sizeBytes = 6L * 1024 * 1024 * 1024 });
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        Assert.Contains("sizeBytes", (await ReadProblemAsync(oversized)).Errors!.Keys);

        var negative = await PostJsonAsync(
            client,
            "/api/v1/uploads/initiate",
            new { parentId = (string?)null, fileName = "negative.bin", contentType = (string?)null, sizeBytes = -1L });
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);

        var badName = await PostJsonAsync(
            client,
            "/api/v1/uploads/initiate",
            new { parentId = (string?)null, fileName = "bad/name.bin", contentType = (string?)null, sizeBytes = 10L });
        Assert.Equal(HttpStatusCode.BadRequest, badName.StatusCode);
        Assert.Contains("fileName", (await ReadProblemAsync(badName)).Errors!.Keys);

        var unknownParent = await PostJsonAsync(
            client,
            "/api/v1/uploads/initiate",
            new { parentId = "0123456789abcdef01234567", fileName = "orphan.bin", contentType = (string?)null, sizeBytes = 10L });
        Assert.Equal(HttpStatusCode.NotFound, unknownParent.StatusCode);
    }

    [Fact]
    public async Task Initiating_rejects_a_name_already_taken_by_a_ready_file()
    {
        var client = CreateClient(NewUserId());
        var fileName = $"taken-{Guid.NewGuid():N}.txt";

        var first = await InitiateAsync(client, fileName);
        await CompleteAsync(client, first.UploadId);

        var duplicate = await PostJsonAsync(
            client,
            "/api/v1/uploads/initiate",
            new { parentId = (string?)null, fileName, contentType = (string?)null, sizeBytes = 10L });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("node.name.conflict", (await ReadProblemAsync(duplicate)).Code);
    }

    [Fact]
    public async Task Pending_file_names_are_reserved_until_the_upload_is_aborted()
    {
        var client = CreateClient(NewUserId());
        var fileName = $"reserved-{Guid.NewGuid():N}.txt";

        var first = await InitiateAsync(client, fileName);

        var conflicting = await PostJsonAsync(
            client,
            "/api/v1/uploads/initiate",
            new { parentId = (string?)null, fileName, contentType = (string?)null, sizeBytes = 10L });
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);

        var abort = await PostJsonAsync(client, $"/api/v1/uploads/{first.UploadId}/abort", new { reason = "user cancelled" });
        Assert.Equal(HttpStatusCode.OK, abort.StatusCode);
        var abortResult = await ReadAsync<AbortUploadResponse>(abort);
        Assert.NotNull(abortResult);
        Assert.Equal("Aborted", abortResult.Status);

        // The pending node was deleted, so the name is free again.
        var retry = await PostJsonAsync(
            client,
            "/api/v1/uploads/initiate",
            new { parentId = (string?)null, fileName, contentType = (string?)null, sizeBytes = 10L });
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);

        // Aborted sessions cannot pre-sign parts any more.
        var partAfterAbort = await PostJsonAsync(client, $"/api/v1/uploads/{first.UploadId}/parts/1/url", new { });
        Assert.Equal(HttpStatusCode.Conflict, partAfterAbort.StatusCode);
        Assert.Equal("upload.notactive", (await ReadProblemAsync(partAfterAbort)).Code);
    }

    [Fact]
    public async Task Aborting_twice_is_harmless()
    {
        var client = CreateClient(NewUserId());
        var session = await InitiateAsync(client, $"abort-twice-{Guid.NewGuid():N}.txt");

        var first = await PostJsonAsync(client, $"/api/v1/uploads/{session.UploadId}/abort", new { });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await PostJsonAsync(client, $"/api/v1/uploads/{session.UploadId}/abort", new { });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var result = await ReadAsync<AbortUploadResponse>(second);
        Assert.NotNull(result);
        Assert.Equal("Aborted", result.Status);
    }

    [Fact]
    public async Task Part_urls_are_validated_against_the_expected_part_count()
    {
        var client = CreateClient(NewUserId());
        var session = await InitiateAsync(client, $"parts-{Guid.NewGuid():N}.txt"); // 1 expected part

        foreach (var partNumber in new[] { 0, 2, -1 })
        {
            var response = await PostJsonAsync(
                client,
                $"/api/v1/uploads/{session.UploadId}/parts/{partNumber}/url",
                new { });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var problem = await ReadProblemAsync(response);
            Assert.Equal("request.validation", problem.Code);
            Assert.NotNull(problem.Errors);
            Assert.Contains("partNumber", problem.Errors.Keys);
        }
    }

    [Fact]
    public async Task Completion_requires_the_exact_expected_number_of_parts()
    {
        var client = CreateClient(NewUserId());
        var session = await InitiateAsync(client, $"count-{Guid.NewGuid():N}.txt");

        var response = await PostJsonAsync(
            client,
            $"/api/v1/uploads/{session.UploadId}/complete",
            new { parts = new[] { new { partNumber = 1, eTag = "e1" }, new { partNumber = 2, eTag = "e2" } } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadProblemAsync(response);
        Assert.Equal("request.validation", problem.Code);
        Assert.Contains("parts", problem.Errors!.Keys);

        var empty = await PostJsonAsync(
            client,
            $"/api/v1/uploads/{session.UploadId}/complete",
            new { parts = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var duplicate = await PostJsonAsync(
            client,
            $"/api/v1/uploads/{session.UploadId}/complete",
            new { parts = new[] { new { partNumber = 1, eTag = "e1" }, new { partNumber = 1, eTag = "e2" } } });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        var badETag = await PostJsonAsync(
            client,
            $"/api/v1/uploads/{session.UploadId}/complete",
            new { parts = new[] { new { partNumber = 1, eTag = "has space" } } });
        Assert.Equal(HttpStatusCode.BadRequest, badETag.StatusCode);

        // None of the rejected attempts consumed the session: it still completes normally.
        var complete = await PostJsonAsync(
            client,
            $"/api/v1/uploads/{session.UploadId}/complete",
            new { parts = new[] { new { partNumber = 1, eTag = "e1" } } });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
    }

    [Fact]
    public async Task Provider_size_mismatch_fails_the_session_and_cannot_be_completed_again()
    {
        var client = CreateClient(NewUserId());
        const long sizeBytes = 1234;
        var session = await InitiateAsync(client, $"mismatch-{Guid.NewGuid():N}.bin", sizeBytes);

        // The provider will report a different size than the client declared.
        Factory.Storage.ReportSizeForNextCompletion(sizeBytes + 1);

        var complete = await PostJsonAsync(
            client,
            $"/api/v1/uploads/{session.UploadId}/complete",
            new { parts = new[] { new { partNumber = 1, eTag = "etag" } } });

        Assert.Equal(HttpStatusCode.BadRequest, complete.StatusCode);

        var problem = await ReadProblemAsync(complete);
        Assert.Equal("request.validation", problem.Code);
        Assert.Contains("sizeBytes", problem.Errors!.Keys);

        // The failed file never became ready...
        var file = await client.GetAsync($"/api/v1/nodes/{session.FileId}");
        Assert.Equal(HttpStatusCode.NotFound, file.StatusCode);

        // ...and the session is terminal: a retry must be a new upload, not a second completion.
        var retry = await PostJsonAsync(
            client,
            $"/api/v1/uploads/{session.UploadId}/complete",
            new { parts = new[] { new { partNumber = 1, eTag = "etag" } } });

        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.Equal("upload.notactive", (await ReadProblemAsync(retry)).Code);
        // ...but the file name is no longer reserved: a fresh upload with the same name is the
        // documented recovery path and must succeed immediately.
        var fresh = await InitiateAsync(client, session.FileName, sizeBytes);
        Assert.NotEqual(session.UploadId, fresh.UploadId);
    }

    [Fact]
    public async Task Unknown_and_foreign_upload_sessions_return_404()
    {
        var client = CreateClient(NewUserId());
        var foreign = await InitiateAsync(CreateClient(NewUserId()), $"foreign-{Guid.NewGuid():N}.txt");

        foreach (var url in new[]
        {
            $"/api/v1/uploads/0123456789abcdef01234567/parts/1/url",
            $"/api/v1/uploads/not-an-id/parts/1/url",
        })
        {
            var response = await PostJsonAsync(client, url, new { });
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        var unknownComplete = await PostJsonAsync(
            client,
            "/api/v1/uploads/0123456789abcdef01234567/complete",
            new { parts = new[] { new { partNumber = 1, eTag = "e" } } });
        Assert.Equal(HttpStatusCode.NotFound, unknownComplete.StatusCode);

        var unknownAbort = await PostJsonAsync(
            client,
            "/api/v1/uploads/0123456789abcdef01234567/abort",
            new { });
        Assert.Equal(HttpStatusCode.NotFound, unknownAbort.StatusCode);

        // Another user's session id is indistinguishable from a missing one (404, not 403).
        var foreignComplete = await PostJsonAsync(
            client,
            $"/api/v1/uploads/{foreign.UploadId}/complete",
            new { parts = new[] { new { partNumber = 1, eTag = "e" } } });
        Assert.Equal(HttpStatusCode.NotFound, foreignComplete.StatusCode);

        var foreignAbort = await PostJsonAsync(client, $"/api/v1/uploads/{foreign.UploadId}/abort", new { });
        Assert.Equal(HttpStatusCode.NotFound, foreignAbort.StatusCode);
    }

    [Fact]
    public async Task Download_url_is_only_issued_for_files()
    {
        var client = CreateClient(NewUserId());
        var folderId = await CreateFolderAsync(client, $"not-a-file-{Guid.NewGuid():N}");

        var response = await client.GetAsync($"/api/v1/nodes/{folderId}/download-url");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<InitiateUploadResponse> InitiateAsync(
        HttpClient client,
        string fileName,
        long sizeBytes = 100,
        string? parentId = null)
    {
        var response = await PostJsonAsync(
            client,
            "/api/v1/uploads/initiate",
            new { parentId, fileName, contentType = (string?)null, sizeBytes });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var session = await ReadAsync<InitiateUploadResponse>(response);
        Assert.NotNull(session);
        return session;
    }

    private async Task<NodeDetails> CompleteAsync(HttpClient client, string uploadId, long sizeBytes = 100)
    {
        _ = sizeBytes;
        var response = await PostJsonAsync(
            client,
            $"/api/v1/uploads/{uploadId}/complete",
            new { parts = new[] { new { partNumber = 1, eTag = "etag" } } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var file = await ReadAsync<NodeDetails>(response);
        Assert.NotNull(file);
        return file;
    }
}
