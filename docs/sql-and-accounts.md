# SQL storage, accounts and deployment

All user content belongs to the authenticated account in SQL Server/Azure SQL. Mutations require both antiforgery protection and the matching account header. The browser retains version tokens in memory.

## Automatic deployment upgrades

Normal website startup never runs migrations in production. The manually dispatched `.github/workflows/deploy.yml` publishes the application, runs `--migrate` with a separate deployment connection, and deploys to Azure only if the upgrade and import validation succeed.

Configure the GitHub `production` environment:

- Secret `NAGAPIE_MIGRATION_CONNECTION`: SQL connection for a deployment identity with schema and data permissions.
- Secret `AZURE_WEBAPP_PUBLISH_PROFILE`: the target App Service publish profile.
- Variable `AZURE_WEBAPP_NAME`: the existing App Service name.
- Allow the deployment runner to reach SQL using your approved network arrangement. A self-hosted runner can avoid opening SQL to public hosted runners.

The website's `ConnectionStrings__Nagapie` must use a separate identity with `db_datareader` and `db_datawriter`, without `db_ddladmin` or `db_owner`. For an existing runtime identity previously granted DDL, remove that membership after configuring the deployment identity:

```sql
ALTER ROLE db_ddladmin DROP MEMBER [nagapie_app];
```

The SQL Server integration test verifies runtime reads, writes, retry receipts and application locks under these restricted permissions, and verifies CREATE TABLE is denied.

EF tracks applied migrations and coordinates migration runners. Each legacy account imports in its own transaction. Validation failures log the account ID and failure type, preserve original documents, continue validating other accounts, and make the deployment command exit unsuccessfully. Existing website startup is independent of this command. Fix the reported account's legacy data with an appropriate backup/recovery procedure and rerun the deployment.

Before upgrading a deployment that still writes legacy JSON, stop those old writers during conversion. Keep a restore point. Additive schema changes support the current relational app during deployment, but the older JSON app must never write during or after import. Do not run migration Down methods after users edit relational data.

This separation follows [Microsoft's migration deployment guidance](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying).

## Local setup

Install .NET 10 and SQL Server LocalDB, then run:

```powershell
dotnet run --project src/Nagapie.BraindumpLite.Api -- --migrate
dotnet run --project src/Nagapie.BraindumpLite.Api --launch-profile http
```

Development defaults to `(localdb)\\MSSQLLocalDB`, database `Nagapie`. Override `ConnectionStrings:Nagapie` through user secrets for another SQL Server. Optional `Database:ApplyMigrationsOnStartup=true` is honored only in Development; it defaults to false. Azure SQL provisioning, credentials, firewall rules and backups remain hosting setup tasks.

## Account sign-in

Registration creates the account and signs the user in immediately. Existing accounts can sign in without confirming their email address. Email confirmation, password-reset emails and recovery screens are deferred; no email provider, SMTP credentials or AccountEmail settings are needed.

Keep Data Protection keys persistent and shared across app instances so authentication cookies remain usable across restarts. Account deletion and MFA remain separate product features. Passwords require 12–128 characters; five failed sign-ins lock the account for 15 minutes.

## Typed operations, concurrency and pagination

`IUserDataStore` exposes typed category, thought, draft and preference operations. Collection omission never means deletion. AppState serializes entire state mutations, including the final in-memory update. SQL uses per-user transaction-owned application locks plus per-row optimistic versions.

Thought commands carry operation IDs. SQL persists a request hash and the original response in the same transaction as the mutation. Identical retries return that response, including its authoritative timestamps and versions; changed payloads conflict. Reset changes the account epoch and removes receipts, so old commands cannot recreate cleared content. Receipts stay server-side. Receipt response bodies can retain earlier thought text until delete-all, alongside the pre-existing legacy recovery archive; account deletion must include these stores.

Thoughts are queried in SQL with page size 1–100, category/unsorted, completion and horizon filters. The list displays 50 thoughts per page with navigation and category filtering. Ordering uses creation time plus ID. Concurrent writes can move entries between offset pages; reload to refresh. Reads compare the reset epoch before and after instead of acquiring serializable range locks. Receipt history is not returned. The compatibility `items` read is bounded to the first 50 thoughts.

Completion state/date consistency is validated. Creation, update and completion-transition times come from the server. Selecting the let-go category archives a thought; restoring a released thought clears that category on the server.

## Trial and entitlements

The server records which dump IDs successfully received AI processing and counts successful AI commits atomically. Browser counts and unlock tokens do not authorize access. Concurrent commits cannot exceed the enabled free allowance. Failed processing, canceled reviews and manual saves do not count. As designed, this is a saved-session trial, not a cap on every AI request; request rate limiting remains necessary.

Payhip verification binds a license hash to one account using a unique database constraint. Another account cannot claim the same license or reuse a browser token. Clearing content preserves usage and entitlement. Earlier token holders re-enter their license to bind it to the account. Refund/revocation synchronization is not implemented; operators can revoke the server entitlement. Payments remain disabled by default.

## Repeatable tests and safe diagnostics

`.github/workflows/ci.yml` starts SQL Server 2022 and runs the real EF migration test on every build. The test creates a uniquely named disposable database, migrates from the legacy schema, validates import and reruns, exercises concurrent writes and restricted runtime permissions, then removes only its own database.

To run the SQL test locally:

```powershell
$env:NAGAPIE_TEST_SQL = 'Server=(localdb)\\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True'
dotnet test
```

Without that variable the SQL-specific test is explicitly skipped; SQLite HTTP tests still run. Migration and unexpected request errors record correlation IDs, exception types, HRESULTs, SQL error numbers/state/class and method frames. Raw exception messages, SQL parameter values, passwords, connection strings and thought content are excluded.
