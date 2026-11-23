# Admin Dashboard

Added 2026-10-22. The admin home page answers two questions at a glance: what is waiting for an admin, and how the
business is doing. Read-only. Code: `AdminDashboardService` (`src/Application/Service/AdminDashboardService.cs`),
`DashboardController` (`api/v1/admin/dashboard`, Admin role), date rules in `DashboardRange`
(`src/Core/Data/Dto/Dashboard/DashboardRange.cs`).

## Needs attention (`GET admin/dashboard/summary`)

One count per queue, with how many arrived in the last 7 days and when the oldest one arrived. The admin menu shows the
same counts as badges, so the frontend calls this from every admin page; that's why it is cached for one minute in the
distributed cache (key `AdminDashboardSummary`). A count can be up to a minute old.

| Key | What waits | Source |
|---|---|---|
| `ContactUs` | unread contact-us tickets | `Tickets.IsReadByAdmin = 0`, status not Closed/NotImportant; date = `LastActivityDate` |
| `SchoolImages` | new school images | `Contributions`, type SchoolImage, status Review |
| `SchoolContributions` | new schools / school edits | `Contributions`, type School, status Review |
| `SchoolIssues` | reported school issues | `Contributions`, type SchoolIssues, status Review |
| `SchoolImageRemovals` | image removal requests | `Contributions`, type RemoveSchoolImage, status Review |
| `SchoolComments` | school reviews | `Contributions`, type SchoolComment, status Review |
| `Posts` | posts | `Posts.Status = Review` |
| `PostComments` | post comments | `PostComments.Status = Review` |
| `Payouts` | commission payout requests | `CommissionPayouts` Pending or Approved |
| `PastPapers` (2026-11-23) | papers to review | `PastPapers` Pending or Edited, not deleted |
| `Exams` (2026-11-23) | exams to review or confirm | `Exams` status 0 (waiting) or 7 (published by its teacher, not confirmed), not deleted |
| `ExamQuestions` (2026-11-23) | questions to review | `ExamQuestions.Status = 0`; confirming an exam publishes its owner's own |
| `Tutorials` (2026-11-23) | tutorials to review | `Tutorials` Pending |

Contributions of type Post/PostComment are not counted: posts are moderated on the post itself since 2026-09-21
(`docs/business/exams-and-content.md`), and the old Draft rows of those types are leftovers.

**Delegated staff (2026-11-23).** `GET admin/dashboard/summary` filters itself by the caller (any signed-in user may call
it): every queue for an admin; for a user granted admin lists one by one, only the queues of those lists (`PastPapers` with
`Admin.PastPapers.GetPapers`, `Exams` with `Admin.Exams.GetExams`, `ExamQuestions` with
`Admin.ExamQuestions.GetQuestions`, `Tutorials` with `Admin.Tutorials.GetTutorials`, `Posts`/`PostComments` with the posts
lists); none for anyone else. The queues of endpoints never delegated (messages, school edits, payouts) are for admins only.
The admin menu's badges and the dashboard's Needs attention read it, so e.g. a paper reviewer sees how many papers wait.
`overview` and `referrals` stay Admin-only (`[Permission(Roles = [Admin])]` on each action; the controller no longer has a
class-level role).

**Performance.** Each count reads only waiting rows through a filtered index (migration `AddAdminDashboardIndexes`):
`IX_Tickets_Unread_LastActivityDate` (`IsReadByAdmin = 0`), `IX_Contributions_CategoryType_CreationDate`
(`Status = 1`), plus `IX_PostComments_Status`, and since 2026-11-23 the four content queues' (`AddReviewQueueIndexes`). Even without them the counts took tens of milliseconds on real data;
with them it's a few milliseconds, and the cache absorbs repeat loads. A counter column was considered and rejected: it
drifts the first time a code path forgets to update it, a count can't.

When this shipped most of the waiting items were old, which is why each item shows "new this week" and the oldest date
next to the total: a large but stale backlog shouldn't look like today's emergency.

## Overview (`GET admin/dashboard/overview?startDate&endDate`)

Whole UTC days, both dates inclusive; default the last 30 days; at most 366 days (a longer range keeps its end date);
reversed dates are swapped. Returns:

- sign-ups (`RegistrationDate`), split by the user's current Teacher/Student role;
- paid payments counted on the day they were confirmed (`VerifyDate`, else `CreationDate`), per `PaymentKind`, in USD
  (`ReferralTerms.PaymentUsd`: the locked base amount, else the amount for USD/USDC/USDT; anything else is counted in
  `PaymentsWithoutUsd` and left out of the totals);
- subscriptions: active now, active but cancelling at period end, new in the range (paid NewSubscription payments),
  expired in the range;
- exam participation (since 2026-11-18, from `ExamAttempts`, by the day an attempt started): attempts, finished
  attempts, distinct participants (a user here, or a gama-api user not linked yet), and the top 10 exams by attempts
  with their finished and participant counts;
- a daily series: sign-ups, paid payments, revenue, invite-program joins, invited sign-ups, exam attempts and finished
  exams.

The list of who took which exam is `GET admin/exam-participations` (see `docs/business/exams-and-content.md`,
"Exam participation (admin)").

The frontend's monthly payments chart pages through previous months with this same range parameter.

## Invite program report (`GET admin/dashboard/referrals?startDate&endDate`)

For the range: offers created, offers emailed (excluding offers created at the moment a teacher joined from the
dashboard), teachers joined, invited sign-ups, paying invitees (distinct invited users with a referral commission row),
commission earned, and sign-ups flagged as a possible self-invite. Plus the top 20 teachers **all-time** (by earned,
then invited) and the 50 most recent invited sign-ups in the range. See `docs/business/referrals.md`.

### Self-invite flag

An invited sign-up is flagged when it came from a network its teacher was also seen on. The network is the visitor's real
IP (Cloudflare's `CF-Connecting-IP`, `GetDeviceBindingIpAddress`), stored only as a SHA-256 hash in `ReferralIpSightings`
(one row per user + hash, with the last time seen):

- a teacher is recorded when they join or open Invite & Earn (`GET referral`) — that is when they copy their link;
- an invited user is recorded at sign-up, when their inviter is set.

Limits, on purpose: it only covers sign-ups from 2026-10-22 on (there was no IP data before: the frontend signs in through
the legacy bridge, which writes no `LoginHistories` rows, so the table had no rows for the last 30 days); a shared
network (a school or family Wi-Fi) also matches, so it's a hint to look closer, never an automatic rejection.
