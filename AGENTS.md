# AGENTS.md

Guidance for coding agents in this repository. Scope: the whole repo. `CLAUDE.md` is a shorter current overview; `README.md` covers Docker deployment and environment variables. `.github/copilot-instructions.md` describes the legacy KoalaWiki layout (`src/KoalaWiki/`, `web-site/`, FastService `FastApi` services, `KoalaWiki.Core` migrations) that no longer exists in this tree — do not follow its paths or commands.

OpenDeepWiki converts Git repositories, ZIP archives, and local directories into AI-generated documentation sites, served by a Next.js public site with chat and MCP endpoints. Backend: ASP.NET Core on .NET 10. Frontend: Next.js 16 App Router (`web/`). Separate docs site: `docs/` (Fumadocs).

## Where changes belong

- `src/OpenDeepWiki/` — the application. `Program.cs` is the composition root: all DI registrations, endpoint mapping calls, hosted workers, JWT/Serilog/rate-limit/MCP setup. `Endpoints/` holds static `MapXxxEndpoints(this WebApplication)` classes (admin ones under `Endpoints/Admin/`); `Services/<Area>/` holds `I*Service` interface + implementation pairs; background jobs are `IHostedService` workers (`RepositoryProcessingWorker`, `TranslationWorker`, `IncrementalUpdateWorker`, …).
- `src/OpenDeepWiki.Entities/` — EF entities. New entities inherit `AggregateRoot<TKey>` (UTC timestamps, soft delete via `IsDeleted`, `Version` rowversion for optimistic concurrency).
- `src/OpenDeepWiki.EFCore/` — `IContext` and `MasterDbContext` (`MasterDbContext.cs`): the single list of `DbSet`s plus shared model configuration in `OnModelCreating`.
- `src/EFCore/OpenDeepWiki.Sqlite/` and `src/EFCore/OpenDeepWiki.Postgresql/` — thin provider contexts (subclasses of `MasterDbContext`), each with its own `Migrations/` directory.
- `framework/OpenDeepWiki.Cache.*` — cache abstractions, memory implementation, DI extension; leaf projects referenced by the app.
- `src/OpenDeepWiki/Agents/` — AI provider binding through `AgentFactory` (Microsoft.Agents.AI), including the HTTP handler chain documented in its `CreateHttpClient` comment. Prompt assets are `src/OpenDeepWiki/prompts/*.md`, copied to output via the csproj `Content` glob.
- `web/` — Next.js public site and admin UI. All API calls go through `web/lib/api-client.ts` (`api.get/post/...`), which the catch-all route `web/app/api/[...path]/route.ts` proxies at runtime to the backend via `API_PROXY_URL`.
- `tests/OpenDeepWiki.Tests/` — xUnit with FsCheck, Moq, and EF Core InMemory/Sqlite. Folders mirror backend areas (`Services/`, `Agents/`, `Infrastructure/`, …); the project references the main csproj directly.
- `docs/` — separate Fumadocs app; `scripts/` — deployment helpers; `DatabaseQuery/` — standalone helper console project, not part of `OpenDeepWiki.sln`.

Dependency direction: `OpenDeepWiki` → provider contexts → `OpenDeepWiki.EFCore` → `OpenDeepWiki.Entities`. Entities and EFCore must not reference the app or services.

## Commands

The package manager for `web/` is **npm**. `web/bun.lock` exists but nothing consumes it: `web/Dockerfile` runs `npm ci` against `package-lock.json` (the comment there explains it avoids an npm peer-resolution crash and keeps builds reproducible) and the Makefile uses `npm install`. Do not switch package managers.

Backend:

```bash
dotnet build OpenDeepWiki.sln
dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj
dotnet run --project src/OpenDeepWiki/OpenDeepWiki.csproj
```

Dev API is served at `http://localhost:5265` (default http profile per README) with `/health` and, in Development, Scalar at `/v1/scalar`. Targeted test run (standard xUnit filter; not executed while writing this file): `dotnet test tests/OpenDeepWiki.Tests/OpenDeepWiki.Tests.csproj --filter "FullyQualifiedName~<TestClass>"`.

Frontend, from `web/`:

```bash
npm install          # first time
npm run dev          # needs API_PROXY_URL (env var or web/.env.local) pointing at the backend
npm run lint         # eslint
npm test             # vitest --run; npm run test:watch for watch mode
```

Vitest discovers `**/*.{test,spec}.*` outside `node_modules`/`.next` (see `web/vitest.config.ts`); tests live in `__tests__/` directories (`web/lib/__tests__/`) or beside the component (`web/components/repo/branch-generation-status.test.tsx`). Single file: `npx vitest --run <path>`.

`docs/` has no lockfile: `cd docs && npm install && npm run dev`. `make dev` runs the entire stack under Docker Compose — it is not the way to run local dev servers; use the commands above. Docker-free Makefile helpers: `make test-backend`, `make lint` (web + docs), `make format-backend` (runs `dotnet format OpenDeepWiki.sln`).

Commands above are copied from `Makefile`, `web/package.json`, `docs/package.json`, and `README.md`; builds and tests were not executed while writing this document.

## Linked updates

- **New entity**: add the class under `src/OpenDeepWiki.Entities/<Area>/`, a `DbSet<>` in **both** `IContext` and `MasterDbContext` (`src/OpenDeepWiki.EFCore/MasterDbContext.cs`), and model configuration in `MasterDbContext.OnModelCreating`. Startup calls `EnsureCreatedAsync` plus hand-written backfill DDL in `DbInitializer.MigrateSqliteAsync` / `MigratePostgresqlAsync` (`src/OpenDeepWiki/Infrastructure/DbInitializer.cs`): schema that existing installs need (new table or column) must be added there too, or deployed databases will never get it. Provider `Migrations/` directories cover the EF-migration path.
- **New API surface**: implement `Services/<Area>/I*Service` + implementation, register it in `Program.cs` (scoped by default), add `Endpoints/XxxEndpoints.cs` with a `MapXxxEndpoints` extension, and call it from `Program.cs`. Endpoint registration is explicit — an unmapped `Map` method exposes nothing.
- **Wiki languages vs UI locales**: `web/i18n/config.ts` intentionally separates `uiLocales` (interface language) from `wikiLanguageCodes` (document-generation language) so a UI language never implicitly becomes a generation language. Adding a wiki language means editing `wikiLanguageCodes` **and** `WikiLanguageNames.DefaultLanguages` in `src/OpenDeepWiki/Services/Wiki/WikiLanguageNames.cs` — both files carry "keep in sync" comments. Adding a UI locale means `uiLocales`, `uiLocaleNames`, and a `web/i18n/messages/<locale>/` translation pack.
- **New backend project**: add its csproj to the restore layer of `src/OpenDeepWiki/Dockerfile`. That stage publishes with `--no-restore`, so a `ProjectReference` chain csproj missing from the copy list fails the image build with NETSDK1004 (this already broke the image once; see the Dockerfile comment).
- **Package versions**: centrally managed in `Directory.Packages.props` (`ManagePackageVersionsCentrally`) — `PackageReference`s in csproj files carry no `Version`. The `<Version>` property in that same file drives Docker image tags and GitHub releases in both `.github/workflows/*.yml`.

## Conventions and traps

- Backend comments are mostly Chinese and often encode invariants ("Keep in sync…", "Requirements: x.y") or incident notes (e.g., the rate-limiter and Dockerfile comments). When your change makes such a comment stale, update it in the same edit; do not delete the rationale.
- The global rate limiter in `Program.cs` is registered but `app.UseRateLimiter()` is deliberately commented out (commit history: normal requests triggered 429s). Leave it off unless the task is specifically about rate limiting.
- `API_PROXY_URL` is read at **runtime**, with `.env`/`.env.local` re-read on a short cache (`web/app/api/[...path]/route.ts`; rationale in `web/next.config.ts`). Do not bake it at build time — the Docker image must stay environment-agnostic.
- `Directory.Build.props` suppresses NU1604/NU1903 and `.editorconfig` disables OPENAI001; pre-release packages are allowed. Do not remove these suppressions in unrelated changes.
- A `dotnet-tools.json` pinning dotnet-ef sits at `src/OpenDeepWiki/dotnet-tools.json`, not the standard `.config/` location, so `dotnet tool restore` from the repo root will not discover it; treat local `dotnet ef` availability as unverified and use the `DbInitializer` path above for schema work.
- Frontend: kebab-case file names, `use-*` hook prefix, `@/` import alias, colocated tests. Backend: standard .NET naming; `Nullable` and `ImplicitUsings` are enabled solution-wide.
- Commit messages use Conventional Commits prefixes with short Chinese summaries (see `git log`).

## Maintenance rules

- Stay within the task's scope: read current file contents before editing, keep diffs minimal, and do not refactor, reformat, or clean up code the task did not touch.
- If a change alters any command, boundary, or linked-update step documented here, update the corresponding section of this file in the same change.
- If a task instruction conflicts with an invariant documented here, follow the task and call out the conflict instead of silently breaking the invariant.
