# Project Snapshot

> High-level, point-in-time view of the system's current state. Update this file whenever
> architecture, database structure, APIs, business rules, infrastructure, or major workflows
> change significantly — see the "Living documentation" section of [`CLAUDE.md`](CLAUDE.md).
>
> Last updated: 2026-07-10, branch `feature/subscription-quotas`.

## What this system is

GamaEdtech Backend is a layered ASP.NET Core (.NET 10) REST API for the Gamatrain ed-tech
platform. It serves: a crowdsourced school directory with multi-dimension parent reviews, a blog,
a curriculum/exam content model, a gamified points ledger, crypto (Solana) + Stripe payments, a
quota-based subscription system (separate from the points ledger — see
[`docs/business/subscriptions.md`](docs/business/subscriptions.md)), and a support-ticket system.
Full domain detail: [`docs/business/`](docs/business/).

## Architecture at a glance

Clean/onion-style layering: `Domain` (entities, smart enums, specifications) ← `Application`
(interfaces + services, `ResultData<T>` everywhere) ← `Infrastructure` (EF Core DbContext,
providers) ← `Presentation` (view models + ASP.NET Core API). A large in-house framework lives in
`Core/Common` (DI-by-attribute, generic `Startup<TUser,TRole>`, specification base classes,
`ApiResponse<T>` envelope). Full detail: [`docs/architecture/`](docs/architecture/).

Key conventions every contributor (human or AI) should already know before touching code:
- Services never throw to callers — they return `ResultData<T>` with an `OperationResult`.
- Query filtering is via composable `ISpecification<T>` classes, not ad-hoc LINQ in controllers.
- Dependencies are injected as `Lazy<T>` almost everywhere.
- External integrations (email, file storage, payment gateways, currency conversion) go through a
  provider interface + `IGenericFactory<TProvider, TEnum>`, keyed by a smart enum.
- **HTTP status codes do not reflect success/failure** — nearly everything returns `200 OK`; check
  the `succeeded`/`errors` fields in the JSON body. See
  [`docs/api/overview.md`](docs/api/overview.md#known-limitations).

## Data

~109 EF Core migrations, no baseline/squash yet. Migrations apply automatically at process
startup. Full entity catalog: [`docs/database/schema.md`](docs/database/schema.md). Notable
recent fix: the `ImportLocations` migration (bulk-seeds ~156k location rows from an embedded
resource) now runs in 1000-line SQL batches instead of one giant batch, to avoid SQL Server
"insufficient memory to compile" errors (701) on memory-constrained instances — see
[`docs/database/migrations.md`](docs/database/migrations.md).

## API

Versioned URL-segment routing (`api/v1/...`), three auth schemes (Identity cookie, custom opaque
bearer token, API key — no JWT), `ApiResponse<T>` JSON envelope on every action. Full endpoint
catalog: [`docs/api/endpoints.md`](docs/api/endpoints.md).

## Known risks / open issues (carried from a 2026-07-07 deep static review, `ANALYZE.md`, untracked)

These are real, current issues a new contributor should be aware of, not hypothetical:

- **Secrets are committed** in `src/Presentation/Api/appsettings.json` (email provider token,
  payment gateway API key, root API key). They need rotating and moving to environment
  variables/Key Vault; this has not been done yet. Never add new secrets to a tracked file.
- **Exception messages leak to API clients** — most controllers/services return raw
  `exception.Message` in the error response body.
- **Payment verification needs concurrency/authorization hardening** before it should be
  considered a stable, audited path for high-value transactions — see
  [`docs/business/payments-and-points.md`](docs/business/payments-and-points.md) (mechanism
  details intentionally kept out of this public repo; see the internal review).
- **Near-zero real test coverage** — the existing xUnit tests are integration tests requiring a
  live database and are not run in CI. See [`docs/development/testing.md`](docs/development/testing.md).
- **No CI test/lint gate** — all three deploy workflows build and deploy directly with no
  `dotnet test` step. See [`docs/deployment/ci-cd.md`](docs/deployment/ci-cd.md).

None of the above block day-to-day feature work, but they should inform priorities and should not
be treated as "someone already fixed this."

## Open design discussion (not yet implemented)

**School "review score" vs. internal ranking score are conflated.** The public-facing 0-5 school
rating (`reviewScore` in the schools list API) is currently derived from an internal ranking
`Score` (a Hangfire-computed points system mixing review average with contact-info/photo/coordinate
completeness) via a mis-scaled formula, rather than from actual parent review averages. A fix is
designed but awaiting a product decision — see
[`docs/business/school-scoring-analysis.md`](docs/business/school-scoring-analysis.md) before
touching either the ranking job or the school list API.

## Recent notable changes

- Fixed `ImportLocations` migration batching (SQL Server error 701 on constrained instances).
- Full documentation system created (this file, `docs/`, `CLAUDE.md`, updated `README.md`/`CONTRIBUTING.md`) — 2026-07-10.
- **Quota-based subscription system built** (2026-07-10, phase 1 — see
  [`docs/business/subscriptions.md`](docs/business/subscriptions.md)): `SubscriptionPlan` no
  longer carries a price — pricing moved to `SubscriptionPlanPrice` (regional-pricing-ready,
  gated dormant behind `Subscription:RegionalPricingEnabled`, default `false`) and quotas moved to
  a new `Feature`/`SubscriptionPlanFeature` catalog. Purchasing a plan reuses the existing
  Payment/gateway checkout flow (never the currency→points conversion); `games/spends` now tries
  subscription quota before falling back to wallet points, unchanged for non-subscribers.
  Deliberately deferred: PayPal, native recurring billing, a real FX source for base-currency
  reporting, and in-house pastpaper file serving.

## Documentation completeness

All six `docs/` subfolders (`architecture`, `business`, `database`, `api`, `development`,
`deployment`) are populated as of 2026-07-10. Treat this documentation as source of truth over
memory or assumption — but if you find it wrong, fix it in the same change, per `CLAUDE.md`.
