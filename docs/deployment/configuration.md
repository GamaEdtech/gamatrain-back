# Configuration

## Structure

Configuration follows the standard ASP.NET Core layered-JSON pattern, loaded explicitly in `src/Core/Common/Hosting/Host.cs:38-43`:

1. `src/Presentation/Api/appsettings.json` — base configuration, **tracked in git**.
2. `appsettings.{ASPNETCORE_ENVIRONMENT}.json` (e.g. `appsettings.Development.json`) — optional environment override, loaded from the current working directory at run time. `src/Presentation/Api/appsettings.Development.json` is listed in `.gitignore` and is not tracked — it is the correct place for any developer-local values (connection strings, local Redis endpoint, local API keys).
3. Environment variables — loaded last, so they win over both JSON layers.

If `ASPNETCORE_ENVIRONMENT` is unset, `Host.cs:41` falls back to `"Production"` and looks for `appsettings.Production.json` (optional).

## Section names present in `appsettings.json`

(Names only — see the secrets callout below for why no values are reproduced here.)

- `Connection` — `ConnectionString`, `DefaultSchema`, `SensitiveDataLoggingEnabled`, `DetailedErrorsEnabled`, `ProviderType`, `License`.
- `EnableAudit`, `AutoConfirmComments`, `AutoConfirmPosts`, `DaysDistanceForRemoveOldRejectedSchoolImages` — top-level feature flags/settings.
- `FileProvider` — `Type` (switch) + `Azure`, `Local`, `AmazonS3` sub-sections.
- `EmailProvider` — `Type`, `Emails` (list of named mailboxes), `SupportEmail`, `NoReplyEmail`, `Resend` sub-section (`ApiToken`, `Secret`).
- `Captcha` — `Type`, `Google` sub-section (`Uri`, `SecretKey`).
- `Authentication` — `Google` sub-section (`ClientId`, `ClientSecret`). `ClientId` is required for `identities/tokens/google`: it is the audience every Google ID token is checked against, and without it all Google sign-ins are rejected. It must be the same OAuth client ID the frontend uses to get the token.
- `Serilog` — standard Serilog configuration schema (`Using`, `LevelSwitches`, `MinimumLevel`, `WriteTo`, `Enrich`).
- `IdentityOptions` — `Lockout`, `Password`, `SignIn`, `User`, `Tokens.ApiDataProtectorTokenProviderOptions`, `SecurityStampValidator`, `DataProtection` (password/lockout policy and custom token provider settings).
- `Cache` — `InstanceName`, `Configuration` (Redis connection string).
- `PaymentGateway` — `Stripe` (`ApiKey`, `WebhookSecret` - Stripe's webhook signing secret from the Dashboard's webhook endpoint config, required to verify `POST payments/webhooks/stripe` events for native recurring billing; empty by default in the tracked file, every recurring-webhook code path fails closed until it's set, same pattern as `Core:JwtSigningSecret`), `GamaTrain` (`Uri`, `ApiKey`), plus `ConvertUri`, mint/wallet addresses, `CallbackBaseUrl` for the Solana-based gateway. `Stripe:ApiKey` is also the platform key for **Stripe Connect commission payouts** (2026-10-07): Connect must be enabled on that Stripe account (separately in test and live mode), and the platform's available USD balance must cover approved payouts. No extra key or webhook secret. If `ApiKey` is a **restricted** key (`rk_...`), it also needs the Connect permissions: Accounts (write), Account Links (write), Transfers (write) and connected-account read (`connected_account_read`); otherwise onboarding fails with a permission error (seen on sandbox 2026-10-07). The onboarding return URL is checked against `CorsUrls`, so each frontend origin that starts Stripe onboarding must be listed there. See `docs/business/content-delivery.md`, "Stripe Connect payouts".
- `Core` — external "Core"/gama-api service base URLs (`Cdn`, `Url`, `Test`, `ExamResult`, `Exam`, `ExamTest`, `Boards`, `ExamDetailsUrl`, `Login`, `Register`, `Recovery`, `GoogleAuth`), used by both the pre-existing Core integration and the temporary legacy-auth-bridge, plus `JwtSigningSecret` — the real HS256 key gama-api signs its JWTs with, required to cryptographically verify any legacy JWT presented to gamatrain-back (see `docs/api/authentication.md`). Empty by default in the tracked file; must be obtained from the gama-api team out-of-band and set via environment-specific secret configuration — every legacy-JWT code path fails closed until it's set. (`UserInfo` removed 2026-09-03 alongside the `tokens/old` endpoint it only backed. `ExamInfo` (`exams/start/{id}`) replaced 2026-09-24 by `Exam` (`exams/{id}`) and `ExamTest` (`examTests?id={id}`, one call per question; since 2026-10-01 `examTests?exam_id={id}`, one call per exam) for the exam export — see `docs/business/exams-and-content.md`.) The MCP exam import (2026-10-08) added `Types` (`types/list`), `PastPapers` (`tests`), `Search` (`search`, the paper directory's title search, 2026-10-10), `Upload`, `AddExamTest` (`POST examTests`), `CurrentExam` (`exams/current`), `AddExam` (`POST exams`), `ExamQuestions` (`exams/tests/{0}`) and `PublishExam` (`exams/publish/{0}`); it also uses `Test` (to change or delete a question), `Exam` (to read, update or delete a draft; its origin is also where the preview widget loads question images from) and `ExamTest` (a draft's questions), and, for staff starting from a paper on gamatrain, the content-delivery keys `TestDetails` (`tests/{0}`) and `TestDownload` (`tests/download/{0}/{1}`).
- `Mcp` (2026-10-08, the MCP exam import and its OAuth server) — `PublicUrl`: the public https origin MCP clients reach this API at (the OAuth issuer, the `/mcp` resource, the signed figure upload link). **Set it in every deployed environment** (in the environment's server-side settings, not the tracked `appsettings.json`, which each deploy replaces; see `overview.md`): behind the reverse proxy the request looks like `http`, and an empty value falls back to the request's origin, which is only right for local development. The proxy must send `X-Forwarded-Proto`. `ExamUrl` and `ExamDraftUrl` (gamatrain.com's exam page and exam builder; `{0}` = the gama-api exam id). Everything it encrypts (access tokens, the upload link, client ids) uses Data Protection, whose keys are in the database, so there is no new secret; `Core:JwtSigningSecret` must be set, as for the legacy-auth bridge.
- `ApiKey` — root API key used by the ApiKey auth scheme.
- `CorsUrls` — allow-listed CORS origins.
- `HeadlessBrowser` — `DownloadPath` (optional): where PuppeteerSharp downloads `chrome-headless-shell` for the exam
  exports. Must be writable by the service user; unset means `<temp>/gamaedtech-chrome`. Not in the tracked file. See
  `docs/deployment/overview.md`.
- `AllowedHosts`.

## Secrets — do not propagate

**`src/Presentation/Api/appsettings.json` currently contains committed secret-looking values** in several of the sections listed above. Treat every value currently in that tracked file as untrustworthy/compromised — they must be rotated and moved to environment variables, `dotnet user-secrets` (local dev), or a proper secret store (e.g. Azure Key Vault) for any environment beyond a throwaway local dev database. Git history retains current values even after they are edited or removed, so rotation (not just removal) is required. This is a standing P0 action item, independent of any documentation change.

**Do not commit real secret values into any new configuration file or documentation.** When adding a new configuration section:
- Use empty strings or obviously-fake placeholders (as several sections in `appsettings.json` already do, e.g. `FileProvider.Azure.ConnectionString`, `Captcha.Google.SecretKey`) in the tracked base file.
- Put real values in `appsettings.Development.json` (gitignored, local only) or environment-specific secret storage, never in a tracked file.
- Do not reference actual key/token/connection-string values in markdown docs, code comments, or commit messages.
