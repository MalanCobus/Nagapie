# Nagapie · Braindump Lite

A quiet, bilingual place to capture thoughts, review AI suggestions, and decide what to do, keep, or let go.

## Run locally

Requires the .NET 10 SDK and SQL Server Express LocalDB. See [SQL and account setup](docs/sql-and-accounts.md), including the Azure steps. Pending database migrations run automatically before the app starts serving requests.

```powershell
dotnet run --project src/Nagapie.BraindumpLite.Api --launch-profile http
```

Open http://localhost:5211 and register an account. The API serves the Blazor WebAssembly app on the same origin. All saved data is stored per user in SQL. Payments are disabled by default.

## Connect automatic sorting

The real OpenAI-compatible integration is implemented; it does not substitute mock output when no key is available. Without a key, users can save their dump as one thought in their SQL-backed account and manage their list.

Store your key using the server project's user-secrets manager (never in client configuration or source control):

```powershell
dotnet user-secrets set "AiProvider:ApiKey" "YOUR_API_KEY" --project src/Nagapie.BraindumpLite.Api
```

Restart the API after changing secrets. The default model is `gpt-4o-mini`. Override `AiProvider:Model`, `AiProvider:BaseUrl`, `AiProvider:DisplayName`, and `AiProvider:PrivacyUrl` for your provider. The endpoint must use HTTPS and support Chat Completions with strict JSON Schema structured outputs and `store: false`.

For production, use environment variables such as `AiProvider__ApiKey`. Keep secrets out of the PWA. Do not paste credentials into chat.

The first AI request asks for consent. Only the dump and category references go to the provider. AI timeout: 20 seconds; input limit: 5,000 characters; output: 1–30 items. Users review every suggestion before saving.

## What is implemented

- Installable, mobile-first Blazor WebAssembly PWA, with Dutch and English resources.
- Registration, login, logout, and per-user SQL storage; onboarding explains account storage.
- Draft autosave; AI consent, processing, cancellation, and honest fallback.
- Review: edit text, change category/time bucket, merge, remove, and save.
- Today / Next week / Later list, filtering, editing, completion, recoverable Let go, Undo, archive, and confirmed permanent deletion.
- Default and custom categories.
- Browser speech recognition where supported, consent and five-minute limit.
- Local RFC 5545 calendar export with escaping, UTF-8 line folding, timed and all-day events.
- Original braindump history and optimistic concurrency checks. Data access requires an internet connection.
- Payhip verification and signed unlock tokens, behind a disabled-by-default paywall.
- Rate limiting, response validation, body limits, generic errors, content-safe logging.
- Versioned, replaceable browser storage; idempotent session commits.
- Test-version privacy and terms pages, with production gaps explicitly identified.

## Tests and release build

```powershell
dotnet test Nagapie.BraindumpLite.sln
node --test tests/service-worker.test.mjs
dotnet publish src/Nagapie.BraindumpLite.Api -c Release -o artifacts/publish
```

The published API contains the client assets. Production hosting must provide HTTPS. Configure the reverse proxy so it does not log request bodies or Payhip license query strings. The application does not trust forwarded headers by default; configure known proxies deliberately if deploying behind one.

The service worker is active in published builds; the development service worker intentionally does not cache. Updates wait for old tabs to close and never forcibly reload a review.

## Payhip (when ready)

Configure these server settings before enabling `Access:PaywallEnabled`:

- `Payhip:ProductUrl`
- `Payhip:ProductSecret` (secret)
- `UnlockTokens:SigningKey` (secret; at least 32 random bytes represented as a long string)

The first three successfully saved AI sessions are free. Failed calls, canceled reviews, manual saves, and failed storage writes do not count. Items and commit receipts are stored atomically; retrying after an interrupted draft cleanup cannot duplicate a session.

The optional count is saved with the account but remains client-supplied and resettable. The server receives this untrusted count and validates signed unlock tokens; it does not claim to enforce a fraud-resistant trial. Tokens have no automatic expiry or revocation in this Lite build. Rotating the signing key invalidates existing tokens; purchasers can re-enter their original key.

## Before calling it a finished public MVP

See [verification](docs/verification.md) and [release checklist](docs/release-checklist.md). A real provider key, live AI quality validation, Payhip checkout tests, finalized legal/vendor details, real-device speech tests, and the complete cross-browser/calendar-import matrix remain necessary.

Saved data belongs to the signed-in account and is stored in SQL. Reload to see updates from another device. Stale writes are rejected instead of silently overwriting another tab. Configure database backups for hosted use. Browser-only data from earlier versions is not read or imported.

## Project layout

- `src/Nagapie.BraindumpLite.Client` — UI, local state, resources, storage, speech bridge, calendar export.
- `src/Nagapie.BraindumpLite.Api` — hosted static PWA, AI processing and Payhip verification.
- `src/Nagapie.BraindumpLite.Contracts` — request/response contracts, input validation, and shared error codes.
- `tests/Nagapie.BraindumpLite.Client.Tests` — API client, persistence, recovery, localization and calendar tests.
- `tests/Nagapie.BraindumpLite.Tests` — API, AI validation and access tests.
- `docs/specification.md` — original user-provided specification.

See [architecture](docs/architecture.md) for service boundaries and a guide to the refactored C# structure.

Implementation references: [OpenAI structured outputs](https://developers.openai.com/api/docs/guides/structured-outputs), [Payhip software license keys](https://help.payhip.com/article/317-software-license-keys-new).
