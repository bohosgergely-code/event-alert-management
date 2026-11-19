# Independent review: what it found and what was done

The plan promised a second pair of eyes in a fresh context, and the first version of this repository did not have one ([AI-USAGE.md](../../AI-USAGE.md) said so). This is that review.

## How it was done

- **Three reviewers, each a fresh AI agent with no memory of the work,** each with its own focus: backend pipeline and data integrity; security of the API and the webhook; frontend, API contract, tests and fit with the brief.
- **Independence was enforced physically, not by asking.** Each reviewer got a plain copy of the code with the authors' own assessments removed (review log, AI-usage notes, outcome page, prompts, evidence) and the git history stripped, so commit messages could not hint either. They had the specification documents and the code, and could run anything locally.
- **They were told to prove, not suspect:** a reproduced finding was worth more than a guess, and unverified suspicions had to be listed separately.
- **Nothing was accepted on their word.** Every claim was reproduced with my own probe (backend, frontend) or a test written first that failed on the old code (security), before any fix. Fixes were then reverted one at a time to check that a test notices.

## Result in numbers

40 findings (including one result that was not a defect):

| Disposition | Count |
|---|---|
| Fixed | 19 |
| Partly fixed | 9 |
| Documented, not fixed | 11 |
| Held up | 1 |

**What stood up under attack:** the address protection (the security reviewer tried unusual notations, IPv6 forms, redirects, DNS tricks, ports, credentials and proxy settings and could not reach an internal address), ownership checks on every endpoint tried, role separation, error responses without internals, no unsafe bindings in the frontend.

**What my own methods had missed**, and the independent eyes found: severity-blind cooldown (a design flaw, not an implementation slip), alerts that ignored the current state of their rule and channel, the ingest race (19 of 20 rounds), a rate limit that parallel requests walk through, secrets in the log, unbounded input, a list that could show the wrong filter's answer, a checkbox that lied after a failure. Most are interactions between two things (a limit and concurrency, a feature and a deletion, a filter and a late answer), which is exactly where a single author's tests are thinnest.

**One more thing worth knowing:** the security reviewer's own baseline run showed one failing test in its copy. Twelve consecutive runs here all passed, so it is recorded as unexplained and not hidden.

## Every finding and what happened to it

IDs: B backend, F frontend, C contract, T tests, P product fit, S security.

| ID | Finding | My check | Disposition | What was done, or why not |
|---|---|---|---|---|
| B1 | A cooldown suppresses the newer, more severe event (earliest event gets the slot) | Reproduced: severity 1 sent, severity 5 suppressed | **Fixed** | Most severe first; a more severe event escalates through a running cooldown (ADR-021) |
| B2a | Disabling a rule does not stop alerts already queued | Reproduced | **Fixed** | Checked again at send time; suppressed with a visible reason |
| B2b | Deleting a rule erases its delivery history and queued alerts | Reproduced: 0 notifications, 0 attempts remain | **Documented, not fixed** | Needs the rule name and owner to be copied onto each alert. The delete confirmation now says it; ADR-003 amended |
| B2c | Deleting an unused channel erases its history; editing a rule does not touch queued alerts | Read in code | **Documented, not fixed** | Same cause as B2b |
| B3 | Switching a channel type off turns queued alerts into permanent failures | Reproduced | **Fixed** | Now suppressed, like at ingestion; an admin can send a suppressed alert on request |
| B4 | Overlapping ingests of the same source fail on the unique key and book a false source failure | Reproduced: 19 of 20 rounds | **Fixed** | Ingests serialised per source inside the process. Several instances would need the same from the database |
| B5 | Deleting a rule while its alert is being sent aborts the dispatcher batch | Reproduced: DbUpdateException | **Fixed** | One vanished alert no longer stops the rest |
| B6 | USGS: a revised magnitude never re-notifies; items that first appear in the feed more than 3 hours after they happened are dropped | Read in code | **Documented, not fixed** | Trade-off between old-news alerts and late items; stated in the README |
| B7 | No retention or cleanup anywhere; data grows without bound | Read in code (nothing to clean found) | **Documented, not fixed** | Deleting old events would also let a re-fed item notify again, so it needs a design |
| B8 | Manual retry repeats attempt numbers | Reproduced: #1 twice | **Fixed** | Numbers keep counting across retries |
| B9 | A crash during a send is not counted as an attempt, so it can be resent without a cap | Read in code | **Documented, not fixed** | Part of at-least-once delivery (ADR-003) |
| B10 | One serial dispatcher: a slow destination delays everyone's alerts | Read in code | **Partly fixed** | Fan-out per rule capped at 5 channels; no per-user fairness or parallel sending yet |
| B11 | Cooldown starts when the alert is queued, not when it is delivered | Read in code | **Documented, not fixed** | Low impact |
| B12 | Keyword matching is a substring match ("war" matches "software") | Read in code | **Documented, not fixed** | Kept on purpose ("flood" should match "flooding"); the rule editor now says so |
| B13 | Preview used the system clock instead of the injected one | Read in code | **Fixed** |  |
| B14 | Docs: ADR-008 and ARCHITECTURE overstate crash and race safety; ASSUMPTIONS A16 promises a daylight-saving test that did not exist | Checked | **Fixed** | Docs corrected; the test now exists |
| F1 | Lists can show the answer to an older filter; the refresh timer can undo a page change | Reproduced: dropdown "Sent", table showed the Failed row | **Fixed** | One tested loader (cancels superseded requests, remembers the requested page) |
| F2 | After a failed pause the checkbox shows the opposite of the truth | Reproduced | **Fixed** | A button, disabled while the request runs |
| F3 | The rule preview stays after the form changes, with the new category's noun | Reproduced (reviewer), confirmed in code | **Fixed** | Hidden until previewed again |
| F4 | The admin cannot see users or rules although the API supports it | Confirmed by search | **Fixed** | Users and Rules screens |
| F5 | A user cannot tell whether a rule works (last alert unused, no warning for unusable channels) | Confirmed by search | **Fixed** | Last alert column and a warning when no channel can reach anyone. The owner still cannot retry a failed alert |
| F6 | A rejected token silently logs the user out and loses unsaved work | Read in code | **Partly fixed** | The sign-in page now explains it. No return to the previous page, no warning before expiry, the form is still lost |
| F7 | Editing a rule that failed to load shows a blank, saveable form | Reproduced | **Fixed** |  |
| F8 | Channels cannot be edited; fixing an address means recreating the channel and re-attaching it | Read in code | **Documented, not fixed** | Pause, test and delete exist |
| F9 | An empty page after the list shrinks hides the pager; refresh keeps running when the API is down without saying so | Reproduced (pager) | **Partly fixed** | Pager fixed. No "last updated" indicator |
| F10 | Accessibility: page titles, table scopes, unnamed actions, buttons inside links, current page not announced, no skip link, focus lost after actions, locale dates | Read from templates | **Partly fixed** | Titles, scopes, named actions, no nested interactive elements, current page announced. Not done: skip link, focus management, field-level error links, timezone in dates |
| C1 | Filters are untyped; one list type is hand-written; keywords containing commas do not survive an edit; a future filter version would be dropped | Read in code | **Documented, not fixed** |  |
| T1 | Whole screens had no tests (history, rules, admin); guards were only tested in isolation; the contract check and browser journey are not in CI | Confirmed | **Partly fixed** | Screen and route tests added; the contract check is now a CI job. The browser journey needs three processes and stays a local check |
| P1 | README row says news and markets are "fully supported" | Confirmed | **Fixed** | Reworded: supported by the engine, no live feed (ADR-020) |
| S1 | The 10 second limit on test messages is bypassed by parallel requests: one user can make the server send many messages to a third party | Reproduced by the reviewer; my test failed on the old code | **Fixed** | One atomic claim plus a per-user budget across channels |
| S2 | Webhook and Slack addresses (secrets) are written to the server log on every send | Reproduced by a log-capturing test | **Fixed** | Request logging removed for those clients |
| S3 | One user can stall delivery for everyone (serial dispatcher, 20 stalling channels, 50 rules) | Read in code | **Partly fixed** | See B10 |
| S4 | No size limit on rule filters (18 MB rule accepted) or request bodies | Reproduced | **Fixed** | Limits on filters, per-rule channels and a 256 KB body limit |
| S5 | Sign-in throttle depends on the caller's address (shared bucket behind a proxy, one IPv6 range gives many); admin key strength not enforced; nothing else rate limited | Read in code | **Partly fixed** | Admin key must be 12+ characters. Proxy and IPv6 behaviour documented |
| S6 | Anonymous, unbounded user creation; large unauthenticated bodies | Reproduced (bodies) | **Partly fixed** | Bodies limited. User creation is bounded only by the sign-in rate limit |
| S7 | The first-line check on webhook addresses accepts odd spellings of internal addresses (the connection guard still stops them) | Reproduced: 5 spellings accepted | **Fixed** | Folding of unusual spellings and refusal of numeric host names. The connection guard stays decisive |
| S8 | Data Protection keys are stored unencrypted next to the database | Read in code | **Documented, not fixed** | ADR-014 amended |
| S9 | The launch profile forces the Development environment, which carries placeholder secrets | Read in code | **Documented, not fixed** | README says so |
| S10 | Untrusted text is interpreted by Slack (*bold*, :emoji:); links may carry a user name and password; masked Slack text shows the token tail | Read in code | **Partly fixed** | Links with credentials and the token tail fixed. Slack text formatting left as is |
| S11 | SSRF: could not be bypassed; ownership and role checks held on every endpoint tried | Not a defect | **Held up** | Recorded as a result |

## Where I did not follow the reviewers

- **Substring keyword matching (B12)** was kept: requiring whole words would stop "flood" from matching "flooding". The rule editor now tells the user.
- **Rule deletion keeping history (B2b)** and **retention (B7)** need a data-model decision (copying the rule name and owner onto each alert; what may be deleted without letting old items notify again). Doing that late and quickly would have risked more than it fixed, so they are written down as limits and the delete confirmation says what it does.
- **Parallel or fair delivery (B10, S3)** was reduced (fan-out cap) but not redesigned.
- **Slack text formatting (S10)** and several accessibility items (F10) were left, and are listed as such.

## Slips of mine while fixing

- A test I wrote for overlapping polls passed even without the fix, because the simulator hands its events to whichever poll asks first. It could not fail, so I deleted it rather than keep false comfort.
- One "revert" check did not compile and printed nothing; I almost counted silence as success. Redone properly.
- A first test I wrote ended with an assertion that cannot fail (the document exists). Rewritten to click, look and assert on the real text.
- A mail-server process from an earlier run was still holding its port, and for a while the browser journey failed for that reason, which looked like an application problem. The screenshot showed the real message.

Also see [01-backend-report-verification.md](01-backend-report-verification.md) for the reproductions taken before any fix.
