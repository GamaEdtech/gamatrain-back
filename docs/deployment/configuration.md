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
- `Authentication` — `Google` sub-section (`ClientId`, `ClientSecret`).
- `Serilog` — standard Serilog configuration schema (`Using`, `LevelSwitches`, `MinimumLevel`, `WriteTo`, `Enrich`).
- `IdentityOptions` — `Lockout`, `Password`, `SignIn`, `User`, `Tokens.ApiDataProtectorTokenProviderOptions`, `SecurityStampValidator`, `DataProtection` (password/lockout policy and custom token provider settings).
- `Cache` — `InstanceName`, `Configuration` (Redis connection string).
- `PaymentGateway` — `Stripe` (`ApiKey`), `GamaTrain` (`Uri`, `ApiKey`), plus `ConvertUri`, mint/wallet addresses, `CallbackBaseUrl` for the Solana-based gateway.
- `Core` — external "Core"/gama-api service base URLs (`Cdn`, `Url`, `Test`, `ExamResult`, `ExamInfo`, `UserInfo`, `Boards`, `ExamDetailsUrl`, `Login`, `Register`, `Recovery`, `GoogleAuth`), used by both the pre-existing Core integration and the temporary legacy-auth-bridge (see `docs/api/authentication.md`).
- `ApiKey` — root API key used by the ApiKey auth scheme.
- `CorsUrls` — allow-listed CORS origins.
- `AllowedHosts`.

## Secrets — do not propagate

**`src/Presentation/Api/appsettings.json` currently contains committed secret-looking values** in several of the sections listed above. Treat every value currently in that tracked file as untrustworthy/compromised — they must be rotated and moved to environment variables, `dotnet user-secrets` (local dev), or a proper secret store (e.g. Azure Key Vault) for any environment beyond a throwaway local dev database. Git history retains current values even after they are edited or removed, so rotation (not just removal) is required. This is a standing P0 action item, independent of any documentation change.

**Do not commit real secret values into any new configuration file or documentation.** When adding a new configuration section:
- Use empty strings or obviously-fake placeholders (as several sections in `appsettings.json` already do, e.g. `FileProvider.Azure.ConnectionString`, `Captcha.Google.SecretKey`) in the tracked base file.
- Put real values in `appsettings.Development.json` (gitignored, local only) or environment-specific secret storage, never in a tracked file.
- Do not reference actual key/token/connection-string values in markdown docs, code comments, or commit messages.
