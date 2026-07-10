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
- **The school "ranking Score" and the public "rate" are deliberately separate concepts** (fixed
  2026-07-10, see `docs/business/school-scoring-analysis.md`): `Score`/`CountryRank`/`StateRank`/
  `CityRank` (`SchoolService.UpdateSchoolScoreAsync`) are the internal ranking signal; `Rate` is the
  live `AVG(SchoolComments.AverageRate)`. Don't reintroduce a derivation between them.
- **PRs target `staging`, not `main`.** `main` deploys straight to production.

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
