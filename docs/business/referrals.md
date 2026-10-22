# Teacher Invite Program (Referral Commission)

Added 2026-10-21. A teacher shares an invite link to any Gamatrain page. Users who sign up through it are linked to that
teacher for good, and the teacher earns a percent of every subscription payment those users make: the first purchase,
every renewal and every paid plan switch. The money goes into the teacher's existing commission balance and is paid out
through the existing payout flow (`docs/business/content-delivery.md`, "Payouts").

Code: `ReferralService` (`src/Application/Service/ReferralService.cs`), rules in `ReferralTerms`
(`src/Core/Data/Dto/Referral/ReferralTerms.cs`), API on `ReferralController`.

## The promise: a teacher's percent is locked when they join

The admin sets one percent, `ReferralCommissionPercent` (default 15). A change only ever affects teachers who join
*after* it. A teacher who already joined keeps their own percent for life, on every user they invite, past and future.

1. **Offer.** When a user becomes a teacher, a `ReferralMemberships` row is created with `OfferPercent` = today's
   setting and `OfferExpirationDate` = today + `ReferralOfferValidDays` (default 30; `0` = never expires). A background
   job emails the offer (`ReferralOfferEmailTemplate`).
2. **Join.** The teacher joins from the dashboard (`POST referral/join`), usually through the email's link. Joining while
   the offer is open locks `OfferPercent`, even if the admin lowered the setting in between: that is the promise the
   email made. Joining after it expired locks the setting's value at that moment. Joining also creates the teacher's
   invite code (`ApplicationUser.ReferralId`).
3. **Earn.** Every accrual uses the locked `ReferralMemberships.Percent`, never the setting.

Teachers who were teachers before the program existed get no offer email. They see the current percent on the dashboard
and can join at it. Becoming a teacher means holding `Role.Teacher`. The offer is created at the four places a user gets
that role (`IdentityService`: new legacy user, first role on a later legacy login, the self-service group change, and an
admin adding the role in `UpdateUserPermissionsAsync`).
`CreateOfferAsync` does nothing for a non-teacher, a user who already has a row, or a 0% setting.

Only teachers can join. A user's inviter is set only from a code whose owner has joined, so a code generated through the
older `POST referral/generate` by a non-member links nobody.

## Invite links and attribution

An invite link is any page URL with `?invite=CODE`: the home page, a past paper, a blog post. (The token page's older `get-token?ref=CODE`
link shows the same code but links nobody; the program uses its own `invite` parameter.) The frontend captures the code in the browser only,
first click wins, kept 30 days while the visitor browses. It sends the code with every sign-up and sign-in request.

**Public pages are server-rendered and cached by Cloudflare**, so the code must never reach the rendered HTML or a
`Set-Cookie` from the server: a cached copy would hand one teacher's code to every visitor. The frontend reads
`?invite=` in a client-only plugin, stores it in `localStorage` + a first-party cookie written with `document.cookie`,
and removes it from the address bar. Every visitor gets the same HTML, so caching is unaffected. Pages keep their
canonical URL (without the parameter). Recommended: a Cloudflare cache rule that ignores `invite` in the cache key, so
shared links don't each create their own cached copy (correct either way, just a lower hit rate without it).

## Linking a new user to the inviter

The backend uses the code only when the request **creates** the account, so an existing user is never re-linked, and the
link never changes once set (`ApplicationUser.ReferredByUserId`/`ReferredDate`, update guarded on
`ReferredByUserId IS NULL`). Ignored without error: an unknown or empty code, a teacher who hasn't joined, a disabled
inviter, and a self-invite. Linking never fails the sign-up.

| Sign-up path | Endpoint | Where the account is created | How the inviter is set |
|---|---|---|---|
| Email register | `identities/register` | `IdentityService.RegisterAsync` | from `referralCode` in the request |
| Google | `identities/tokens/google` | `GoogleAuthenticationProvider` (reports `IsNewUser`) | from `referralCode`, only when the account was just created |
| Legacy OTP register | `legacy-auth/register` | nowhere: a passthrough to gama-api. The local account is created at the login that follows | `referralCode` is saved as a `PendingReferrals` row for that email/phone (kept 1 day); the login that creates the account uses it |
| Legacy Google | `legacy-auth/google` | `SyncLegacyAuthAsync` | a pending code for that identity, else `referralCode` in the request |
| Legacy password login | `legacy-auth/login` | `SyncLegacyAuthAsync` | **only** a pending code. A first local login is usually an old gama-api account, so a code in the request is not used |
| Admin creates a user | admin `identities` | `CreateUserAsync` | never |

**Known limit:** on legacy Google sign-in, a long-time gama-api user who signs in here for the first time also counts as
a new account (gama-api returns no creation date), so they can be linked to an invite link they clicked. Accepted.

## Accrual

A recurring job, `AccrueReferralCommissions` (every 15 minutes, `ReferralService.AccrueSubscriptionCommissionsAsync`),
adds one `ContentOwnerCommissions` row (`Reason = ReferralSubscription`) for each payment that is:

- `Status = Paid`, `Kind` one of `NewSubscription`, `Renewal`, `PlanSwitch` (never a points top-up);
- made by a user with an inviter, and created after they were linked;
- worth more than 0 USD (a free downgrade switch or a $0 invoice is skipped);
- not already accrued: `PaymentId` has a **unique** filtered index, so a payment can never pay commission twice, even if
  two runs overlap.

The row: `OwnerUserId` = teacher, `DownloaderUserId` = the invited user who paid, `PaymentId`, `CommissionPercent` = the
teacher's locked percent, `AmountUsd` = payment USD x percent (4 decimals), `Points = 0`, download-only columns null.
The USD value is the payment's `BaseCurrencyAmount`, else its `Amount` for USD/USDC/USDT (the reconciliation renewal
doesn't store a base amount). A payment in another currency without a base amount is skipped and logged; subscriptions
are only sold in USD today.

**Why a job and not a hook in the payment code:** a paid subscription payment is written in four places (checkout verify,
the renewal webhook, the plan-switch webhook, and reconciliation in `SubscriptionQuotaService`), and the checkout verify
runs inside a transaction scope where a second database connection must not open. The job covers all four, and any
future one, without touching that code. Trade-off: commission appears up to 15 minutes after the payment.

**Not handled:** refunds and chargebacks. There is no refund flow today, so referral commission (like download
commission) is not reversed.

## Offer email

A second job, `SendReferralOfferEmails` (every 10 minutes), sends each offer once. It claims the row
(`OfferEmailSentDate`) before sending, so overlapping runs can't double-send, and releases it if the send fails. An
offer for a teacher with no email, or one who already joined, is marked sent without emailing. The join link is the
`Referral:DashboardUrl` setting; without it no offer email is sent.

Subject: `ReferralOfferEmailSubject` (admin setting, default "Teachers are the heart of the Gamatrain community").
Keep money wording ("earn 15%") out of it: it is a spam signal. Template `ReferralOfferEmailTemplate` (admin settings) tokens:
`[FIRST_NAME]` ("there" when unknown), `[RECEIVER_NAME]` (full name), `[PERCENT]` (the offer's own percent, so the
email always matches what joining locks), `[JOIN_LINK]`, `[EXPIRY_DATE]` (`20 November 2026`, or "no end date"). The
default leads with what students get (free past papers, exams, study tools) and calls the share a thank-you, adding
"your students pay nothing extra": teachers are wary of earning from their own students, and "lifetime earnings"
wording reads like affiliate marketing. The admin form requires `[PERCENT]` and `[JOIN_LINK]`.

## Admin settings

| Setting | Default | Meaning |
|---|---|---|
| `ReferralCommissionPercent` | 15 | Offered/locked by teachers from now on (0-100). 0 closes the program to new members; joined teachers keep earning |
| `ReferralOfferValidDays` | 30 | How long an emailed offer's percent can be locked (0-365; 0 = never expires) |
| `ReferralOfferEmailSubject` | Teachers are the heart of the Gamatrain community | The offer email subject (max 150 chars) |
| `ReferralOfferEmailTemplate` | see `ApplicationSettingsDto` | The offer email body |

## API

`ReferralController` (`api/v1/referral`, signed-in user):

- `GET referral`: status: `isTeacher`, `joined`, `percent` (locked), `joinPercent` (what joining now locks), the offer
  (`offerPercent`, `offerExpirationDate`, `offerOpen`), `referralCode`, `invitedCount`, `subscribedCount` (invited users
  with a paid subscription payment), `earnedUsd` (lifetime referral commission, already part of `commissions/balance`).
- `POST referral/join`: teachers only; returns the new status. Joining again changes nothing.
- Admins see the whole program on the admin dashboard (`GET admin/dashboard/referrals`, `docs/business/admin-dashboard.md`),
  including a possible self-invite flag. To support it, `GET referral` and `POST referral/join` record the teacher's
  network and the sign-up records the invited user's (hashed, `ReferralIpSightings`).
- `GET referral/invited`: the caller's invited users, newest first: a short name only (`Sara K.`, never email or phone),
  join date, whether they're subscribed now, payment count and earnings.

Earnings also appear in `GET commissions` (filter `reason=ReferralSubscription`), `commissions/statistics` and
`commissions/balance`, next to download commission.

## Frontend (gamatrain-front)

- `plugins/invite-capture.client.ts` + `utils/inviteCode.ts`: browser-only capture of `?invite=` (first code wins, 30
  days, `localStorage` + first-party cookie), removed from the address bar after load. `useAuth` sends it with the
  legacy register and Google sign-in, and clears it on any successful sign-in.
- `pages/user/invite` (Invite & Earn, teachers only in the menu): the offer and join button; once joined, the locked rate,
  totals, a link builder (home, past papers, blog, or any pasted page; copy, share, QR code) and the invited users.
- Admin settings: the three settings above. The commission pages label referral rows "Invite earning".
