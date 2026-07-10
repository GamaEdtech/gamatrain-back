# GamaEdtech Business Domain Overview

GamaEdtech (Gamatrain) is an ed-tech platform combining a crowdsourced school
directory, learning content, gamified points, and crypto-based monetization.
The backend is a layered ASP.NET Core API (`src/Application/Service/`
implements the business logic behind `src/Application/Interface/`; entities
live in `src/Domain/Entity/`; business vocabulary is encoded as smart enums
in `src/Domain/Enumeration/`). This folder documents the *business* domains;
see `ANALYZE.md` (repo root, untracked local analysis doc — may not be present in every
checkout) for a technical/architecture deep-dive and known code-quality issues.

## School Directory & Reviews

The core product: a searchable directory of schools (`School` entity,
`SchoolService.cs`, ~2076 lines) with geospatial search (NetTopologySuite
`Point`/`geography` distance ordering), multi-dimension parent reviews (8
sub-ratings averaged per `SchoolComment`), photos, tags, and curriculum-board
associations. Most public edits (new schools, comments, images, issue
reports) flow through a generic **Contribution** approve/reject workflow
rather than direct writes, with an optional per-user auto-confirm permission.
A separate Hangfire job computes an internal ranking `RankScore` used only for
country/state/city ordering and is not exposed publicly — this is a
deliberately distinct concept from the public 0-5 `Rate` (real review
average). See `docs/business/school-scoring-analysis.md` for the full history
(the two were previously conflated; now fixed). Full detail:
`docs/business/schools-directory.md`.

## Content / Blog

A blog subsystem (`Post`, `PostComment`, `PostTag`, `Tag` entities,
`BlogService.cs`) with the same Contribution-based moderation pattern as
schools: user-submitted posts/comments are held for admin approval (or
auto-confirmed via a claim/setting) before becoming visible, with captcha
enforcement at the controller layer for comments. Detail:
`docs/business/exams-and-content.md`.

## Exams & Curriculum

A curriculum hierarchy (`Board` → `Grade` and `Subject` in a many-to-many
relationship, `Subject` ↔ `Topic` also many-to-many) backs a `Question`
bank and two distinct submission-tracking mechanisms: `TestSubmission`
(one graded answer to one practice question) and `ExamSubmission` (one
aggregate valid/invalid/no-answer tally per user per formal exam, sourced
from an external "Core"/Game exam system). Detail:
`docs/business/exams-and-content.md`.

## Gamification & Points

A ledger-style points system (`Transaction` entity, `TransactionService.cs`)
where every balance change is an immutable, linked-list transaction
(`PreviousTransactionId` → `CurrentBalance`), protected from concurrent
double-writes by a unique constraint plus a single retry. Points are earned
via successful contributions, correct exam/test answers, payments, and
admin adjustments (`TransactionType` enum), and spent on downloads. A
separate `VotingPower` concept tracks on-chain governance token weight per
wallet/proposal (DAO-style voting), unrelated to the points ledger.
`Reaction` (like/dislike) and `ContributionService` round out the
engagement/reward loop. Detail: `docs/business/payments-and-points.md`.

## Payments & Subscriptions

Users can top up their points balance by paying via a custom "GamaTrain"
Solana on-chain gateway (or Stripe) — `PaymentService.cs` creates a pending
`Payment` and later verifies it against the chain (memo/destination/
currency/amount checks) before crediting points. `SubscriptionPlan` defines
purchasable plans (price, currency, billing interval, points granted,
optional geographic coverage `Polygon`) but no enrollment/purchase-fulfillment
flow was found tying a plan to a user. Payment verification has known
hardening needs — see `docs/business/payments-and-points.md` (details kept
in an internal, non-public review document rather than this repo).

## Support

A ticket system (`Ticket`/`TicketReply`, `TicketService.cs`) supporting both
authenticated and anonymous submissions (with captcha), inbound-email
ingestion that matches replies to tickets by subject-line ticket number, and
simple read/unread tracking in place of a formal status/priority workflow.
Detail: `docs/business/support-and-social.md`.

## Identity & Social

ASP.NET Core Identity-based registration/login (`IdentityService.cs`, ~1937
lines) with a custom opaque bearer-token scheme layered on top of Identity's
cookie auth, flag-based `Role`s (Admin/Teacher/Student/Advisor/Finance), and
a lightweight social layer: `Experience` (a user's school affiliation
history), `Connection` (follow/unfollow with two-way confirmation),
`Message` (direct messages), and `LoginHistory` (per-login audit trail).
Detail: `docs/business/identity-and-access.md` and
`docs/business/support-and-social.md`.
