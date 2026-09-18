# SQL storage and accounts

Nagapie now requires an account. Registration, login and logout are included. All saved user data lives in SQL Server or Azure SQL, not browser storage. Local browser data from the old version is neither read nor imported.

## What is saved

- Identity tables: email, password hash, account security and lockout information. Passwords are never stored as plain text.
- `UserDocuments`: one versioned SQL row per user and data section (settings, draft/review, items/todos with commit receipts, categories, access). The section is serialized JSON in a SQL column, not a file or browser record. This preserves atomic updates to the existing item-list model. Individual todos are not separate relational rows in this version.
- `BrainDumps`: original text and metadata for each committed braindump, with its owning user. The history page lists these originals.

Every query takes its owner from the authenticated server identity. The browser cannot choose another owner. Mutations require an antiforgery token. An account header must match the authenticated identity, so a tab opened under a different account cannot silently read or save under the newly signed-in account.

Version tokens reject stale writes with a conflict message. Reload to see another device's changes; there is no live collaboration or offline save queue. Delete-all clears only the signed-in user's data, including original dumps, while keeping the account. Versioned tombstones prevent stale edits from resurrecting deleted sections.

The browser still uses session/security cookies and may cache public app files. Neither contains saved thoughts. Unsaved text exists only in the current page's memory and is lost if the page closes before a successful save.

## Run locally from Visual Studio

The local `Nagapie` database has been created on this machine using SQL Server LocalDB. Set the API as the startup project and run the normal `http` or `https` profile. Open the website and create your own account.

On a new development machine, install SQL Server Express LocalDB through Visual Studio Installer (Data storage and processing), then run this once from the solution folder in Visual Studio's terminal:

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
6. In Visual Studio **SQL Server Object Explorer**, add the Azure SQL server, connect to the `Nagapie` database, open a new query, and run `docs/sql-schema.sql`. This generated script creates the Identity and application tables and can be rerun because it checks migration history. Use schema-deployment credentials for this step.
7. Set up a dedicated database user for the app with read/write permissions on these tables, rather than leaving the app running as the server administrator. The application does not run migrations at normal startup.
8. In your Azure App Service, open **Settings → Environment variables → App settings**. Add `ConnectionStrings__Nagapie` (two underscores) using the ADO.NET connection string for that database and application user. Keep `Encrypt=True` and `TrustServerCertificate=False` for Azure SQL.
9. Save the settings, then publish the **API** project from Visual Studio using the existing profile.
10. Open the website, register, save a test thought, sign out, and sign back in. Register another test user and confirm their list starts empty.

Example connection-string format (replace every placeholder; do not commit the result):

```text
Server=tcp:YOUR-SERVER.database.windows.net,1433;Initial Catalog=Nagapie;User ID=YOUR-APP-USER;Password=YOUR-PASSWORD;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
```

Azure SQL is a separate resource. No Azure database or paid plan was provisioned by this change. Existing AI and Payhip server settings retain their names.

## Schema updates and deployment

EF Core migrations are under `src/Nagapie.BraindumpLite.Api/Data/Migrations`. Apply migrations deliberately before starting a version that needs them; do not run multiple web instances racing to change the schema. Back up the database before future migrations. `docs/sql-schema.sql` is generated from the initial migration.

The API supports `--migrate` as a setup-only mode and exits afterward. On a deployment machine, it uses the configured connection string and therefore needs schema-change permissions. The normal web process only needs read/write access.

Azure App Service on Windows normally persists ASP.NET Data Protection keys in its HOME folder. Preserve those keys across restarts and instances so authentication cookies stay valid. See [Microsoft's key-storage guidance](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/default-settings?view=aspnetcore-10.0).

## Current account scope

Registration accepts an email and a 12–128 character password. Five failed password attempts lock the account for 15 minutes. Sign-in uses a non-persistent, HttpOnly, SameSite cookie, with HTTPS required in production, and an eight-hour sliding expiration.

Email verification, forgotten-password email delivery, account deletion and MFA are not implemented in this iteration. Configure an email provider and add verification/recovery before a wider public account launch. The existing optional trial counter remains a soft application limit; this change does not turn licensing into a fraud-resistant billing system.

Automated integration tests use a relational SQLite database to exercise registration, cookies, CSRF, ownership, lockout, persistence and concurrency. The migration was also applied to actual SQL Server LocalDB, and the browser save/reload flow was exercised against it.
