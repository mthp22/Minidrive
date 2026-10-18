# Minidrive

A Dropbox-style file storage system — **backend only** (Phase 1). Built with .NET 9,
MongoDB for metadata and Cloudflare R2 (S3-compatible) for object storage.

```
backend/
├── src/
│   ├── Minidrive.Api/            # minimal APIs under /api/v1, JWT auth, OpenAPI, health
│   ├── Minidrive.Application/    # use cases, validators, options, ports
│   ├── Minidrive.Domain/         # entities, value objects, domain rules (no framework deps)
│   └── Minidrive.Infrastructure/ # MongoDB repositories, R2/S3 storage, maintenance worker
└── tests/
    ├── Minidrive.UnitTests/          # pure logic tests
    └── Minidrive.IntegrationTests/   # HTTP pipeline + disposable MongoDB + fake storage
```

## Highlights

- **Clean Architecture** — the Domain layer depends on nothing; infrastructure details
  (MongoDB, AWS SDK) stay behind application interfaces.
- **Three-phase multipart uploads** — initiate → presigned part URLs → complete, plus
  abort; the API never proxies file bytes. 5 MiB minimum for non-final parts,
  configurable 10 MiB chunk, declared size verified against the provider.
- **Single `Nodes` collection** with materialised paths for fast subtree queries;
  programmatic, idempotent index creation at startup.
- **S3-compatible object storage** — MinIO (or any S3 service) locally, Cloudflare R2 in
  production; path-style addressing works out of the box.
- **Soft delete + deferred storage cleanup** through a durable queue drained by a
  background worker — a storage outage can never leak objects or block a delete.
- **Ownership enforced in the data layer**; foreign resources return `404`, never `403`.
- **JWT bearer auth**, RFC 7807 Problem Details with stable error codes, FluentValidation
  field errors, generated OpenAPI (Scalar UI), liveness/readiness health probes.
- **Strongly typed configuration validated at startup**; no secrets committed
  (Development mints an ephemeral signing key when none is configured).

## Quick start

Prerequisites: [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0), MongoDB, and
[MinIO](https://min.io) (or any S3-compatible store — Cloudflare R2 in production).

```bash
# one-time: start MinIO and create the bucket (bucket "minidrive", credentials minioadmin)
minio server ./data

# restore, build (0 warnings), test
cd backend
dotnet restore Minidrive.sln
dotnet build Minidrive.sln
dotnet test  Minidrive.sln        # unit tests need nothing extra;
                                  # integration tests download a disposable mongod (no Docker)

# run the API (default http://localhost:5080) — appsettings.Development.json already
# defaults to localhost MongoDB + MinIO, so no extra configuration is needed locally
dotnet run --project src/Minidrive.Api
```

Then open the API browser at <http://localhost:5080/scalar/v1>.

In Development you can mint a token for any user id:

```bash
TOKEN=$(curl -s -X POST http://localhost:5080/api/v1/auth/dev-token \
  -H 'content-type: application/json' -d '{"userId":"alice"}' | jq -r .accessToken)

curl -s http://localhost:5080/api/v1/nodes -H "authorization: Bearer $TOKEN"
```

## API at a glance

| Method & path | Purpose |
| --- | --- |
| `GET /api/v1/health` | liveness probe |
| `GET /api/v1/health/ready` (`/health/ready`) | readiness probe with per-dependency checks |
| `GET /api/v1/auth/dev-token` → `POST …` | Development-only JWT minting |
| `GET /api/v1/nodes` | list children (`parentId`, `pageSize`, `cursor`) |
| `GET /api/v1/nodes/{id}` | node details (`includeFolderSize=true`) |
| `POST /api/v1/folders` | create folder |
| `PATCH /api/v1/nodes/{id}` | rename |
| `POST /api/v1/nodes/{id}/move` | move / re-parent |
| `DELETE /api/v1/nodes/{id}` | soft-delete (storage cleanup is deferred) |
| `GET /api/v1/nodes/{id}/download-url` | short-lived presigned download URL |
| `POST /api/v1/uploads/initiate` | start a multipart upload |
| `POST /api/v1/uploads/{id}/parts/{n}/url` | presigned URL for one part |
| `POST /api/v1/uploads/{id}/complete` | finalise (idempotent) |
| `POST /api/v1/uploads/{id}/abort` | cancel and release the reserved name |
| `GET /openapi/v1.json` · `GET /scalar/v1` | OpenAPI document · API browser |

Full documentation, configuration reference, curl walkthrough and design notes:
**[backend/README.md](backend/README.md)**.

## Repository files

| File | Purpose |
| --- | --- |
| [`.env.example`](.env.example) | configuration template (copy to `.env`, never commit real values) |
| [`.gitignore`](.gitignore) | ignores build output, IDE files and all secret-bearing files |
| [`.editorconfig`](.editorconfig) | shared editor/analyser settings |
| [`backend/README.md`](backend/README.md) | architecture, config, API reference, testing, limitations |
