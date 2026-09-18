# Privacy and data flow

| Data | Processing | Storage |
|---|---|---|
| Draft and reviewed items | Browser; full draft and category names sent to the selected AI provider only after consent | Per-user relational SQL rows; temporary review suggestions remain JSON |
| Accounts | ASP.NET Identity | Email, password hash, lockout/security metadata in SQL |
| Audio | Browser speech-recognition provider when explicitly enabled | Nagapie never stores audio |
| AI requests | Own API and configured external provider | Drafts and saved content are in SQL; no request/response body logging |
| License | Own API and Payhip over HTTPS | Signed unlock token saved in the account's SQL document |
| Request metadata | ASP.NET Core / hosting layer | Hosting/log policy must be finalized |
| Calendar export | Browser | User downloads .ics |
| Support | User's mail client | Recipient mail provider, when user sends it |

Do not enable ASP.NET HTTP body logging, provider SDK verbose logging, analytics capturing form content, or proxy query logging for outgoing license requests. Outgoing HttpClient logs are disabled.

The relational upgrade retains previous JSON documents as a recovery archive until a later reviewed cleanup. Individual edits/deletions do not rewrite this archive. Delete-all removes its content alongside current user data. Account deletion (when implemented) must include both current content and the archive.

Provider retention, processing location, training policy, operator business details, and log retention are not invented. Complete these before public launch and update the visible privacy text and consent flow accordingly.
