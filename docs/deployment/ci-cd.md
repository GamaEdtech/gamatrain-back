# CI/CD Detail

Source: `.github/workflows/main_gamaedtechv2.yml`, `staging.yml`, `vps-deploy-dotnet.yml`, `ai-review`.

## `main_gamaedtechv2.yml` — Azure Web App

- **Trigger**: `push` to `main`, or manual `workflow_dispatch` (lines 6-10).
- **`build` job** (`runs-on: ubuntu-latest`):
  1. `actions/checkout@v4`.
  2. `actions/setup-dotnet@v4`, `dotnet-version: '10.x'`.
  3. `dotnet build --configuration Release` (from `src/`).
  4. `dotnet publish --configuration Release -o ${{env.DOTNET_ROOT}}/myapp` (from `src/`).
  5. `actions/upload-artifact@v4`, artifact name `.net-app`.
- **`deploy` job** (`needs: build`, environment `Production`):
  1. `actions/download-artifact@v4`.
  2. `azure/login@v2` using `AZUREAPPSERVICE_CLIENTID_*` / `TENANTID_*` / `SUBSCRIPTIONID_*` secrets (federated identity / OIDC — `permissions: id-token: write`).
  3. `azure/webapps-deploy@v3` → app name `gamacoreapp`, slot `Production`.
- **Dead/commented-out steps** (lines 27-43): an EF Core migrations step (`dotnet tool install --global dotnet-ef`, `dotnet ef database update` against `AZURE_SQL_CONNECTIONSTRING`, and a `dotnet ef migrations bundle` line) is entirely commented out. As documented in `docs/development/setup.md`, migrations are actually applied automatically at process startup (`Host.cs:76-83`), so this dead step is not filling a real gap — but it does mean there is no explicit CI record of which migration ran against which environment.
- **Gaps**: no `dotnet test` step; no lint/format/analyzer step beyond what `dotnet build` already enforces via `TreatWarningsAsErrors`; no secret scanning; runs on every push to `main` with no required PR review gate visible in the workflow itself.

## `staging.yml` — VPS (labeled "Staging")

- **Trigger**: `push` to `staging`, or manual `workflow_dispatch` (lines 3-7).
- **`build` job**: checkout → `setup-dotnet` (10.x) → `dotnet build --configuration Release` → `dotnet publish --configuration Release -o app` (both from `src/`) → upload artifact `.net-app` (path `src/app`).
- **`deploy` job** (`needs: build`):
  1. `actions/download-artifact@v4`.
  2. `appleboy/scp-action@v0.1.4` copies the published output to `SANDBOX_VPS_HOST`, path `/var/www/stagegamacoreapp`, using `SANDBOX_VPS_USER` / `SANDBOX_VPS_SSH_KEY` / `SANDBOX_VPS_PORT` secrets.
  3. `appleboy/ssh-action@v1.0.3` runs `sudo systemctl restart stagegamacoreapp.service` on the VPS.
- **Gaps**: same as above — no test step, no build artifact reuse from `main_gamaedtechv2.yml`, no rollback step if the restart fails.

## `vps-deploy-dotnet.yml` — VPS (second target, on `main`)

- **Trigger**: `push` to `main`, or manual `workflow_dispatch` (lines 3-7). This means every push to `main` triggers this workflow **in addition to** `main_gamaedtechv2.yml`.
- **`build` job**: identical shape to `staging.yml` (checkout → setup-dotnet 10.x → build → publish to `app` → upload artifact).
- **`deploy` job**: `scp-action` to `VPS_HOST`, path `/var/www/gamaapp`, using `VPS_USER`/`VPS_SSH_KEY`/`VPS_PORT` secrets, then `ssh-action` runs `systemctl restart gamaapp.service` (no `sudo`, unlike `staging.yml`'s restart command — inconsistent between the two VPS workflows).
- **Gaps**: same as `staging.yml`.
- **Fixed real production bug found live (2026-08-22): `wwwroot/Files/user` uploads were failing with
  "Access ... is denied", and the fix kept reverting on every deploy.** Root cause: 4 files under
  `wwwroot/Files/user/` had been accidentally committed to git (a stray artifact of local testing during the
  "move avatar to file providers" feature work). `dotnet publish` on the GitHub Actions runner includes
  whatever's actually tracked in the repo, so every build carried these files - owned, on the runner, by its
  own default `runner` account (Ubuntu GitHub-hosted runners use UID **1001** for this, not 1000 - see
  [actions/runner-images#10936](https://github.com/actions/runner-images/issues/10936)). `scp-action`
  transferred that ownership through unchanged, recreating the file (and its containing directory) with a
  UID that maps to no account at all on the target VPS, blocking `www-data` (the actual app service user)
  from writing new uploads into that same directory. A manual `chown` on the server fixed it only until the
  next deploy re-pushed the same tracked files with the same broken ownership. Real fix: removed the 4
  files from git and added `/src/Presentation/Api/wwwroot/Files/` to `.gitignore` - this directory is
  `LocalFileProvider`'s runtime upload target (`FileProvider:Type = Local`,
  `src/Infrastructure/Infrastructure/Provider/File/LocalFileProvider.cs`), one subfolder per `ContainerType`
  (`user`/`post`/`school`); it should never have had tracked content in the first place.

## `ai-review` — automated PR review (not a merge gate)

- **Trigger**: `pull_request_target`, types `opened`/`reopened`/`synchronize`, against base branch `staging` (lines 3-10). Uses `pull_request_target` (runs with base-repo permissions/secrets even for fork PRs) rather than `pull_request`.
- **Steps**:
  1. Checks out the base branch with full history, fetches the PR head into a local ref, and computes `git diff --name-status` + a unified diff between base SHA and PR head.
  2. A Node.js inline script chunks the diff text into ≤45,000-character chunks (env `CHUNK_MAX_CHARS`) to stay under request size limits.
  3. For each chunk, calls the OpenAI Chat Completions API (`model: gpt-4o`) with a fixed system prompt asking for a structured review (Summary / Potential Issues / Suggestions / Positive Feedback, capped at ~400 words per chunk) and the PR title + changed files/diff chunk as the user prompt. Requires `secrets.OPENAI_API_KEY`; the job fails if the secret is missing or the API call errors.
  4. Posts the concatenated review (prefixed with an "AI Generated Review" disclaimer) as one or more PR comments via `actions/github-script@v7`, splitting into multiple comments if the total exceeds ~62,000 characters.
- **Role**: advisory only — it comments on the PR, it does not block merge, set a required status check, or run any build/test itself.

## Summary of gaps across all workflows

- No `dotnet test` anywhere — the test project (`src/Test`, see `docs/development/testing.md`) never runs in CI.
- No `dotnet format` / style-only check beyond what `dotnet build` enforces via analyzers-as-errors.
- No secret scanning (relevant given `docs/deployment/configuration.md`'s callout about committed secret-looking values in `appsettings.json`).
- No dependency/vulnerability audit step (`Directory.Build.props` also suppresses `NU1902`, the NuGet "known vulnerability" warning, solution-wide).
- Two independent deploy targets (Azure + a VPS) both fire off the same `main` push, with no coordination or shared artifact between them.
