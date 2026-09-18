# Architecture

The API serves a static WebAssembly PWA. All UI interactions run in the browser. There is no Interactive Server, SignalR session, user database, or content persistence on the API.

The only external calls are explicit AI processing and license verification. The OpenAI-compatible HTTP adapter sits behind IAiBrainDumpProcessor. The browser uses ILocalStorageService, allowing a future SQLite-backed implementation.

## Storage

Five namespaced keys: nagapie.braindump.items, categories, draft, settings, access. Each contains a schema-versioned envelope. Items and committed session IDs share one atomic JSON write. This slightly extends the document's items-only shape to make retry and free-session accounting reliable without a transaction across localStorage keys.

Draft cleanup happens after commit. A repeated commit recognizes its source ID. Invalid saved data blocks writes instead of silently replacing it. A failed storage write never replaces the in-memory saved list with an optimistic result.

The manual fallback deliberately permits one thought up to 5,000 characters, so declining AI can preserve the entire input. AI output is restricted to 500 characters per item. Standard category IDs are deterministic across refreshes.

Time buckets never automatically move items. Let go is an archive reason, not deletion. Restoring a let-go item clears its category. All persisted timestamps use UTC.

## Access

Both API and client read the same server configuration. The paywall is disabled by default. The accepted accountless soft limit uses an untrusted local count, not tracking or identity. Signed HMAC tokens protect paid unlock claims; they do not make the trial counter tamper-proof.

## Localization

Both .resx resource sets are embedded in the main client assembly. A scoped IStringLocalizer selects them using the saved language. This makes switching immediate and offline-capable without relying on asynchronously downloaded satellite assemblies.

## PWA

Published app assets are versioned by the SDK service-worker manifest. API responses are never cached. Old service workers are allowed to finish naturally; no skipWaiting or forced update during an active review.
