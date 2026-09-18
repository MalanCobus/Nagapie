# SQL/accounts verification — 18 September 2026

- 68 automated .NET tests pass in Release (23 client, 45 API/provider/account).
- Four service-worker tests pass. The worker caches static app files only; API requests use the network.
- Formatting verification passes and the Release API/client publish succeeds.
- Initial EF migration applied successfully to actual SQL Server LocalDB, database Nagapie.
- Browser walkthrough against SQL Server: registration, onboarding, draft save, manual review/commit, list reload, original dump history, logout and login.
- Synthetic browser-test account and its data removed afterward. Local schema remains ready for normal use.
- Relational integration tests verify anonymous denial, per-user isolation, wrong-owner headers, CSRF, persisted data after login, stale-write/delete conflicts, original-dump retention, per-user clear, invalid input, duplicate/weak registration and account lockout.
- SQL client tests verify server reads, failed-write version preservation and no automatic overwrite on conflict.
- No localStorage or storage bridge calls remain in application code.
- No Azure resources provisioned or deployed. Hosted SQL connection/schema still need configuration.
- Email verification, email password recovery, MFA and account deletion are not part of this iteration.
- Live external AI/Payhip and the complete real-device/browser matrix were not rerun.

The historical local-only checks below describe the previous build. Offline list editing is superseded: this account version requires an online server for all user data.

---

# Verification — 18 September 2026

## Passed

- .NET 10 Debug and Release builds.
- 32 automated .NET tests: 13 client/persistence/calendar/localization tests and 19 API/provider/access tests.
- Four isolated service-worker tests: asset installation, cached navigation, API bypass, and old-cache cleanup.
- Release publication, including the static Blazor app.
- HTTP verification of all 106 published manifest assets against their SHA-256 hashes.
- Published HTML startup script resolves correctly (the hosted publish fingerprint-placeholder issue was fixed).
- Browser walkthrough: NL/EN switching, onboarding acknowledgement, example input, local draft, AI consent, missing-key failure, save-as-one fallback, review editing, category/time changes, save, Let go, Undo, delete confirmation with Cancel focused, calendar export controls, custom category creation, and a full reset of disposable test data.
- Saved list survives page reload and the transition from development to published build.
- Desktop and 320-pixel mobile visual inspection. Mobile document width is exactly 320 pixels, without horizontal overflow.
- No runtime errors observed on the working published page.
- Published PWA reopened with the server stopped after its new service worker activated. Saved list navigation and item completion worked with no server connection.

## Not claimed as verified

- Live AI quality: no provider API key was supplied. Provider request/response behavior is tested with fake HTTP responses; the app itself never uses fake AI output.
- Live Payhip purchase/license behavior: product credentials were not supplied; production payments remain off.
- Real-device installation/update/offline matrix. The in-app browser offline check passed after closing the old-version tab; a normal Chrome browser was unavailable to the browser tool.
- Real microphone recognition and five-minute recording behavior.
- Real Apple/Google/Outlook calendar imports.
- The complete Safari/Chrome/Edge/Firefox and iOS/Android matrix.
- Full screenreader audit and public-production legal readiness.

## Reproduce

```powershell
dotnet test Nagapie.BraindumpLite.sln -c Release
node --test tests/service-worker.test.mjs
dotnet publish src/Nagapie.BraindumpLite.Api -c Release -o artifacts/publish
# With the published API running:
node tests/verify-publish.mjs http://localhost:5211
```

The local preview for this session runs the final published output in artifacts/app on http://localhost:5211. Development restart instructions and secure AI configuration are in README.md.
# Relational data upgrade verification

- 78 .NET tests passed (24 client, 54 API/domain), including per-item concurrency, stale draft rejection, category reference cleanup, legacy import/rollback, reset isolation, client delta requests, and SQL history pagination.
- Four service-worker tests passed; saved user data remains network-only.
- SQL Server LocalDB upgrade check used an isolated temporary database with the previous migration and synthetic saved data. Applied the new migration and importer under `db_datareader`, `db_datawriter`, and `db_ddladmin`; verified original history, imported thought content, preserved archive, repeat-safe import, and relational updates. Temporary database was removed afterward; the user's live database was not used for this test.
- EF reports no pending model changes; formatting verification passed. Release publishing succeeded in `artifacts/relational-release`.
- Azure deployment remains a user action. Follow the one-time stop/publish/start cutover in `sql-and-accounts.md` to prevent old instances writing JSON during import.
