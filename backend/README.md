# Minidrive — Backend

A Dropbox-style file storage backend built with **.NET 9**, **MongoDB** (metadata) and
**Cloudflare R2** (object storage over the S3 API). It exposes a minimal, versioned HTTP API
under `/api/v1` with JWT bearer authentication, a three-phase multipart upload flow, soft
delete with deferred storage cleanup, and cursor-based pagination.

---

## Contents

- [Solution layout](#solution-layout)
- [Architecture](#architecture)
- [Configuration](#configuration)
- [Running locally](#running-locally)
- [API reference (with curl examples)](#api-reference-with-curl-examples)
- [Testing](#testing)
- [Design decisions](#design-decisions)
- [Limitations](#limitations)

---

## Solution layout

```
backend/
├── Minidrive.sln
├── Directory.Build.props          # shared build settings (nullable, analyzers, warnings-as-errors)
├── Directory.Packages.props       # central package management (single place for versions)
├── src/
│   ├── Minidrive.Domain/          # entities, value objects, enums, domain exceptions
│   ├── Minidrive.Application/     # use cases, models, validators, options, ports (interfaces)
│   ├── Minidrive.Infrastructure/  # MongoDB repositories, R2/S3 storage, background worker
│   └── Minidrive.Api/             # composition root, minimal API endpoints, auth, OpenAPI
└── tests/
    ├── Minidrive.UnitTests/       # pure domain/application tests (no I/O)
    └── Minidrive.IntegrationTests/# full HTTP pipeline + disposable MongoDB + fake storage
```

Dependency rule (Clean Architecture — enforced by project references):

```
Api ──▶ Application ──▶ Domain ◀── Infrastructure
                               ▲
                    (interfaces defined in Application,
                     implemented in Infrastructure)
```

- **Domain** references nothing (no ASP.NET Core, no MongoDB, no AWS SDK).
- **Application** references Domain only; storage and persistence appear as interfaces
  (`IObjectStorageService`, `INodeRepository`, `IUploadSessionRepository`, …).
- **Infrastructure** implements those interfaces (MongoDB.Driver, AWSSDK.S3) and maps
  domain entities to persisted documents.
- **Api** wires everything together and owns HTTP concerns (auth, validation pipeline,
  Problem Details, OpenAPI).

---

## Architecture

### Key flows

**Metadata** lives in MongoDB. The `Nodes` collection stores files and folders together and
materialises each node's full path as a delimited string
(`,parentId,folderId,`) so subtree queries and moves are simple indexed lookups.

**Uploads** are three-phase so the API never proxies multi-gigabyte bodies:

1. `POST /api/v1/uploads/initiate` — validates the request, reserves the file name with a
   **pending** node, creates an upload session and asks the storage provider to start a
   multipart upload. Returns the chunk size and expected part count.
2. `POST /api/v1/uploads/{id}/parts/{n}/url` — returns a short-lived **presigned URL** for
   one part; the client PUTs bytes straight to R2.
3. `POST /api/v1/uploads/{id}/complete` — receives the ordered `(partNumber, eTag)` list,
   verifies the declared size against what the provider actually received, finalises the
   multipart upload and turns the pending node into a ready file.

`POST /api/v1/uploads/{id}/abort` releases the reserved name, aborts the multipart upload
and deletes the pending node.

**Deletion** is metadata-first: `DELETE` marks the node (and its subtree) as deleted, then
enqueues the object key into a durable `storage_cleanups` collection. A background worker
(`MaintenanceBackgroundService`) replays the queue, so a storage outage during a delete can
never leak objects — and a delete never blocks on storage. The same worker expires stale
upload sessions and releases their reserved names.

**Ownership** is enforced in the data layer itself (every query is scoped by `userId`).
Cross-user access returns **404, not 403**, so the API never discloses that someone else's
resource exists.

### Non-functional behaviour

| Concern | Approach |
| --- | --- |
| Errors | RFC 7807 Problem Details with a stable machine-readable `code` extension |
| Validation | FluentValidation; field-level `errors` map camelCased from request properties |
| Auth | JWT bearer (`sub` claim = user id); dev-only token endpoint in Development |
| OpenAPI | Generated at `/openapi/v1.json`, browsable at `/scalar/v1`, bearer scheme described |
| Health | `GET /api/v1/health` (liveness), `GET /api/v1/health/ready` (+`/health/ready` alias) with per-dependency checks |
| Indexes | Created programmatically at startup (`EnsureIndexesAsync`), idempotent |
| Options | Strongly typed options classes validated at startup — the app fails fast on bad config |
| Pagination | Opaque base64url cursors `(name, id)` — stable under concurrent inserts |
| Quotas | Optional per-user byte quota enforced at initiate (0 = unlimited) |

---

## Configuration

Configuration is read from `appsettings.json`, `appsettings.Development.json`, environment
variables and command-line arguments (standard ASP.NET Core ordering). **No secrets are
committed** — connection strings and credentials ship empty and validation fails fast at
startup. See [`.env.example`](../.env.example) for the full template.

| Section | Key | Notes |
| --- | --- | --- |
| `MongoDB` | `ConnectionString` | **Required.** e.g. `mongodb://localhost:27017` |
| | `DatabaseName` | Default `minidrive` |
| | `EnsureIndexesOnStartup` | Default `true` |
| `CloudflareR2` | `Endpoint` | **Required** (or `AccountId`). Explicit S3 endpoint — MinIO locally (`http://127.0.0.1:9000`), R2 in production |
| | `AccountId`, `AccessKeyId`, `SecretAccessKey` | **Required.** MinIO's defaults locally; from the R2 dashboard in production |
| | `BucketName`, `Region`, `ForcePathStyle` | Default bucket `minidrive`, path style `true`; region `us-east-1` for MinIO, `auto` for R2 |
| `Authentication` | `Issuer`, `Audience` | Default `minidrive` / `minidrive-api` |
| | `SigningKey` | **Required outside Development** (≥ 32 chars). In Development an ephemeral key is generated when empty |
| | `TokenLifetimeMinutes` | Default `60` |
| | `EnableDevTokenEndpoint` | Dev-only; startup validation rejects `true` outside Development |
| `Uploads` | `ChunkSizeBytes` | Default 10 MiB; must be ≥ 5 MiB (R2 non-final part minimum) |
| | `MaxFileSizeBytes` | Default 5 GiB |
| | `PresignedUrlExpirationMinutes` | Default `60` |
| | `UploadSessionLifetimeHours` | Default `24` |
| | `QuotaBytesPerUser` | `0` disables quotas |
| `Maintenance` | `Enabled`, `IntervalSeconds`, `BatchSize` | Background cleanup worker (default `true` / `300` / `100`) |

Environment variables use `__` as the section separator, e.g.
`MongoDB__ConnectionString` → `configuration["MongoDB:ConnectionString"]`.

---

## Running locally

Prerequisites: **.NET 9 SDK**, **MongoDB**, and an **S3-compatible object store** —
[MinIO](https://min.io) locally, Cloudflare R2 in production.

### 1. Object storage (MinIO)

The storage client is a plain S3 client with path-style addressing, so any
S3-compatible endpoint works:

```bash
# run MinIO however you like (binary, container, …):
minio server ./data                          # S3 API on :9000, console on :9001

# create the bucket once (via the console or the CLI):
mc alias set local http://127.0.0.1:9000 minioadmin minioadmin
mc mb local/minidrive
```

### 2. Run the API

`appsettings.Development.json` already ships stock local defaults
(`mongodb://localhost:27017`, MinIO on `http://127.0.0.1:9000` with the factory-default
`minioadmin` credentials and bucket `minidrive`), so with MongoDB and MinIO running a
plain `dotnet run` just works:

```bash
# 1. restore & build
dotnet restore Minidrive.sln
dotnet build Minidrive.sln          # 0 warnings, 0 errors

# 2. run (listens on http://localhost:5080; Development environment via launchSettings)
dotnet run --project src/Minidrive.Api
```

To use different endpoints/credentials — or real Cloudflare R2 — override via
environment variables (they take precedence over the JSON files; see
[`.env.example`](../.env.example) for the full template):

```bash
set -a && source ../.env && set +a   # after editing ../.env
dotnet run --project src/Minidrive.Api
```

Outside Development there are no built-in defaults: `MongoDB:ConnectionString`, the
`CloudflareR2` settings and `Authentication:SigningKey` must all be supplied, and the
process fails fast at startup otherwise.

Once running:

- Scalar API browser: <http://localhost:5080/scalar/v1>
- OpenAPI document: <http://localhost:5080/openapi/v1.json>
- Liveness: <http://localhost:5080/api/v1/health>
- Readiness: <http://localhost:5080/api/v1/health/ready>

### Obtaining a token

In **Development**, the API exposes a convenience endpoint that mints a JWT for any user id
(it is rejected at startup/404 outside Development):

```bash
TOKEN=$(curl -s -X POST http://localhost:5080/api/v1/auth/dev-token \
  -H 'content-type: application/json' \
  -d '{"userId":"alice"}' | jq -r .accessToken)

AUTH="authorization: Bearer $TOKEN"
```

Outside Development, obtain tokens from your identity provider and send them as
`Authorization: Bearer <jwt>`; the `sub` claim is used as the owning user id.

---

## API reference (with curl examples)

All routes are prefixed `/api/v1`. Bodies are JSON; enums serialise as strings.

### Health

```bash
curl -s http://localhost:5080/api/v1/health
curl -s http://localhost:5080/api/v1/health/ready     # alias: /health/ready
```

### Folders & nodes

```bash
# Create a folder at the root
curl -s -X POST http://localhost:5080/api/v1/folders \
  -H "$AUTH" -H 'content-type: application/json' \
  -d '{"parentId":null,"name":"Projects"}'
# → 201 { "id": "...", "location": "/api/v1/nodes/..." }

# List root entries (also ?parentId=<folderId>, ?pageSize=25, ?cursor=<token>)
curl -s "http://localhost:5080/api/v1/nodes?pageSize=25" -H "$AUTH"

# Read one node (add ?includeFolderSize=true for recursive folder size)
curl -s "http://localhost:5080/api/v1/nodes/$NODE_ID?includeFolderSize=true" -H "$AUTH"

# Rename
curl -s -X PATCH "http://localhost:5080/api/v1/nodes/$NODE_ID" \
  -H "$AUTH" -H 'content-type: application/json' \
  -d '{"name":"Work"}'

# Move into another folder (null = root)
curl -s -X POST "http://localhost:5080/api/v1/nodes/$NODE_ID/move" \
  -H "$AUTH" -H 'content-type: application/json' \
  -d "{\"parentId\":\"$OTHER_FOLDER_ID\"}"

# Soft-delete (objects are removed by the maintenance worker afterwards)
curl -s -i -X DELETE "http://localhost:5080/api/v1/nodes/$NODE_ID" -H "$AUTH"   # → 204

# Short-lived download URL (files only)
curl -s "http://localhost:5080/api/v1/nodes/$FILE_ID/download-url" -H "$AUTH"
```

### Uploads (three-phase multipart)

```bash
# 1. initiate
SESSION=$(curl -s -X POST http://localhost:5080/api/v1/uploads/initiate \
  -H "$AUTH" -H 'content-type: application/json' \
  -d "{\"parentId\":null,\"fileName\":\"report.pdf\",\"contentType\":\"application/pdf\",\"sizeBytes\":10485760}")
UPLOAD_ID=$(echo "$SESSION" | jq -r .uploadId)
CHUNK=$(echo "$SESSION" | jq -r .chunkSizeBytes)      # 10 MiB default
PARTS=$(echo "$SESSION" | jq -r .expectedPartCount)
# → 201 { uploadId, fileId, chunkSizeBytes, partNumberMinimum, partNumberMaximum,
#         expectedPartCount, expiresAtUtc, status }

# 2. pre-sign each part and upload bytes straight to storage
PART1_URL=$(curl -s -X POST \
  "http://localhost:5080/api/v1/uploads/$UPLOAD_ID/parts/1/url" \
  -H "$AUTH" -H 'content-type: application/json' -d '{}' | jq -r .url)
curl -X PUT "$PART1_URL" -H 'content-type: application/pdf' \
  --upload-file ./report.pdf                       # (single part = whole file)

# …repeat for parts 2..PARTS with the corresponding byte range…

# 3. complete (ordered or unordered; server sorts and validates)
curl -s -X POST "http://localhost:5080/api/v1/uploads/$UPLOAD_ID/complete" \
  -H "$AUTH" -H 'content-type: application/json' \
  -d '{"parts":[{"partNumber":1,"eTag":"\"abc123\""}]}'          # → 200 NodeDetails

# …or abandon it (releases the reserved file name)
curl -s -X POST "http://localhost:5080/api/v1/uploads/$UPLOAD_ID/abort" \
  -H "$AUTH" -H 'content-type: application/json' -d '{"reason":"user cancelled"}'
```

Rules enforced server-side:

- Non-final parts must be ≥ 5 MiB; the declared total must match what the provider
  received (`400` with `errors.sizeBytes` otherwise, session is failed).
- Part numbers must be contiguous `1..expectedPartCount`, unique, with printable-ASCII
  eTags (`400 request.validation` otherwise).
- Completing an already-completed session is **idempotent** — the existing file is
  returned. Terminal/failed sessions return `409`.

### Errors

Failures return Problem Details:

```json
{
  "type": "about:blank",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "detail": "The request could not be processed as submitted.",
  "code": "request.validation",
  "errors": { "name": ["The name is invalid. …"] }
}
```

| Status | `code` examples |
| --- | --- |
| 400 | `request.validation`, `request.pagination.pageSize` |
| 401 | missing/invalid/expired token |
| 404 | `resource.notfound` (also for other users' resources — never 403) |
| 409 | `node.name.conflict`, `node.move.invalid`, `upload.notactive`, `upload.completed`, `upload.expired` |
| 413 | `request.tooLarge` |
| 502 | `storage.unavailable` |
| 507 | `quota.exceeded` |
| 500 | `internal.error` (no exception details leaked) |

---

## Testing

```bash
dotnet test Minidrive.sln
```

- **Unit tests** (`tests/Minidrive.UnitTests`) — pure domain and application logic:
  naming rules, path materialisation, move/cycle rules, the upload state machine,
  pagination cursor codec, option validators. No I/O; ~138 tests.
- **Integration tests** (`tests/Minidrive.IntegrationTests`) — drive the real HTTP
  pipeline through `WebApplicationFactory` against a **disposable MongoDB**
  (EphemeralMongo downloads a `mongod` binary — no Docker) with an in-memory
  **fake object storage**, so the suite needs neither Docker nor R2 credentials.
  Each test runs as its own user id against a unique database.

  R2 settings in the test factory are syntactically valid placeholders that are never
  contacted (the storage implementation is swapped out), and the background maintenance
  worker is disabled — tests invoke `MaintenanceService` explicitly for deterministic
  assertions.

---

## Design decisions

- **Single collection + materialised path.** One `Nodes` collection holds files and
  folders; `Path` (`…,parentId,folderId,`) makes "everything under X" an indexed prefix
  scan, and a move is one document update per subtree member.
- **Soft delete + deferred cleanup.** `IsDeleted`/`DeletedAtUtc` flip instantly; object
  keys go into a durable queue drained by the worker. Deleting twice is harmless
  (`404` the second time), and the unique sibling-name index is *partial*
  (`IsDeleted: false`), so a name is freed the moment its node is deleted.
- **Pending files reserve names.** An initiated upload creates a pending node so two
  concurrent uploads cannot claim the same name. Pending nodes are excluded from
  listings and return `404`; abort/expiry hard-deletes them to release the name.
- **404 instead of 403** for foreign resources, to avoid disclosing their existence.
- **Cursor pagination** over offset pagination: `(lastItemName, lastItemId)` tokens stay
  correct while siblings are inserted/renamed between pages. A malformed cursor is
  treated as "no cursor" (first page) so clients can always recover.
- **Provider-trusted metadata.** File size/ETag after completion come from the storage
  provider's response, not from the client — the declared size is checked against it.
- **Options validated at startup** (FluentValidation + `ValidateOnStart`), so
  misconfiguration fails the process immediately instead of the first request.
- **No secrets in the repo.** Development generates an ephemeral JWT signing key when
  none is configured; real keys arrive via environment variables.

## Limitations

- **Phase 1 scope:** no sharing/collaboration, no versions/history, no thumbnails, no
  virus scanning, no public links, no web frontend.
- Single-region, single-bucket storage; no replication or tiering.
- No server-side encryption-at-rest configuration (rely on the provider's defaults).
- Quotas are enforced at initiate based on currently-stored bytes; concurrent uploads
  can briefly overshoot, and bandwidth is not metered.
- Rate limiting and request-level auditing/observability (OpenTelemetry) are not wired
  up yet.
- The dev-token endpoint is for local development only — do not enable
  `Authentication:EnableDevTokenEndpoint` outside Development (startup validation
  enforces this).
