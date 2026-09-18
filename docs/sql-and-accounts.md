# SQL storage, accounts and deployment

All user content belongs to the authenticated account in SQL Server/Azure SQL. Mutations require both antiforgery protection and the matching account header. The browser retains version tokens in memory.

## Automatic deployment upgrades

The supported production release path is GitHub Actions **Deploy application**. Visual Studio's direct Azure Publish is blocked by the API project because it skips database upgrades. Local folder publishing remains available. Normal website startup never changes the database schema.

Every release first runs the same verification workflow as CI, including actual SQL Server migrations and restricted-permission tests. That job publishes an artifact tied to the selected commit. The deployment job downloads that exact artifact, checks configuration, signs in to Azure, pauses the website, upgrades and validates SQL with the deployment identity, deploys the artifact, starts the website, and checks `/health/ready` with the website's own database identity. It does not rebuild between validation and deployment. Concurrent releases are serialized and a newer run does not cancel an in-progress upgrade.

This implementation uses a maintenance window; it does not provide zero-downtime deployment. Pausing prevents older JSON-writing versions from changing data during conversion. Migration failure prevents code deployment. Any failure after pausing attempts to keep the site stopped, rather than resume an incompatible version. A timed-out runner or forcibly canceled job may not execute cleanup: check App Service state before recovery.

### One-time hosting setup

An Azure/GitHub administrator must configure the following once. These are hosting permissions and secrets, not manual database-maintenance steps.

1. Create the GitHub `production` environment, restrict deployment branches to `main`, and configure required reviewers according to your release policy. Protect `main` and require CI. Review workflow changes as privileged deployment code.
2. Configure [Azure OpenID Connect for GitHub Actions](https://learn.microsoft.com/en-us/azure/app-service/deploy-github-actions). The federated credential must trust `repo:MalanCobus/Nagapie:environment:production`. Scope its Azure deployment permissions to this App Service (for example, Website Contributor at the app resource), not the subscription. The workflow needs deployment, read, stop and start permissions.
3. Set these variables and secret on the GitHub `production` environment:

- Secret `NAGAPIE_MIGRATION_CONNECTION`: SQL connection for a deployment identity with schema and data permissions.
- Variable `AZURE_WEBAPP_NAME`: `NagapieBraindumpLiteApi20260918152910` for the current site.
- Variable `AZURE_RESOURCE_GROUP`: `nagapie-cobus` for the current site.
- Variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`: the federated deployment identity and target subscription identifiers.

4. Configure the website's runtime SQL connection as described below. Confirm both connections point to the **same server and database**, with different identities. Configure approved runner-to-SQL network access; for a private database, change the deployment job's `runs-on` to an organization-managed runner inside that network. Do not open SQL publicly to make a deployment pass.
5. Confirm Azure SQL point-in-time recovery/retention and a tested restore procedure. Persist App Service authentication keys across releases.
6. Remove legacy deployment routes: disable App Service SCM/FTP basic publishing authentication, revoke old publishing credentials, remove the obsolete `AZURE_WEBAPP_PUBLISH_PROFILE` secret, and limit Azure deployment rights to the pipeline and audited emergency operators. The project guard prevents accidental publishing; Azure permissions enforce it against bypasses. Disable any other deployment workflow or Deployment Center integration that publishes without this upgrade gate.

### Each release

Push the reviewed changes to `main`, open **GitHub → Actions → Deploy application → Run workflow**, select `main`, and complete any configured environment approval. The pipeline performs all upgrade and deployment steps automatically. No Azure console commands or application startup migration switches are needed.

If verification/configuration fails before maintenance, the running site is untouched. If the upgrade, deployment or readiness check fails after maintenance starts, inspect the failed job's safe diagnostics, fix the cause, and rerun the release. Migrations and imports are retryable. Never automatically run migration `Down` methods or restart an old JSON-writing binary after conversion. For unrecoverable changes, restore to a separate database at a verified restore point and release a compatible application against it under an approved recovery procedure; a code rollback alone is not a database recovery.

The website's `ConnectionStrings__Nagapie` must use a separate identity with `db_datareader` and `db_datawriter`, without `db_ddladmin` or `db_owner`. For an existing runtime identity previously granted DDL, remove that membership after configuring the deployment identity:

```sql
ALTER ROLE db_ddladmin DROP MEMBER [nagapie_app];
```

The SQL Server integration test verifies runtime reads, writes, retry receipts and application locks under these restricted permissions, and verifies CREATE TABLE is denied.

EF tracks applied migrations and coordinates migration runners. Each legacy account imports in its own transaction. Validation failures log the account ID and failure type, preserve original documents, continue validating other accounts, and make the deployment command exit unsuccessfully. Existing website startup is independent of this command. Fix the reported account's legacy data with an appropriate backup/recovery procedure and rerun the deployment.

The workflow pauses the target App Service during conversion. If another service or deployment slot also writes to this database, include it in the maintenance procedure before running the upgrade. The older JSON app must never write during or after import.

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
