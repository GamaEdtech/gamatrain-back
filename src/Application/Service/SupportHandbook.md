# GamaTrain Support Handbook

Reference for the AI reply assistant in Admin → Contact Us. It describes how the
GamaTrain website actually works so replies can give customers real steps.

Rules for using this handbook:
- Only give links listed in "Links". Always use the full URL.
- Use menu and button names exactly as written here (they match the website).
- Anything marked `TODO(confirm)` is not confirmed policy. Do not state it to the
  customer; say the team will check and follow up instead.
- The public FAQ page is partly outdated (it mentions PayPal "soon", an email
  activation link and an annual-only fee). Do not quote it; this handbook wins.

---

## Links (the only links that may be given to customers)

| Page | URL |
|---|---|
| Home | https://gamatrain.com |
| Dashboard | https://gamatrain.com/user |
| Subscription (plans, cancel, resume, usage, Usage History) | https://gamatrain.com/user/subscription |
| Activity History (Gem spending) | https://gamatrain.com/user/activity-history |
| Commission (creator earnings) | https://gamatrain.com/user/commission |
| Edit Profile | https://gamatrain.com/user/profile |
| Security (change password, delete account) | https://gamatrain.com/user/edit-pass |
| Choose role (Student / Teacher) | https://gamatrain.com/user/type |
| My Papers | https://gamatrain.com/user/paper |
| My Multimedia | https://gamatrain.com/user/multimedia |
| My Q & A | https://gamatrain.com/user/question |
| My Blogs | https://gamatrain.com/user/blogs |
| My Exams (exam maker) | https://gamatrain.com/user/exam |
| Exam results | https://gamatrain.com/exam/results |
| Past papers search | https://gamatrain.com/search?type=paper |
| Multimedia search | https://gamatrain.com/search?type=multimedia |
| Exams / QuizHub search | https://gamatrain.com/search?type=quizhub |
| Subject directory | https://gamatrain.com/subject-directory |
| School Finder | https://gamatrain.com/school |
| Add a school | https://gamatrain.com/school/add |
| Earn money (become a content creator) | https://gamatrain.com/earn-money |
| Contact us | https://gamatrain.com/contact-us |
| Terms | https://gamatrain.com/terms |
| GDPR / privacy | https://gamatrain.com/gdpr |

---

## Website navigation

**Top header:** Home, About us, FAQ, Services, and an **Add** button (shortcuts to add
a Worksheet, Predicted Paper, Study Guide, Topical Questions, School or Blog).

**User menu / dashboard sidebar** (after login, click the profile avatar → dashboard):
- **Dashboard**
- **Contents** → Paper, Multimedia, Q & A, Blogs
  (Paper and Multimedia are only available to users with the Teacher role)
- **Online Exam** → Results, Exam maker
- **Financial** → Subscription, Activity History, Commission
  (Sell Report is shown but not available yet)
- **Messages**: shown but not available yet. Support replies arrive by email.
- **Settings** → Edit Profile, Security
- **Logout**

---

## Account

### Sign up
1. Click **Login** in the header, then **Create account**.
2. Enter your email address and accept the terms and conditions.
3. A verification code is emailed to you; enter it on the next screen.
4. Choose a password.
5. Choose your role: **Student** or **Teacher**.

Users can also sign in with **Google** from the login window.
Accounts are tied to an email address. A person can create separate accounts
with different emails.

### Didn't receive the verification code
- Check the spam/junk folder.
- On the code screen, use **"Your email is incorrect? Recheck it."** to fix a typo.
- TODO(confirm): resend-code timing / limits.

### Log in
Click **Login**, enter email and password, or use the Google button.

### Forgot password
1. Click **Login** → **Forgot password?**
2. Enter your email; a code is sent to it.
3. Enter the code, then set and confirm a new password.

### Change password
Dashboard → **Settings** → **Security** → enter current password, new password,
repeat it, and click **Submit**.

### Student vs Teacher role
- The role is chosen right after the first login (page "Choose Role").
- Only **Teachers** can upload Papers and Multimedia.
- Students and teachers can both post Q & A and Blogs and create exams.
- TODO(confirm): whether and how a user can change their role later.

### Edit profile
Dashboard → **Settings** → **Edit Profile**.

### Delete account
1. Dashboard → **Settings** → **Security**.
2. Under **Request Account Deletion**, click **Delete Account**.
3. Confirm with your password.

There is a **7-day grace period**. During it, the Security page shows the time left
and a **Cancel deletion request** button that keeps the account.
After 7 days the account and related data are permanently deleted.

### Unsubscribe from emails
Use the unsubscribe link at the bottom of any GamaTrain email.

---

## Subscription (premium membership)

### What it is
Many resources are free. Premium files (marked with a price in **Gems**) need a
subscription. Each plan includes a Gem allowance per billing period for certain
feature groups, or unlimited use for some groups. Plan names, prices and
allowances are shown on the plan cards and can change. Do not quote prices.

Billing intervals that may be offered: Daily, Weekly, Monthly, Quarterly, Annual
(only the ones currently available are shown). Longer intervals show a
"Save X%" discount.

### How to buy
1. Go to https://gamatrain.com/user/subscription (Dashboard → **Financial** →
   **Subscription**). The plans modal also opens automatically when you try to
   download a premium file without enough allowance.
2. Pick a billing interval at the top, then click **Choose Plan** on a plan.
3. You are taken to **Stripe** checkout to pay by card.
4. After paying you return to GamaTrain and see "Payment Successful", then
   **Return To App** takes you back to where you were (for example the file you
   wanted to download).

Note: the success screen says "Your wallet has been charged successfully" even
for subscriptions. This wording is a known UI issue; the payment did activate
the subscription.

### Check your plan and usage
The Subscription page shows: Plan, Status (Active / Pending / Expired /
Cancelled), Billing interval, Amount paid, Start date, Expiry date, and a table
of each feature group's Limit, Used and Remaining. Gem spending paid from the
wallet (not the subscription) is in **Activity History**.

### See what used your allowance (Usage History)
On the Subscription page, the **Usage History** section lists every time the
subscription allowance was used, newest first, grouped by day (Today, Yesterday,
then dates):
- Each row shows what it was (for example "Past paper download"), the item it
  was used on with a link to it (for example "Paper #48213"), the Gems used, and
  when. Hover over the time to see the exact date.
- The item link opens the item's own page, where it can be downloaded again.
  A question paper, mark scheme, multimedia or exam already paid for downloads
  again for free. A past paper's extra files (Insert, Data booklet) are charged
  again each time.
- A green **+N Refunded** row means a download failed after the charge and the
  allowance was given back automatically. The customer does not need to do
  anything for it.
- The period menu at the top: **This billing period** (the default; the same
  window as Used/Remaining above), **Last 30 days**, **All time**.
- The chips under it show the total Gems and number of uses per feature for that
  period. Clicking a chip shows only that feature; clicking it again shows all.
- **Load more** at the bottom shows older rows.
If a customer disputes a charge to their allowance, ask them to check Usage
History first: it shows each item and time. Items are shown by number, not by
title.

### Change plan or billing interval
1. Subscription page → **Change Plan**.
2. Pick a plan or interval and click **Switch Plan** (or **Switch to <interval>**).
3. A confirmation shows what you'll be charged now, if anything.
- Some switches apply immediately; others take effect at the end of the current
  billing period (the confirmation message shows the date).
- Plans that would be a downgrade show **Not an Upgrade** and can't be selected.
- If cancellation is pending, you must **Resume** the subscription before switching.
- Your current plan shows **Current Plan**.

### Cancel
1. Subscription page → **Cancel subscription** → confirm.
2. You **keep full access until the expiry date**; after that it won't renew.
3. The page then shows "Ends <date> - won't renew".
Cancelling can be undone any time before the expiry date.

### Resume (undo a cancellation)
Subscription page → **Resume subscription** → confirm. It goes back to
auto-renewing on the expiry date at the same price.

### Refunds
TODO(confirm): refund policy. Never promise a refund. Say the request has been
passed to the team, who will review it and reply.

### Payment problems
- "Payment Failed" after checkout: no subscription was created; try again,
  or try another card.
- Charged but the subscription doesn't show: ask for the account email, payment
  date and amount (and a Stripe receipt if they have one), and escalate.
- TODO(confirm): accepted cards/currencies, invoices/receipts.

---

## Downloading files

### Where files are
Past papers, multimedia, exams, tutorials and the subject directory. A resource
page has buttons such as **Download Question Paper**, **Download Question Doc**,
**Download Mark Scheme** / **Download Answer Doc**, **Download PDF**,
**Download DOC**, and sometimes extra files (e.g. Insert, Data booklet).
Many files can be opened with **Full preview** before downloading.

### How downloading works
1. You must be logged in (clicking a download opens the login window otherwise).
2. **Free files** download immediately.
3. **Premium files** show a Gem price. With an active subscription that has
   allowance left, the Gems are deducted and the file downloads.
4. Without a subscription, or without enough allowance, the plans window opens
   ("Get Membership. Unlock Premium Downloads."). After subscribing or
   upgrading, the download continues.
5. When finished: "Your download is ready and saved on your device!"

### Download didn't save / didn't start
- In the download-complete window, click **Show file in new tab** and save it
  from there.
- Check the browser's downloads list and that pop-ups/downloads aren't blocked.
- Large files can take a while; a progress bar shows on the button.
- Error messages: "File not found" (ask for the page link so the team can fix
  it), "Too many requests" (wait and retry), "Server error" / "temporarily
  unavailable" (retry later, escalate if it persists).

### Gems spent but no file
Ask for the resource link and the time of the attempt, and escalate. Gem
spending is listed in **Activity History**.
TODO(confirm): whether Gems are refunded for failed downloads and if a
re-download costs Gems again.

---

## Exams (QuizHub)

- **Take an exam:** open an exam (or a paper with a quiz) and click
  **Begin Quiz**. Login required. Answer, then **Send answers**.
- **See results:** Dashboard → **Online Exam** → **Results**
  (https://gamatrain.com/exam/results), then open a result for details.
- **Create an exam:** Dashboard → **Online Exam** → **Exam maker**, or
  **Create Quiz** on a paper page. Steps: Header → Tests → Review.
- **Manage your exams:** https://gamatrain.com/user/exam.
- Teachers can submit unlimited exams.

---

## Uploading content and earning money

- **Upload papers / multimedia (Teachers only):** Dashboard → **Contents** →
  Paper or Multimedia → create. Formats include Word, PDF, PowerPoint and more.
- **Q & A and Blogs:** Dashboard → **Contents** → Q & A / Blogs.
- **Earn money:** creators earn when students buy their content. See
  https://gamatrain.com/earn-money.
- **Commission:** Dashboard → **Financial** → **Commission** shows earnings
  (USD) by week or month.
- TODO(confirm): revenue share percentage, payout method, minimum payout,
  content review/approval time.

---

## Schools

- **Find a school:** https://gamatrain.com/school.
- **Add a school:** **Add** button in the header → School, or
  https://gamatrain.com/school/add.
- TODO(confirm): how to claim or correct an existing school's information.

---

## Always escalate (never resolve in the reply)

Say the team will review and follow up, and ask for the details listed:
- Refund or chargeback requests → account email, payment date, amount.
- Charged but no subscription → account email, payment date, amount, receipt.
- Account hacked / login email changed → account email, last successful login.
- Copyright or content takedown → link to the content, proof of ownership.
- Creator payouts → account email, period in question.
- Legal, GDPR data requests → account email.

## Never
- Quote prices, discounts or Gem amounts not shown in the customer's own message.
- Promise refunds, credits, deadlines or new features.
- Give links that are not in the Links table.
- Ask the customer for their password or card details.
