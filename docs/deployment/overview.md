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
