# Minidrive client

Angular 22 (standalone components, SSR template) + Tailwind 4 + Prettier. A
Dropbox-style file explorer backed by the Minidrive API.

## Running it

The API must be running locally (see the root [README](../README.md)), in the
Development environment with the dev-token endpoint enabled:

```bash
dotnet run --project ../backend/src/Minidrive.Api   # http://localhost:5080
```

Then, in `client/`:

```bash
npm install
npm start                      # dev server on http://localhost:4200
```

The dev server proxies every `/api/**` request to the API — see
[`proxy.conf.mjs`](proxy.conf.mjs). By default it targets `http://localhost:5080`;
point it elsewhere with the `API_PROXY_TARGET` env var:

```bash
API_PROXY_TARGET=http://127.0.0.1:5090 npm start
```

Because requests are proxied, the API needs no CORS configuration for local
development.

## Identity (no login yet)

Phase 1 has no sign-in screen. At bootstrap `AuthService` (browser only — the app
renders on the server too) mints a JWT for the fixed user `dev-user` via the
Development-only `POST /api/v1/auth/dev-token`, then attaches it to every request
through an HTTP interceptor. Two things must therefore be true of the API you point at:

- `ASPNETCORE_ENVIRONMENT=Development` (or the dev-token endpoint explicitly enabled).
- `Authentication__EnableDevTokenEndpoint=true` — if your `.env` or environment sets it
  to `false`, the app fails at startup with a clear bootstrap error rather than silently
  showing an empty drive.

A real login flow is Phase 2; nothing here is a security boundary.

## What's in the UI

| Area | Behaviour |
| --- | --- |
| **Explorer** (`/files`, `/files/:folderId`) | Breadcrumbs, folders-first listing, human-readable sizes/timestamps, cursor-based *Load more*, skeleton rows while loading, empty states |
| **Uploads** | Drag-and-drop onto rows/empty area or the toolbar button; the real 3-phase flow (initiate → presigned part URLs → complete) with per-part progress, up to 2 files × 3 parts in flight, a collapsible queue drawer with cancel/retry/dismiss |
| **CRUD** | New folder, rename, delete with confirmation, all surfaced as toasts |
| **Multi-select** | Checkbox selection + Select all; bulk delete and bulk move; drag-to-folder moves |
| **Move dialog** | Lazy-loaded folder tree (loads children on expand); folders being moved and the current parent are excluded/disabled |
| **Search** (`/search?q=`) | Server-side, case-insensitive name search via `GET /api/v1/search`; URL-driven with debounced input, results show their folder location |
| **Polish** | Dark mode (persisted to `localStorage`), keyboard support (Enter/Space to select, Delete to remove, Escape closes dialogs), responsive layout |

## Project layout

```
src/app/
├── core/        # ApiService (typed endpoints, XHR for upload progress), AuthService,
│                # UploadManager, ToastService, wire-format models, error interceptor
├── shared/      # <ui-modal>, confirm/name/move dialogs, inline <ui-icon> set
├── features/
│   ├── files/   # FileBrowser — the main explorer page
│   ├── search/  # Search page
│   └── uploads/ # Upload queue drawer
└── app.ts       # shell: header (search box, theme toggle), toasts, drawer, routes
```

Notes on conventions: `models.ts` mirrors the API's JSON exactly (including enum
casing — the API emits `"Folder"`/`"File"`, so the TS `NodeType` union uses those
literals and comparisons are compile-checked). The upload engine uses `XMLHttpRequest`
rather than `fetch` because only XHR exposes upload progress. All data loading happens
in the browser; every route is client-rendered (`app.routes.server.ts`) because API
calls need the minted token.

## Building and testing

```bash
npm run build     # production build → dist/
npm start         # dev server with HMR
```

## Additional resources

- [Angular CLI command reference](https://angular.dev/tools/cli)
- [Tailwind CSS 4](https://tailwindcss.com/docs)
