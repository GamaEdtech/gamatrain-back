# Payments & Points

Business logic: `src/Application/Service/TransactionService.cs`,
`PaymentService.cs`, `SubscriptionService.cs`, `SubscriptionQuotaService.cs`
(see `docs/business/subscriptions.md` for the latter). Contracts:
`src/Application/Interface/ITransactionService.cs`, `IPaymentService.cs`,
`ISubscriptionService.cs`, `ISubscriptionQuotaService.cs`. Entities:
`src/Domain/Entity/Transaction.cs`, `Payment.cs`, `SubscriptionPlan.cs`,
`VotingPower.cs`.

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
and its deletion path — see `docs/business/schools-directory.md`. Posts/comments
no longer go through `ContributionService`: `PostService.ConfirmPostAsync` /
`ConfirmPostCommentAsync` issue the same `SuccessfulContribution` reward
directly (see `docs/business/exams-and-content.md`, "Posts").

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
to the expected amount. From here the flow **branches on
`Payment.UserSubscriptionId`**:

- **Not set** (the ordinary top-up path, unchanged): the paid amount is
  converted to points via a currency-specific `ICurrencyConverterProvider`,
  the payment is marked `Paid`, and `TransactionService.IncreaseBalanceAsync`
  is called with `TransactionType.Payment` to credit the user — all within
  one `TransactionScope`.
- **Set** (a subscription purchase): no currency-conversion/points-credit
  step runs at all. The payment update is instead guarded on
  `Status == Pending` (rows-affected checked), and
  `ISubscriptionQuotaService.ActivateSubscriptionAsync` is called inside the
  same `TransactionScope` to flip the `UserSubscription` to `Active` and
  snapshot its quota rows. See `docs/business/subscriptions.md` for the full
  purchase lifecycle — subscription quota is deliberately never derived from
  the payment amount.

Both branches also set `Payment.BaseCurrencyAmount`/`ExchangeRate` — the
amount expressed in the base reporting currency (USD), locked at verify
time so Finance's daily/monthly totals are comparable across currencies and
don't drift if `GetPaymentsSummaryAsync` is re-viewed later after an
exchange-rate move. Today this is a pragmatic 1:1 peg for `USD`/`USDC`/`USDT`
only; `SOL`/`GET` are left `null` pending a real FX-rate source.

Supported currencies (`src/Domain/Enumeration/Currency.cs:9-21`): `SOL`,
`USDC`, `GET`, `USDT`, `USD`. Payment statuses
(`src/Domain/Enumeration/PaymentStatus.cs:9-15`): `Pending`, `Paid`,
`Failed`.

### `Payment.Kind` — what a payment represents (added 2026-09-16)

Before this, nothing on a `Payment` row said whether it was a fresh subscription purchase, a
renewal, a plan switch, or a points top-up — the admin/finance payments views could show totals but
never split them by kind, so a revenue chart couldn't distinguish "new business" from "recurring"
without a fragile heuristic (grouping by `UserSubscriptionId` and eyeballing `TransactionId`'s
prefix). `Payment.Kind` (nullable `PaymentKind` smart enum: `PointsTopUp`, `NewSubscription`,
`Renewal`, `PlanSwitch`) is now set explicitly at every insert site:

- `PaymentService.CreatePaymentAsync` — `NewSubscription` when `UserSubscriptionId` is set,
  `PointsTopUp` otherwise. Always a subscription's first period here:
  `PurchaseSubscriptionAsync` redirects an existing-subscription purchase to
  `SwitchSubscriptionPlanAsync` instead of reaching this method.
- `PaymentService.HandleInvoicePaidAsync` (an ordinary `invoice.paid` renewal webhook) — `Renewal`.
- `SubscriptionQuotaService.SyncExpirationFromGatewayAsync`'s "recovered cycle" insert (reconciling
  a missed renewal webhook) — also `Renewal`, same business event as the line above. Only for an invoice
  the gateway reports as paid (since 2026-10-05; an unpaid one used to be recorded as a `$0` renewal).
- `PaymentService.HandlePlanChangeInvoicePaidAsync` (an immediate plan/interval switch's prorated
  invoice) — `PlanSwitch`.

Exposed on both `GET admin/payments` (list + `/export`, filterable via a new `Kind` query param)
and `GET finance/payments/summary` (a `NewSubscriptionAmount`/`RenewalAmount`/`PlanSwitchAmount`/
`PointsTopUpAmount` breakdown per day, each with its own `*Count` — an independent pivot of the
same underlying rows as the existing Paid/Failed/Pending-by-`Status` breakdown, not a subset of it,
intended for a chart split by kind).

**"Per day" means the viewer's day (since 2026-10-10).** `GET finance/payments/summary` takes an
optional `TimeZone` query param (IANA id such as `Asia/Tehran`, or a Windows id; the admin dashboard
sends the browser's own zone). Both the `StartDate`/`EndDate` range and the per-day buckets are that
zone's calendar days, so a payment at 02:00 Tehran time is counted on that Tehran day rather than on
the previous UTC day, and an admin in another zone sees the same payments split by their own days.
Omitted or unknown ids fall back to UTC days (the old behaviour). `GetPaymentsSummaryAsync` aggregates
in SQL into 15-minute UTC buckets and folds those into local days in memory, which keeps a range that
spans a DST change exact. `GET admin/payments` (list/export) date filters are unchanged (UTC days).

**Nullable, and deliberately not fully backfilled**: the adding migration backfills only what's
provably correct from already-stored data - `PointsTopUp` (no `UserSubscriptionId`) and
`NewSubscription` (`TransactionId` still has the `cs_...` Checkout Session prefix, which - before
2026-09-15's reconciliation-duplicate-payment fix - was used *only* for a subscription's first
period). Existing renewal/switch rows can't be told apart from stored data alone (both used the
same Stripe invoice-id `TransactionId` scheme before that fix), so those are left `Kind = null`
rather than guessed - a finance chart covering a date range that includes pre-2026-09-16 data will
show `null`-Kind payments outside the four `*Amount` fields, so their sum can be smaller than
`PaidAmount` for older dates.

### Who confirms a payment (since 2026-10-18)

Until 2026-10-18 only the browser could complete a Stripe checkout: Stripe's `SuccessUrl` sends the customer to
`/payments/{id}/verify`, and that page's call to `POST payments/{id}/verify` was the only thing that moved the
payment from `Pending` to `Paid`. A customer who paid and then closed the tab, lost connection or never got the
page to load was charged and got nothing, and the row stayed `Pending` forever. Now three paths complete a
payment, all through the same `PaymentService.VerifyPaymentAsync`:

1. **The browser's verify redirect**, as before (the caller must own the payment).
2. **Stripe's `checkout.session.completed` webhook** (and `checkout.session.async_payment_succeeded` for a
   delayed payment method), mapped by `StripePaymentGatewayProvider.ParseWebhookEventAsync` to
   `CheckoutCompleted` with the payment id read back from the session's `ClientReferenceId` (both checkout
   kinds set it to the payment id). `checkout.session.expired`/`async_payment_failed` close the Pending
   payment as `Failed` (comment `CheckoutExpired`), so abandoned checkouts no longer pile up. An expired first-subscription
   checkout can then get a recovery email with a first-payment discount (since 2026-10-19,
   `docs/business/notifications.md`, "Checkout recovery").
3. **The hourly `CompletePendingPayments` job**, a safety net for a missed webhook (the endpoint always
   answers 200, so Stripe never retries): it re-checks Pending payments created between 3 days and 1 hour ago
   that have a gateway transaction id, at most 200 per run, and completes the paid ones. It never closes an
   unpaid one (`FailWhenUnpaid = false`); expiry is the webhook's job. Each completion is logged as a warning.

**They don't conflict.** The flip to `Paid` is a set-based update guarded on `Status == Pending`, on both the
subscription and the points top-up path, so only the first arrival grants anything (the top-up path had no
guard before, so two concurrent confirmations would have credited the points twice). A later arrival that finds
the payment already `Paid` asks the gateway again whether this transaction is the paid one for this payment, and
answers success without granting again, so the customer's verify page still shows success when the webhook
got there first.

**What verify now checks before the gateway** (all paths): the payment exists and, for the redirect, belongs to
the caller (another user's payment answers `PaymentNotFound`); if the gateway issued a transaction id at
checkout (Stripe's Checkout Session id, stored on `Payment.TransactionId`), the submitted id must be that one
(`PaymentMissMatch`, payment untouched); and `StripePaymentGatewayProvider.VerifyAsync` requires the session's
`ClientReferenceId` to be this payment's id. GamaTrain already tied the transaction to the payment through its
memo.

### Known risk: payment verification hardening

The payment verification path (`VerifyPaymentAsync`) has known concurrency
and authorization edge cases that need hardening before this path should be
considered production-hardened at scale. Specifics are intentionally not
published in this public-facing document; see the internal (untracked)
technical review for the full writeup and fix recommendation before relying
on this flow for high-value transactions, and treat it as a priority
hardening item rather than a stable, audited path.

Partly addressed 2026-10-18 (see "Who confirms a payment" above): verify now checks the payment's owner and that
the gateway transaction belongs to this payment, and the top-up path's grant is guarded against a concurrent
confirmation. Re-check the internal review before calling the path fully audited.

## Subscriptions

**RESOLVED (2026-07-10)**: the previous note here said no enrollment/purchase
flow existed and it was unclear how a plan actually grants anything to a
user. That's now built. Full design, entities, and the purchase → verify →
activate lifecycle are documented in `docs/business/subscriptions.md` —
see that file rather than duplicating it here. Short version: plans grant
fixed per-feature quotas (not points), purchased via the same
`Payment`/gateway flow described above (branch on `UserSubscriptionId`), and
`GameService.SpendPointsAsync` tries subscription quota before falling back
to points as described above.

**Native recurring billing added (2026-08-10, Stripe only)**: a Stripe subscription purchase now
auto-renews via a real Stripe Subscription object instead of staying one-time — a new
`POST payments/webhooks/{gateway}` (`[AllowAnonymous]`) receives Stripe's `invoice.paid`/
`customer.subscription.deleted` events, verified via `PaymentGateway:Stripe:WebhookSecret`. Each
successful renewal gets its own `Payment` row (same idempotency guard as everything else in this
file — `TransactionId`+`Gateway`'s unique index). As of 2026-08-14 a third event,
`invoice.payment_failed`, is also received — visibility only (stamps
`UserSubscription.LastPaymentFailedDate`, never a `Payment` row, never touches access) — see
"Dunning visibility" in `docs/business/subscriptions.md`. Full mechanics in
`docs/business/subscriptions.md`'s "Native recurring billing (Stripe)" section rather than
duplicated here; GamaTrain is unaffected/unchanged
to spending wallet points — see `docs/business/subscriptions.md#quota-consumption-and-the-points-fallback`.

## Content delivery & owner commissions (separate from points)

`ContentDeliveryService` (see `docs/business/content-delivery.md`) resolves downloadable content
from external sources (gama-api's legacy tests today) and, when the source hasn't already accounted
for payment, charges the downloader via the same quota-then-points path `GameService.SpendPointsAsync`
uses, then accrues a commission for the content's owner. That commission is a wholly separate ledger
(`ContentOwnerCommission`) — never a `Transaction`, never mixed into the points wallet.

Since 2026-10-21 the same ledger also receives the teacher invite program's share of subscription payments
(`Reason = ReferralSubscription`, one row per paid `NewSubscription`/`Renewal`/`PlanSwitch` payment of an invited user,
added by a 15-minute job, never by the payment code itself). See `docs/business/referrals.md`.

## Voting power (separate from points)

`VotingPower` (`src/Domain/Entity/VotingPower.cs:19-41`) is unrelated to the
points ledger: it stores a snapshot of a Solana wallet's token `Amount` for
a given governance `ProposalId`/`TokenAccount` — i.e. DAO-style on-chain
vote weighting. `VotingPowerService.cs` only supports listing
(`GetVotingPowersAsync`) and bulk-importing (`BulkImportVotingPowersAsync`)
these snapshots, presumably from an external chain-indexing job.
