# CLAUDE.md — instructions for AI coding agents

Concise operating instructions for any AI agent (Claude Code or otherwise) working in this repo.
For narrative/reference documentation, don't duplicate it here — read `docs/` and
[`PROJECT_SNAPSHOT.md`](PROJECT_SNAPSHOT.md) instead. This file is about *how to work*, not *what
the system is*.

## Start here

1. [`PROJECT_SNAPSHOT.md`](PROJECT_SNAPSHOT.md) — current state, known risks, open discussions.
2. [`docs/architecture/overview.md`](docs/architecture/overview.md) — solution structure, request flow.
3. [`docs/architecture/design-patterns.md`](docs/architecture/design-patterns.md) — patterns you must follow.
4. The rest of `docs/` (`business/`, `database/`, `api/`, `development/`, `deployment/`) as needed
   for the specific area you're touching.

Full documentation map: [`README.md`](README.md#documentation-map).

## Conventions cheat sheet

When adding a feature, mirror the existing pattern (detail in
[`docs/development/coding-standards.md`](docs/development/coding-standards.md)):

1. Entity in `Domain/Entity` + EF configuration; new migration in `Infrastructure/Infrastructure/Migrations`.
2. Specifications in `Domain/Specification/<Feature>/`.
3. DTOs in `Core/Data/Dto/<Feature>/`.
4. Service contract in `Application/Interface/I<Feature>Service.cs`, implementation in
   `Application/Service` (extends `LocalizableServiceBase<T>`); all deps `Lazy<T>`; return
   `ResultData<T>`; never throw to the caller.
5. View models in `Presentation/ViewModel/<Feature>/`.
6. Controller in `Presentation/Api/Controllers` (public) or `Areas/Admin`/`Areas/Finance`
   (role-gated), routed `api/v{version:apiVersion}/[controller]`, extends `ApiControllerBase<T>`.
7. External integrations go through a provider interface in `Infrastructure/Interface` +
   `IGenericFactory<TProvider, TEnum>`, never called directly.

Non-negotiable build hygiene: `TreatWarningsAsErrors` + full analyzer set is on solution-wide
(`src/Directory.Build.props`). Package versions are centrally managed
(`src/Directory.Packages.props`) — never pin a version in an individual `.csproj`.

## Sharp edges to know before you touch related code

- **HTTP status is not the success signal.** Nearly every endpoint returns `200 OK` regardless of
  outcome; check `succeeded`/`errors` in the JSON body. Don't "fix" this as a drive-by — it's a
  known, repo-wide behavior (see `docs/api/overview.md#known-limitations`); changing it is a
  breaking API change requiring explicit sign-off.
- **`result.Data` can be null on failure.** Check `result.OperationResult`/`result.Errors` before
  dereferencing `result.Data` in a controller — this NRE bug already exists in ~20 places; don't
  add a 21st.
- **`appsettings.json` has committed secret-looking values.** Never add a new real secret to any
  tracked file. Never copy existing secret values out of that file into documentation, logs, PR
  descriptions, or anywhere else — describe config sections by name only.
- **Every `IUnitOfWorkProvider.CreateUnitOfWork()` call in one request shares the same scoped
  `DbContext`.** Don't dispose a `UnitOfWork` and don't assume `trackChanges: false` is isolated to
  one call within a request.
- **The school "ranking `RankScore`" and the public "`Rating`" are deliberately separate concepts**
  (fixed 2026-07-10, see `docs/business/school-scoring-analysis.md`): `RankScore`/`CountryRank`/
  `StateRank`/`CityRank` (`SchoolService.UpdateSchoolScoreAsync`) are the internal ranking signal
  and are **not exposed via the public API**; `Rating` is the public 0-5 rating, live
  `AVG(SchoolComments.AverageRate)`. Don't reintroduce a derivation between them, and don't add
  `RankScore` back to a public response. (`RankScore` was renamed from plain `Score`, and the
  `hasScore` list filter went `hasScore` → `hasRate` → `hasRating` as naming settled — `Score`/
  `HasScore`/`Rate`/`HasRate` were all ambiguous or mislabeled at some point; don't reintroduce any
  of the old names. `Rating`/`hasRating` are final.)
- **PRs target `staging`, not `main`.** `main` deploys straight to production.
- **Subscription quota is never derived from payment amount.** A plan's `SubscriptionPlanFeature`
  limits are fixed regardless of which regional `SubscriptionPlanPrice` was paid; buying a
  subscription never runs the amount through `ICurrencyConverterProvider` (that conversion is only
  for the unrelated points-top-up flow). This rule is about `Price`/`Currency` specifically, not
  about `BillingInterval`: since 2026-08-13, `SubscriptionPlanFeature.Limit` **does** vary by
  `BillingInterval` (Monthly vs. Annual of the same plan can carry different explicit limits, set
  per-interval by an admin, no automatic multiplier) — that's a deliberate, separate axis (which
  interval SKU was bought), not a reintroduction of price-derived quota. Two regional prices for the
  same plan+interval must still grant identical quota; never key a limit off `Price`/`Currency`/
  `ICurrencyConverterProvider`. See `docs/business/subscriptions.md`. `Feature.Code` values
  must stay in sync with the `FeatureCodes` constants — the catalog is data-driven but call sites
  that consume quota (e.g. `GameService.SpendPointsAsync`) reference the code as a compile-time
  constant. This rule is about the *limit* (never derived from the subscription's own payment); it
  does **not** forbid how much of that limit one action draws down. Since 2026-08-14,
  `GameService.SpendPointsAsync`'s `ConsumeQuotaAsync` call for content downloads consumes an
  amount equal to the downloaded item's own gama-api-reported price (`SpendPointsRequestDto.
  QuotaAmount`, set by `ContentDeliveryService`), not a flat `1` — a separate, deliberate axis (the
  content's price, not the subscription's). The client-supplied-`Points` `games/spends` endpoint
  still consumes a flat `1`, on purpose: its `Points` is never verified against gama-api, so wiring
  it into quota too would let a caller drain a feature's whole allowance in one call. See
  `docs/business/subscriptions.md` ("Quota consumption and the points fallback") and
  `docs/business/content-delivery.md` ("Charge: quota-then-points").
- **The bearer `Authorization` value is not always the plain `{userId}|{token}` format.**
  `TokenAuthenticationHandler` also accepts a raw gama-api (legacy) JWT directly — resolved via
  `ITokenService.VerifyLegacyTokenAsync` to whichever local user is linked by `CoreId` — as part of
  the temporary legacy-auth-bridge (see `docs/api/authentication.md`). Any code that parses/mints
  tokens outside that handler must account for both shapes, or use the handler/`ITokenService`
  rather than re-parsing the header itself. A legacy-bridge session also isn't revocable via
  `tokens/revoke` (JWTs are stateless) and isn't governed by this app's configurable token lifespan.
- **Never accept a gama-api (legacy) JWT without verifying its signature.** Any code that decodes
  one must go through `IdentityService.ValidateLegacyJwtAsync`, which checks the real HS256 signature
  against `Core:JwtSigningSecret` — not just issuer/audience/expiry. Skipping signature verification
  (as an earlier revision of this code did) means anyone can hand-craft a token claiming any
  `CoreId` and it will be accepted as genuine; this is a full account-takeover path, not a stylistic
  shortcut. `Core:JwtSigningSecret` is the real key gama-api signs with, obtained from their team —
  never derive or guess it.
- **`StripePaymentGatewayProvider`'s `RequestOptions` property mints a fresh `IdempotencyKey =
  Guid.NewGuid().ToString("N")` on every single access — it provides zero duplicate-request
  protection.** Every Stripe-mutating call in that file (`CreateAsync`, `CancelSubscriptionAsync`,
  `ResumeSubscriptionAsync`, `TerminateSubscriptionAsync`, `SwitchSubscriptionPlanAsync`,
  `ReleaseScheduleIfAttachedAsync`) reads this property fresh, so Stripe cannot recognize a retried or
  double-submitted request as the same logical operation — the entire point of an idempotency key.
  Fixed 2026-08-16 for the one call where this was a real, live financial risk
  (`SwitchSubscriptionPlanAsync`'s immediate-upgrade path, which bills the card synchronously via
  `ProrationBehavior = "always_invoice"`): guarded with a `UserSubscription.SwitchLockedUntil` claim
  taken *before* the gateway call — see `docs/business/subscriptions.md`, "Plan upgrade/downgrade
  with proration". The other methods on this list still share the same underlying weakness and
  haven't been individually audited/fixed — don't assume any of them are protected against a
  duplicate request just because one sibling method now is.
- **Smart enums (`Enumeration<TEnum,TKey>` subclasses) don't "just work" with Swagger/JSON in two
  specific spots — both silent, not compile errors.** (1) A smart-enum field in a JSON *body*-bound
  ViewModel needs an explicit `[JsonConverter(typeof(EnumerationConverter<T, byte>))]` attribute
  per property — the globally-registered `EnumerationConverterFactory` only matches the literal
  open generic `Enumeration<,>`, never a concrete subclass, so it silently never fires; every
  existing body-bound smart-enum field already carries this attribute, follow the same pattern.
  (2) A smart enum used as a bare `[FromQuery]` action parameter binds correctly at runtime (a
  dedicated `EnumerationQueryStringModelBinderProvider` handles it) but Swashbuckle documents it
  wrong — it expands the type into its internal properties (`Name`, `Value`,
  `LocalizedDisplayName`, ...) instead of one named parameter, because `EnumerationParameterFilter`
  (`src/Core/Common/Swagger/`) only rewrites the schema for *route-constrained* parameters
  (`{id:someConstraint}`), not query ones. Workaround used so far: declare the parameter as
  `string?`, parse with `.TryGetFromNameOrValue<TEnum, TKey>()` inside the action — see
  `ConnectionsController`'s `idType` parameters.
- **An *optional* `[FromQuery]` smart-enum (or any reference-typed) property on a ViewModel must be
  declared with `?`, or ASP.NET Core silently makes it required.** Nullable Reference Types are
  enabled solution-wide (`Directory.Build.props`) and `SuppressImplicitRequiredAttributeForNonNullable
  ReferenceTypes` is never set, so a non-nullable reference-typed property (e.g. `Status Status`,
  missing the `?`) is implicitly required by MVC's model validation — the request 400s (well, per
  the sharp edge below, actually `200`s with `succeeded:false` and `"The <Prop> field is required."`)
  before the action method ever runs, even though `EnumerationQueryStringModelBinder` itself
  correctly leaves the property `null` when the query key is absent. This is easy to introduce
  without noticing: the controller can have a perfectly correct `if (request.Foo is not null)` guard
  that looks like it handles "omitted", compiles fine, and is simply never reached in practice.
  Fixed 2026-08-15 in `PostContributionListRequestViewModel.Status` (was non-nullable, silently
  blocking the "no filter" case `BlogsController.GetPostContributionList` was written to support) —
  its sibling contribution-list request ViewModels (`SchoolContributionListRequestViewModel` etc.)
  already used `Status?` correctly; this was an isolated miss, not a repo-wide pattern, but check any
  new optional `[FromQuery]` property against this before assuming a null-check downstream will ever
  run.

## Living documentation — this is a hard requirement, not a suggestion

Documentation under `docs/`, plus `README.md`, `PROJECT_SNAPSHOT.md`, and this file, is part of
the source code. Any change that affects architecture, database structure, APIs, business rules,
infrastructure, deployment, workflows, project structure, or development conventions **must**
update the relevant documentation file(s) in the same change.

Before considering any task complete, explicitly check:

- Does this change affect **architecture**? → update `docs/architecture/`.
- Does this change affect **database structure**? → update `docs/database/`.
- Does this change affect **APIs**? → update `docs/api/`.
- Does this change affect **business rules**? → update `docs/business/`.
- Does this change affect **deployment or configuration**? → update `docs/deployment/`.
- Does this change affect **developer onboarding**? → update `docs/development/`.

If yes to any of the above:
- Update the corresponding documentation file(s) — don't just note it, actually edit them.
- Update [`PROJECT_SNAPSHOT.md`](PROJECT_SNAPSHOT.md) if the current state of the system changed
  significantly (new major feature, resolved/introduced known risk, changed architecture decision).
- Keep [`README.md`](README.md) accurate (tech stack, structure, getting-started steps).
- Keep this file (`CLAUDE.md`) synchronized if the change alters a convention or sharp edge listed
  above.

## Task completion rule

A task is **not** complete until:

1. Code changes are finished.
2. Tests pass, when applicable (see `docs/development/testing.md` for the current, limited state
   of the test suite — don't claim coverage that doesn't exist).
3. Relevant documentation has been reviewed and updated per the checklist above.

If you skip a documentation update because you judged it unaffected, that judgment should be
correct — re-read the checklist before saying a task is done, not after.
