# Architecture

The API serves a static WebAssembly PWA. All UI interactions run in the browser. The API uses ASP.NET Identity and EF Core with SQL Server/Azure SQL for accounts and all saved user content. There is no Interactive Server or SignalR session.

The only external calls are explicit AI processing and license verification. The OpenAI-compatible HTTP adapter sits behind IAiBrainDumpProcessor. The browser uses IUserDataStore, implemented by SqlUserDataStore over authenticated HTTP. No user content is stored in browser storage.

The SQL/account upgrade supersedes the original local-only specification. See [SQL and accounts](sql-and-accounts.md) for security boundaries, migrations, and current account limitations.

## Code organization

- API `Program.cs` contains application composition and middleware ordering. `Configuration/ServiceRegistration.cs` registers services and binds typed options, preserving existing configuration keys.
- `Endpoints/NagapieEndpoints.cs` validates requests, applies access rules, and translates service results into HTTP responses. Provider calls live in `AI/` and `Licensing/`.
- `IAiBrainDumpProcessor`, `ILicenseVerifier`, and `IUnlockTokenService` define the server integration boundaries. The prompt, request schema, and output validation have dedicated files.
- `Middleware/ApiRequestMiddleware.cs` owns response headers, safe error handling, and request metadata logging. Provider bodies, credentials, and braindumps are never logged.
- Client `Services/Api/INagapieApiClient.cs` separates pages from HTTP transport and validates responses before returning them. `DumpReviewMapper` maps accepted output into local items.
- Razor markup describes views. Component event handlers and lifecycle methods live in matching `.razor.cs` files. The list's inline item template remains in Razor because it contains markup.
- `AppState` coordinates browser state and persistence order. `Domain/CategoryRules`, `ThoughtRules`, and `StoredDataValidator` contain business rules without browser or network dependencies. Stateless rules do not need interfaces.
- Contracts and domain models have separate, named files. Shared `ErrorCodes` keep protocol values consistent with localized messages. Stored JSON and HTTP field names remain unchanged.

Use `dotnet format Nagapie.BraindumpLite.sln --no-restore` for formatting and `dotnet test Nagapie.BraindumpLite.sln` for regression tests. The repository's `.editorconfig` defines C# brace and indentation conventions. Nullable reference checks and warnings-as-errors remain enabled.

## Storage

UserDocuments has a composite (UserId, Key) primary key and a concurrency version. Five sections (items, categories, draft, settings, access) are serialized in SQL JSON columns. Items and committed session IDs share an atomic write; the original saved BrainDump is inserted in the same transaction. Identity tables hold accounts. See sql-and-accounts.md for schema and setup.

Draft cleanup happens after commit. A repeated commit recognizes its source ID. Invalid saved data blocks writes instead of silently replacing it. A failed storage write never replaces the in-memory saved list with an optimistic result.

The manual fallback deliberately permits one thought up to 5,000 characters, so declining AI can preserve the entire input. AI output is restricted to 500 characters per item. Standard category IDs are deterministic across refreshes.

Time buckets never automatically move items. Let go is an archive reason, not deletion. Restoring a let-go item clears its category. All persisted timestamps use UTC.

## Access

Both API and client read the same server configuration. The paywall is disabled by default. Identity cookie authentication is required for all data, AI and license endpoints. The optional paywall still uses a soft client-supplied count. Signed HMAC tokens protect paid unlock claims; they do not make the trial counter tamper-proof.

## Localization

Both .resx resource sets are embedded in the main client assembly. A scoped IStringLocalizer selects them using the saved language. This makes switching immediate after account settings load without relying on asynchronously downloaded satellite assemblies.

## PWA

Published app assets are versioned by the SDK service-worker manifest. API responses are never cached. Old service workers are allowed to finish naturally; no skipWaiting or forced update during an active review.
