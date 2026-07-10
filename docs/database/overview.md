# Database Layer — Overview

Reference for the EF Core / SQL Server data layer. See `docs/database/schema.md` for the entity catalog and `docs/database/migrations.md` for migration mechanics.

## Stack

- **EF Core 10** on **SQL Server** (`Microsoft.EntityFrameworkCore.SqlServer`), with **NetTopologySuite** for geospatial columns (`geography` type: `Point`, `Polygon`).
- **`EntityFramework.Exceptions`** (`UseExceptionProcessor()`) converts raw SQL exceptions (unique-constraint violations, etc.) into typed exceptions the service layer catches for duplicate-detection control flow.
- Provider selection is test-aware: if the `Test` environment variable equals `"True"`, the context uses `UseInMemoryDatabase` instead of SQL Server — see `src/Infrastructure/Infrastructure/EntityFramework/Context/ApplicationDBContext.cs:26-32`.

## DbContext

- **Class:** `GamaEdtech.Infrastructure.EntityFramework.Context.ApplicationDBContext`
  `src/Infrastructure/Infrastructure/EntityFramework/Context/ApplicationDBContext.cs`
- It derives from a generic base, `Common.DataAccess.Context.IdentityEntityContext<TContext, TUser, TRole, TKey, TUserClaim, TUserRole, TUserLogin, TRoleClaim, TUserToken, TUserPasskey>`
  (`src/Core/Common/DataAccess/Context/IdentityEntityContext.cs`), itself an `IdentityDbContext<...>` — so `ApplicationDBContext` *is* the ASP.NET Core Identity store as well as the application's main context. There is only one DbContext in the solution.
- Registered as **Transient** via the in-house `[ServiceLifetime]` attribute-scanning DI registrar (`ApplicationDBContext.cs:16`), not a hand-written `AddDbContext` call.
- No `IDesignTimeDbContextFactory` exists; `dotnet-ef` resolves the context through the API host's own DI at design time (see `docs/database/migrations.md`).

### Connection / configuration

Configuration keys read in `IdentityEntityContext`'s constructor (`IdentityEntityContext.cs:52-58`), all under the `Connection` section of `appsettings.json`:

| Key | Purpose |
|---|---|
| `Connection:ConnectionString` | SQL Server connection string (`src/Presentation/Api/appsettings.json:2-8`) |
| `Connection:License` | Appended to the connection string (empty by default) |
| `Connection:DefaultSchema` | Optional non-`dbo` schema (`builder.HasDefaultSchema(...)`) |
| `Connection:SensitiveDataLoggingEnabled` / `Connection:DetailedErrorsEnabled` | EF diagnostics toggles |

`ApplicationDBContext.OnConfiguring` (`ApplicationDBContext.cs:22-35`) sets a 5-minute command timeout and enables `UseNetTopologySuite()` for geography columns, and always calls `UseExceptionProcessor()`.

**Local dev override:** create `src/Presentation/Api/appsettings.Development.json` (gitignored) — see `docs/development/setup.md` for a worked example.

## EF conventions used by every entity

### Interface/base-class ladder (`src/Core/Common/DataAccess/Entities/`)

| Type | File | Adds |
|---|---|---|
| `IEntity<TEntity, TKey>` | `IEntity{TEntity,TKey}.cs` | `IIdentifiable<TKey>` (an `Id`) + `IEntityTypeConfiguration<TEntity>` — every entity implements `Configure(EntityTypeBuilder<T>)` itself, applied via `builder.ApplyConfigurationsFromAssembly(...)` (`IdentityEntityContext.cs:107-108`), so there are no separate `IEntityTypeConfiguration` classes — the entity configures itself. |
| `ICreationableEntity<TUser, TKey>` / `CreationableEntity<TUser, TKey>` | `CreationableEntity{TUser,TKey}.cs` | `CreationDate` (`DateTimeOffset`, required) + `CreationUserId`/`CreationUser` |
| `IVersionableEntity<TUser, TCreationKey, TLastModifyKey>` / `VersionableEntity<TUser, TCreationKey, TLastModifyKey>` | `VersionableEntity{TUser,TCreationKey,TLastModifyKey}.cs` | Extends `CreationableEntity` and adds nullable `LastModifyDate` + `LastModifyUserId`/`LastModifyUser` |

Most content entities (`School`, `Post`, `Question`, `Tag`, `Board`, `Grade`, `Subject`, `Topic`, `Location`, `SubscriptionPlan`, `ContentLocalization`, …) extend `VersionableEntity<ApplicationUser, long, long?>`. Simpler join/log entities (`SchoolTag`, `PostTag`, `SchoolBoard`, `Reaction`) extend the lighter `CreationableEntity<ApplicationUser, long>`. Pure logs/facts with no audit trail (`Connection`, `ExamSubmission`, `TestSubmission`, `LoginHistory`, `Message`, `Payment`, `Transaction`, `VotingPower`, `Ticket`, `TicketReply`, `SiteMap`) implement `IEntity<T,TKey>` directly, optionally with `ICreationDate`/`IUserId<long>`.

`CreationUserId`/`LastModifyUserId` FKs to `ApplicationUsers` are wired generically, not per-entity: `IdentityEntityContext.OnModelCreating` reflects over every type implementing `IVersionableEntity<,,>`/`ICreationableEntity<,>` and adds the `HasOne(...).WithMany().OnDelete(DeleteBehavior.NoAction)` relationship for `CreationUser`/`LastModifyUser` automatically (`IdentityEntityContext.cs:110-135`). Individual entities never repeat this.

`CreationDate`/`CreationUserId`/`LastModifyDate`/`LastModifyUserId` are also populated automatically as **shadow-style side effects on `SaveChanges`**: `PrepareShadowProperties()` (`IdentityEntityContext.cs:209-250`) walks the change tracker, sets `CreationUserId`/`CreationDate` on `Added` entities and `LastModifyUserId`/`LastModifyDate` on `Modified` entities from the current `HttpContext` user — application code does not set these fields itself.

### Soft delete (`IDeletable`)

`GamaEdtech.Common.DataAccess.Entities.IDeletable` (`src/Core/Common/DataAccess/Entities/IDeletable.cs`) exposes a `[NotMapped] bool IsDeleted`; each implementing entity declares its own mapped `IsDeleted` column and its own query filter. **`School` is currently the only entity using this pattern** (`src/Domain/Entity/School.cs:90-91,128`):

```csharp
public bool IsDeleted { get; set; }
...
builder.HasQueryFilter(t => !t.IsDeleted).HasIndex(t => t.IsDeleted);
```

`Common.DataAccess.Specification.Impl.DeletedSpecification<TClass>` (`src/Core/Common/DataAccess/Specification/Impl/DeletedSpecification{TClass}.cs`) is a generic, reusable specification (`t => t.IsDeleted == deleted`) available to any `IDeletable` entity for querying by deleted state. As of this writing it is not referenced anywhere in `Application`/`Presentation` — no feature currently surfaces a "show deleted" query — but it's the intended building block if one is added (combine with `IgnoreQueryFilters()` at the repository/query level to see past `HasQueryFilter`).

### Content localization (`IContentLocalizeable`)

`GamaEdtech.Domain.Entity.IContentLocalizeable` (`src/Domain/Entity/IContentLocalizeable.cs`) is a bare marker interface — it adds no properties. Entities implementing it (`School`, `Board`, `Grade`, `Location`, `Post`, `Question`, `Subject`, `Tag`, `Topic`) have one or more fields whose *translated* values live outside the entity's own row, in a single shared table: `ContentLocalization` (`src/Domain/Entity/ContentLocalization.cs`, table `ContentLocalizations`).

`ContentLocalization` columns: `ContentType` (string — the owning entity's type name, e.g. `"School"`), `ContentId` (the owning row's `Id`), `Name` (the field name being localized, e.g. `"Title"`), `Value`, `LanguageId` (FK → `Languages`). Unique index on `(LanguageId, ContentType, ContentId, Name)` (`ContentLocalization.cs:44`). Because `ContentId`/`ContentType` are a generic polymorphic reference (not a real FK), there is **no FK constraint** from `ContentLocalizations` to `Schools`/`Posts`/etc. — verified against the live DB, `ContentLocalizations` has FKs only to `ApplicationUsers` (audit) and `Languages`.

`ContentLocalizationService` (`src/Application/Service/ContentLocalizationService.cs`) is the generic CRUD/lookup service every feature service calls into for translated values, keyed by `ContentType` + `ContentId` + `Name` + the caller's resolved `languageId`. `Language` itself (`src/Core/Common/Localization/Language.cs`, table `Languages`) is a small lookup table (`Code`, `IsEnable`, `IsDefault` — unique filtered index enforcing a single default language).

### Smart enumerations (`Enumeration<TEnum, TKey>`)

Enums in this codebase are not C# `enum`s but classes deriving from `GamaEdtech.Common.Data.Enumeration.Enumeration<TEnum, TKey>` (`src/Core/Common/Data/Enumeration/Enumeration.cs`) — each has a `Name` + typed `Value`, supports localized display names, and doubles as an `IRouteConstraint` so it can be bound directly from route/query values. Concrete smart enums live in `src/Domain/Enumeration/` (`SchoolType`, `Status`, `CategoryType`, `Currency`, `PaymentStatus`, `PaymentGateway`, `TransactionType`, `ConnectionStatus`, `GenderType`, `ProfileVisibility`, `LocationType`, `TagType`, `ImageFileType`, `ItemType`, `ChangeFrequency`, `Role`, `VisibilityType`, `BillingInterval`, and more — 31 files total).

They are persisted as a plain numeric column (`byte` for most, `short` for `TransactionType`) via the `OwnEnumeration<TEntity, TEnum, TKey>` extension (`src/Core/Common/Data/Enumeration/EnumerationExtensions.cs:71-74`), which is just an EF `HasConversion` mapping the smart-enum instance to/from its `Value`:

```csharp
builder.OwnEnumeration<School, SchoolType, byte>(t => t.SchoolType);
```

Every entity with a smart-enum property calls this once per property inside its own `Configure(...)`.

### Audit trail

Any entity decorated with `[Audit((int)Constants.EntityType.X)]` (currently `ApplicationUser`, `ApplicationUserClaim`, `ApplicationSettings` — see `src/Domain/Entity/Identity/ApplicationUser.cs:18`, `ApplicationUserClaim.cs:16`, `ApplicationSettings.cs:15`) has its Added/Modified/Deleted changes captured into `Audit`/`AuditEntry`/`AuditEntryProperty` rows (tables `Audits`, `AuditEntries`, `AuditEntryProperties`) by `GenerateAudit()`/`SaveAudit(Async)` in `IdentityEntityContext.cs:252-358`, gated by the `EnableAudit` config flag. Per-property opt-out via `[AuditIgnore]` (used on `SecurityStamp`, `ConcurrencyStamp`, `LockoutEnd`, `AccessFailedCount` in `ApplicationUser.cs`).

### Other conventions

- **Primary keys**: `long`/`int`/`Guid`/`Ulid` with `[DatabaseGenerated(DatabaseGeneratedOption.Identity)]`; `Guid` PKs default to `NEWSEQUENTIALID()` and `Ulid` PKs get a custom `UlidGenerator` value generator — both wired generically in `IdentityEntityContext.OnModelCreating` (`IdentityEntityContext.cs:142-155`), not per-entity.
- **Strings**: a custom `DataType` enum (`UnicodeString`, `UnicodeMaxString`, `String`, etc.) maps to `nvarchar`/`varchar` consistently via `[Column(name, DataType.X)]` (`GamaEdtech.Common.DataAnnotation.Schema`), used instead of raw `[Column(TypeName = ...)]` almost everywhere except geospatial/decimal-precision columns.
- **Decimals**: money/points columns (`Payment.Amount`, `SubscriptionPlan.Price`, `VotingPower.Amount`) explicitly call `.HasPrecision(36, 18)` to fit crypto-scale values.
- **JSON columns**: `Question.Options` (`Collection<QuestionOption>`) is mapped with `OwnsMany(...).ToJson()`; `Ticket.Receivers`/`TicketReply.Receivers` use a manual `HasConversion` to/from a JSON string; `ApplicationUserPasskey.Data` uses `OwnsOne(...).ToJson()`.
- **Polymorphic references without FKs**: `Reaction.IdentifierId`/`Contribution.IdentifierId`/`SiteMap.IdentifierId`/`Transaction.IdentifierId` are plain `long?` columns whose meaning depends on a sibling `CategoryType`/`ItemType` enum column (e.g. a `Reaction` with `CategoryType = Post` points at a `Posts.Id`, one with `CategoryType = SchoolComment` points at `SchoolComments.Id`) — there is deliberately no FK constraint on these columns; verify the live schema before assuming otherwise.
- **`DeleteBehavior.NoAction` almost everywhere** on explicit `HasOne(...)` relationships, to avoid multiple-cascade-path errors SQL Server would otherwise reject; a handful of pure junction/dependent rows use `CASCADE` instead (e.g. `SchoolTags`, `PostTags`, `SchoolBoards`, `SubjectGrades`, `SubjectTopics`, `TicketReplies`, `Experiences`, `ApplicationUserPasskeys` — confirmed against the live DB's `sys.foreign_keys`).

## Applying / creating migrations

See `docs/database/migrations.md` for the full mechanics (naming, working directory, gotchas). Quick reference:

```bash
cd src
dotnet ef migrations add <Name> \
  --project Infrastructure/Infrastructure/GamaEdtech.Infrastructure.csproj \
  --startup-project Presentation/Api/GamaEdtech.Presentation.Api.csproj

dotnet ef database update \
  --project Infrastructure/Infrastructure/GamaEdtech.Infrastructure.csproj \
  --startup-project Presentation/Api/GamaEdtech.Presentation.Api.csproj
```

Migrations also apply **automatically on app startup** — `Host.RunInternalAsync` calls `context.Database.MigrateAsync()` before the host starts listening whenever the generic `Startup<TUser, TRole>` is used (`src/Core/Common/Hosting/Host.cs:63-83`). This means simply running the API against a behind-schema database brings it up to date; there is no separate "run migrations" deployment step tracked in CI (the migration-related steps in `.github/workflows/main_gamaedtechv2.yml` are commented out).

### The `ImportLocations` chunking gotcha

`20250203080130_ImportLocations.cs` (`src/Infrastructure/Infrastructure/Migrations/20250203080130_ImportLocations.cs`) seeds the `Locations` table from an embedded `.resx` resource (`Resource1.resx` / `Resource1.Designer.cs` in `src/Infrastructure/Infrastructure/`) containing ~156k single-line `INSERT` statements as one big SQL script.

Running that whole script as a single `migrationBuilder.Sql(...)` batch exhausts SQL Server's query-compile memory on constrained instances (**error 701**, "There is insufficient system memory in resource pool 'default' to run this query"). The fix (already applied) splits the script into **1000-line chunks**, each issued as its own `migrationBuilder.Sql(...)` call, all executed on the same migration connection so the leading `SET IDENTITY_INSERT ON` in the script stays in effect until its trailing `OFF` (`ImportLocations.cs:19-32`). Any future bulk-data-seeding migration that generates a giant raw SQL script should follow the same chunking pattern rather than emitting it as one statement.
