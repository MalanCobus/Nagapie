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
