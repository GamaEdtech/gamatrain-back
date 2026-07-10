# Payments & Points

Business logic: `src/Application/Service/TransactionService.cs`,
`PaymentService.cs`, `SubscriptionService.cs`. Contracts:
`src/Application/Interface/ITransactionService.cs`, `IPaymentService.cs`,
`ISubscriptionService.cs`. Entities: `src/Domain/Entity/Transaction.cs`,
`Payment.cs`, `SubscriptionPlan.cs`, `VotingPower.cs`.

## The points ledger

Every balance change is an append-only, linked-list ledger entry, not a
mutable balance field. `TransactionService.CreateTransactionInternalAsync`
(`TransactionService.cs:226-284`):

1. Looks up the user's latest transaction (`GetCurrentBalanceInternalAsync`,
   `:210-224`, the most recent row by `Id` for that user) to read the prior
   `CurrentBalance`.
2. Computes the new balance as `previousBalance + factor`, where `factor`
   is the transaction's `Points` (negated for debits) (`:251-252, 258`).
3. Writes the new `Transaction` row with `PreviousTransactionId` pointing at
   the prior row, and denormalizes the same value onto
   `ApplicationUser.CurrentBalance` via `ExecuteUpdateAsync` (`:268`) — all
   inside one `TransactionScope` (`uow.CreateTransactionScope()`, `:248`,
   completed at `:270`).

**Concurrency control**: `Transaction.PreviousTransactionId` carries a
unique index (`Transaction.cs:59`), so two concurrent writers that both
read the same "previous" transaction and both try to insert a new row
pointing at it will collide — one throws `UniqueConstraintException`. That
case is caught (`TransactionService.cs:274-277`, returns
`OperationResult.Duplicate`) and the caller retries **once**
(`:232-238`). This is a deliberate optimistic-concurrency design (see
`ANALYZE.md` §4.4 — "solid idea") but has two documented edge cases in
`ANALYZE.md` item B3: only one retry (real contention under load can still
surface a user-facing "Duplicate Data" error), and a retry that happens
inside an *ambient* `TransactionScope` (e.g. when called from
`PaymentService.VerifyPaymentAsync`) cannot actually succeed because the
outer scope is already doomed.

Public surface (`ITransactionService.cs:14-19`): `IncreaseBalanceAsync` /
`DecreaseBalanceAsync` (ledger writes, take a `TransactionType`),
`GetCurrentBalanceAsync`, `GetTransactionsAsync` / `GetTransactionAsync`
(history), `GetStatisticsAsync` (debit/credit aggregates by day/month).

`TransactionType` (`src/Domain/Enumeration/TransactionType.cs:9-45`) is the
full vocabulary of why points move: `EasterEgg`, `CorrectTestTimeSubmission`,
`CorrectExamSubmission`, `IncorrectTestTimeSubmission`,
`IncorrectExamSubmission`, `Payment` (top-up), `AdminIncreaseBalance` /
`AdminDecreaseBalance`, `SuccessfulContribution` / `DeleteContribution`
(reversal), `DownloadTest` / `DownloadPastPaper` (spends). Contribution
rewards/reversals are issued by `ContributionService.ConfirmContributionAsync`
and its deletion path — see `docs/business/schools-directory.md`.

## Payments (Solana / GamaTrain gateway)

`PaymentService.CreatePaymentAsync` (`PaymentService.cs:68-126`) creates a
`Payment` row with `Status = Pending` and calls the configured
`IPaymentGatewayProvider` (resolved by `PaymentGateway` enum — `GamaTrain`
or `Stripe` — via a factory) to initiate the payment. For the GamaTrain
gateway, `CreateAsync` is effectively a no-op stub: users pay directly to a
known Solana wallet off-band, so there's no server-initiated checkout step.

`VerifyPaymentAsync` (`PaymentService.cs:128-209`) is the credit path:
loads the payment, requires it to still be `Pending`, calls
`GamaTrainPaymentGatewayProvider.VerifyAsync`
(`src/Infrastructure/Infrastructure/Provider/PaymentGateway/GamaTrainPaymentGatewayProvider.cs:29-76`),
which fetches the on-chain transaction details and checks, in order: memo
equals the payment id, destination wallet equals the configured platform
wallet, currency matches, and transferred amount is greater than or equal
to the expected amount. On success, the paid amount is converted to points
via a currency-specific `ICurrencyConverterProvider`, the payment is marked
`Paid`, and `TransactionService.IncreaseBalanceAsync` is called with
`TransactionType.Payment` to credit the user — all within one
`TransactionScope`.

Supported currencies (`src/Domain/Enumeration/Currency.cs:9-21`): `SOL`,
`USDC`, `GET`, `USDT`, `USD`. Payment statuses
(`src/Domain/Enumeration/PaymentStatus.cs:9-15`): `Pending`, `Paid`,
`Failed`.

### Known risk: payment verification hardening

The payment verification path (`VerifyPaymentAsync`) has known concurrency
and authorization edge cases that need hardening before this path should be
considered production-hardened at scale. Specifics are intentionally not
published in this public-facing document; see the internal (untracked)
technical review for the full writeup and fix recommendation before relying
on this flow for high-value transactions, and treat it as a priority
hardening item rather than a stable, audited path.

## Subscriptions

`SubscriptionPlan` (`src/Domain/Entity/SubscriptionPlan.cs:20-56`) defines
a purchasable plan: `Title`, `Price`, `Currency`, `Point` (points granted),
`BillingInterval` (`Daily`/`Weekly`/`Monthly`/`Seasonally`/`Yearly`, each
carrying a day-count and an end-date calculator —
`src/Domain/Enumeration/BillingInterval.cs:9-21`), `IsActive`, `Highlight`
(featured flag), and `Polygon` (a geography region, suggesting
region-scoped plan availability). `SubscriptionService.cs` only exposes
plan-definition CRUD (`GetSubscriptionPlansAsync`, `GetSubscriptionPlanAsync`,
`ManageSubscriptionPlanAsync`, `RemoveSubscriptionPlanAsync`) — no
enrollment/purchase-fulfillment entity or flow that ties a specific user to
an active subscription was found in the reviewed service; how a plan
purchase actually grants its points to a user is not evidenced in this
service and may live elsewhere or be unimplemented.

## Voting power (separate from points)

`VotingPower` (`src/Domain/Entity/VotingPower.cs:19-41`) is unrelated to the
points ledger: it stores a snapshot of a Solana wallet's token `Amount` for
a given governance `ProposalId`/`TokenAccount` — i.e. DAO-style on-chain
vote weighting. `VotingPowerService.cs` only supports listing
(`GetVotingPowersAsync`) and bulk-importing (`BulkImportVotingPowersAsync`)
these snapshots, presumably from an external chain-indexing job.
