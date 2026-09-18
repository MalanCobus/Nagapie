# SQL storage and accounts

Nagapie now requires an account. Registration, login and logout are included. All saved user data lives in SQL Server or Azure SQL, not browser storage. Local browser data from the old version is neither read nor imported.

## What is saved

The app is named **Braindump**, under the Nagapie umbrella. Planning choices are Today, Tomorrow, Later, and a chosen calendar date. `Thoughts.PlannedDate` is a nullable SQL `date`, so a selected day does not shift with time zones. Startup moves previous `next-week` items and draft suggestions to `later`. Temporary review suggestions retain their dates in draft JSON. Today/Tomorrow remain explicit planning buckets, not automatic reminders or rolling dates.

- Identity tables: email, password hash, account security and lockout information. Passwords are never stored as plain text.
- `Thoughts`: one row per thought/todo, with typed text, category, planning horizon, completion, timestamps, source dump, and concurrency version.
- `Categories`: one row per user-owned category. Composite foreign keys prevent thoughts linking to another user's category or dump.
- `Drafts`: one current draft per user with typed text, ID, input method and AI status. Temporary review suggestions remain JSON until committed.
- `BrainDumps`: original text, metadata and commit/AI receipts. Original history is sorted and paginated in SQL, 50 records per page.
- `UserDocuments`: JSON remains for small preferences and access settings. Old items/categories/draft documents are retained only as an upgrade recovery archive and are no longer read or written by the app after import.
- `RelationalAccounts`: records completed imports and a reset token that prevents stale clients restoring cleared data.

Every query takes its owner from the authenticated server identity. The browser cannot choose another owner. Mutations require an antiforgery token. An account header must match the authenticated identity, so a tab opened under a different account cannot silently read or save under the newly signed-in account.

Each thought/category has its own version. The client sends only changed/deleted rows: two devices editing different thoughts do not conflict simply because they loaded the same list. Same-row stale writes are rejected. SQL transactions coordinate writes for each user; category deletion clears thought and draft references atomically. Reload to see another device's changes; there is no live collaboration or offline queue. The current list screen still loads all of a user's thoughts (up to the existing 5,000-item limit); the relational schema now supports future filtered/paginated list endpoints.

Delete-all clears only the signed-in user's content, including original dumps and the legacy JSON archive, while keeping the account. Reset/version tokens prevent stale edits from restoring that content.

## Publishing the relational upgrade

1. Confirm a usable Azure SQL restore point/backup before upgrading. Stop Nagapie in Azure App Service so the old application cannot write JSON during the import. Stop any other instance connected to this database too.
2. Publish the API from Visual Studio, then start the App Service. Keep the existing SQL connection and `nagapie_app` permissions; no new SQL script is needed.
3. Startup applies the additive `RelationalUserData` schema migration, then imports each existing account in a transaction before accepting traffic. Originals, IDs, timestamps, completion state and receipts are retained. Import markers make a restart safe. Invalid data stops startup and leaves that account's originals untouched; do not bypass the failure by deleting data.
4. Close existing Nagapie browser tabs and reopen the website so the PWA loads the new client. Old whole-list write requests are rejected rather than silently replacing relational rows.
5. Check a previous account's items, categories, draft and dump history; save a change and sign in again.

Some early versions did not keep original dump text. Those source IDs become explicitly marked placeholder dump rows; no original text is invented, and placeholders are excluded from original history. The archive is a recovery copy, not a live backup: new relational changes are not written back into it. Do not roll back to the JSON-based app or run the migration's `Down` method after users start editing. Plan removal of the archive in a later reviewed migration after confirming the conversion. Individual item deletion does not rewrite the historical archive; delete-all clears it.

The browser still uses session/security cookies and may cache public app files. Neither contains saved thoughts. Unsaved text exists only in the current page's memory and is lost if the page closes before a successful save.

## Run locally from Visual Studio

The local `Nagapie` database has been created on this machine using SQL Server LocalDB. Set the API as the startup project and run the normal `http` or `https` profile. Open the website and create your own account.

On a new development machine, install SQL Server Express LocalDB through Visual Studio Installer (Data storage and processing), then start the API. It creates the local database and applies pending migrations automatically. To apply migrations without starting the website, you can still run this from the solution folder:

```powershell
dotnet run --project src/Nagapie.BraindumpLite.Api -- --migrate
```

The normal development configuration defaults to `(localdb)\MSSQLLocalDB`, database `Nagapie`, using your Windows identity. To use a different SQL Server, add `ConnectionStrings:Nagapie` to the API project's Manage User Secrets. Do not commit passwords.

## Set up Azure SQL before publishing this version

Your existing hosted version is unchanged. Do these steps before publishing the account-enabled app; the hosted app now requires a SQL connection and the database schema.

1. In the Azure portal, search for **SQL databases**, then choose **Create**.
2. Select your subscription and `nagapie-rg`. Name the database `Nagapie`.
3. Create or select an Azure SQL logical server in the same region as the app. For a straightforward initial setup, configure SQL authentication and keep its administrator credentials private.
4. Check the **free database offer** if your subscription offers it. Choose **Auto-pause the database until next month** when the free amount is exhausted if you want to avoid paid overage. If no free offer is shown, review the price before creating anything. App Service F1 does not include SQL hosting automatically. See [Microsoft's free-offer instructions](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer?view=azuresql).
5. Configure the SQL server's network firewall to allow your app's outbound IP addresses (shown in App Service **Properties**) and your development computer's IP while applying the schema. Do not allow every public IP. The browser never connects directly to SQL.
6. In Visual Studio **SQL Server Object Explorer**, add the Azure SQL server and connect to the `Nagapie` database as its administrator. There is no need to run `docs/sql-schema.sql`; startup migrations create and update the tables.
7. Set up a dedicated database user for the app with read/write and schema-change permissions as described below. Existing installations using `nagapie_app` need the one-time additional permission before publishing this version.
8. In your Azure App Service, open **Settings → Environment variables → App settings**. Add `ConnectionStrings__Nagapie` (two underscores) using the ADO.NET connection string for that database and application user. Keep `Encrypt=True` and `TrustServerCertificate=False` for Azure SQL.
9. Save the settings, then publish the **API** project from Visual Studio using the existing profile.
10. Open the website, register, save a test thought, sign out, and sign back in. Register another test user and confirm their list starts empty.

Example connection-string format (replace every placeholder; do not commit the result):

```text
Server=tcp:YOUR-SERVER.database.windows.net,1433;Initial Catalog=Nagapie;User ID=YOUR-APP-USER;Password=YOUR-PASSWORD;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
```

Azure SQL is a separate resource. No Azure database or paid plan was provisioned by this change. Existing AI and Payhip server settings retain their names.

## Schema updates and deployment

EF Core migrations are under `src/Nagapie.BraindumpLite.Api/Data/Migrations`. Startup automatically applies pending migrations before serving requests. EF Core's database migration lock coordinates concurrent migration runners; migration history prevents reapplying completed migrations. If migration fails, startup fails rather than serving an incompatible schema. This does not coordinate old app instances still serving traffic: use backward-compatible migrations or stop the app during breaking schema changes. Review migrations and back up data before changes that remove or transform stored data. Developers still need to generate and include migration files when the model changes; startup does not invent migrations.

For an existing `nagapie_app` account with `db_datareader` and `db_datawriter`, run this **once**, as administrator, in the Azure `Nagapie` database:

```sql
ALTER ROLE db_ddladmin ADD MEMBER [nagapie_app];
```

This allows the app to change database schema. It is broader access than read/write and is the tradeoff for startup migrations. Keep the dedicated account scoped to this database; do not use the server administrator connection in the app. The existing migrations only need ordinary table/index schema changes plus their existing read/write permissions. Reassess permissions if future migrations manage users or other privileged objects.

`Database:ApplyMigrationsOnStartup` defaults to `true`. In Azure, set `Database__ApplyMigrationsOnStartup=false` to disable automatic migration if moving to a separate deployment migrator later. The API still supports `--migrate` as a setup-only mode that always applies migrations and exits. `docs/sql-schema.sql` remains an optional script for the initial schema; it is not the automatic update mechanism. Azure SQL database provisioning, firewall rules, and credentials remain one-time setup tasks.

Azure App Service on Windows normally persists ASP.NET Data Protection keys in its HOME folder. Preserve those keys across restarts and instances so authentication cookies stay valid. See [Microsoft's key-storage guidance](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/default-settings?view=aspnetcore-10.0).

## Current account scope

Registration accepts an email and a 12–128 character password. Five failed password attempts lock the account for 15 minutes. Sign-in uses a non-persistent, HttpOnly, SameSite cookie, with HTTPS required in production, and an eight-hour sliding expiration.

Email verification, forgotten-password email delivery, account deletion and MFA are not implemented in this iteration. Configure an email provider and add verification/recovery before a wider public account launch. The existing optional trial counter remains a soft application limit; this change does not turn licensing into a fraud-resistant billing system.

Automated integration tests use SQLite for registration, cookies, CSRF, ownership, row concurrency, category deletion, draft conflicts, legacy import/rollback and history paging. The relational upgrade has also been exercised against a disposable SQL Server LocalDB database seeded with the previous schema and saved data, under the app's read/write plus DDL roles.
