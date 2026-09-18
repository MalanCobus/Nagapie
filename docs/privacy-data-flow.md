# Privacy and data flow

| Data | Processing | Storage |
|---|---|---|
| Draft and reviewed items | Browser; full draft and category names sent to the selected AI provider only after consent | Versioned localStorage |
| Audio | Browser speech-recognition provider when explicitly enabled | Nagapie never stores audio |
| AI requests | Own API and configured external provider | No Nagapie content database or request/response body logging |
| License | Own API and Payhip over HTTPS | Only signed unlock token saved locally |
| Request metadata | ASP.NET Core / hosting layer | Hosting/log policy must be finalized |
| Calendar export | Browser | User downloads .ics |
| Support | User's mail client | Recipient mail provider, when user sends it |

Do not enable ASP.NET HTTP body logging, provider SDK verbose logging, analytics capturing form content, or proxy query logging for outgoing license requests. Outgoing HttpClient logs are disabled.

Provider retention, processing location, training policy, operator business details, and log retention are not invented. Complete these before public launch and update the visible privacy text and consent flow accordingly.
