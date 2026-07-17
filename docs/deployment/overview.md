# Deployment Overview

Three GitHub Actions workflows in `.github/workflows/` each build and deploy the API independently; there is no shared build artifact reused between them, and none of them run tests. A separate workflow provides automated PR review.

| Workflow | Trigger | Target | What it does |
|---|---|---|---|
| `main_gamaedtechv2.yml` | push to `main` (+ manual `workflow_dispatch`) | Azure Web App (`gamacoreapp`, `Production` slot) | `dotnet build` + `dotnet publish` → upload artifact → `azure/login` + `azure/webapps-deploy@v3`. |
| `staging.yml` | push to `staging` (+ manual `workflow_dispatch`) | VPS via SSH (`SANDBOX_VPS_*` secrets), path `/var/www/stagegamacoreapp` | `dotnet build` + `dotnet publish` → `scp-action` copies the publish output to the VPS → `ssh-action` restarts `stagegamacoreapp.service`. |
| `vps-deploy-dotnet.yml` | push to `main` (+ manual `workflow_dispatch`) | A second VPS target via SSH (`VPS_*` secrets), path `/var/www/gamaapp` | Same build/publish pattern → `scp-action` → `ssh-action` restarts `gamaapp.service`. |
| `ai-review` | `pull_request_target` (opened/reopened/synchronize) against `staging` | N/A (review only) | Diffs the PR against its base, calls the OpenAI API (`gpt-4o`) to produce a structured code review, and posts it as a PR comment. Does not gate merges. |

Notes:
- A push to `main` triggers **both** `main_gamaedtechv2.yml` (Azure) and `vps-deploy-dotnet.yml` (a VPS) — two independent deploy targets fire from the same push.
- `staging.yml` deploys to a VPS, not a separate cloud "staging" App Service — despite the workflow name, there is no Azure staging slot involved.
- No workflow runs `dotnet test`, `dotnet format`, or any static analysis/security scan as a merge or deploy gate. See `docs/deployment/ci-cd.md` for per-workflow detail and gaps.

## New native dependency: headless Chromium (exam Word export)

`MathJaxFormulaRenderProvider` (used by the `Word` branch of `POST /exams/export`, see
`docs/business/exams-and-content.md`) launches `chrome-headless-shell` via PuppeteerSharp to render
exam formulas. PuppeteerSharp downloads the browser binary itself on first use (no separate install
step, cached under the app's own directory) — but that binary still needs these native shared
libraries present on the **host OS**, which none of the three deploy targets above are confirmed to
have:

```
libatk1.0-0 libatk-bridge2.0-0 libcups2 libxcomposite1 libxdamage1 libxfixes3 libxrandr2
libgbm1 libxkbcommon0 libasound2 libpango-1.0-0 libpangocairo-1.0-0 libcairo2 libxrender1
libdrm2 libglib2.0-0 libatspi2.0-0 libxi6 libxtst6 libnspr4 libxss1 libxcursor1 libxres1
```

(Package names above are Debian/Ubuntu; exact names/availability vary by distro and version — e.g.
newer Ubuntu suffixes several of these with `t64`.) If a library is missing, `chrome-headless-shell`
fails to launch and formula rendering falls back to raw unrendered `$...$` text rather than crashing
the export — so this fails **silently** in production rather than as a deploy-time error. Verify
these are installed (or bake them into the deploy image/VM) on Azure Web App and both VPS targets
before relying on formula rendering in Word exports.
