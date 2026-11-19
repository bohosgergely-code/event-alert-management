# Outcome

How the work went compared with the plan in [PLAN.md](PLAN.md). The plan is kept as it was written. This page is the honest comparison.

## In three sentences

A working alerting system was built end to end: users define rules, events from a real earthquake feed (and from an admin-controlled simulator) are matched, and alerts are delivered by email, Slack and a signed webhook, with an admin area to operate it, an Angular interface, and a trail of evidence for how each part was checked. Natural disasters are the only category with a live data source; news and markets were cut on purpose (ADR-020) and work with test events only. An independent fresh-context review was then carried out and its findings fixed or recorded (see [the table](../evidence/independent-review/README.md)). Open items are listed below rather than hidden.

## The definition of done from the plan, checked

| Item (from PLAN.md, section 11) | State | Evidence |
|---|---|---|
| A user creates a rule, an event produces a **real email in Mailpit** and a **Slack message** (real or clearly labelled fake) | **Partly.** Email: a real message reached a real SMTP server (a small local one, because Mailpit could not be downloaded). Slack: verified against Slack's documentation and a stub, never posted to a live webhook. | `evidence/003-real-smtp-message.eml`, `evidence/004-usgs-and-slack-verification.md`, REVIEW_LOG R-008, R-009 |
| The same event ingested twice gives one notification | Done | tests; database unique index; `evidence/002-pipeline-mutation-checks.md` |
| A broken channel gives a `Failed` status in the admin view, and manual retry works | Done | browser journey step "Failed ... retry"; `evidence/e2e/` |
| Admin shows events, deliveries with status, source health, can inject an event, and is closed without credentials | Done | route-by-route security test; `evidence/005-api-verification.md` |
| A third channel exists and was added without changing core code | Done, with a diff as proof | `evidence/007-third-channel.md`, REVIEW_LOG R-029 |
| At least one real source (USGS) works with live data | Done | `fixtures/usgs/`, live test, REVIEW_LOG R-010 |
| Tests pass, including at least one integration test | Done | 418 backend tests (plus 5 that need real services and are skipped by default) and 94 frontend tests; a fresh clone reproduces them, and the GitHub Actions workflow passed on the final commits |
| REVIEW_LOG has at least eight entries, at least three AI mistakes corrected | Done | 38 entries, more than a dozen are mistakes in AI output that were found and fixed (summary table at the top of the log) |
| DECISIONS has at least five records; README states assumptions and non-goals | Done | 20 records; README and ASSUMPTIONS |
| Prompt history and evidence in the repository, a commit per milestone | Done | Every prompt the author sent, word for word (Hungarian ones with an English gloss), the prompts given to the three independent reviewers, and a summary of each result in `prompts/`. The AI's replies are summarised, not transcribed; the exported session is the complete record. Evidence in `evidence/`, commits in `git log` |

## What changed from the plan, and why

| Plan | What happened | Why |
|---|---|---|
| Time-boxed schedule | Removed from the documents, work ran as long as needed | Requested; time is not what the work is judged on |
| Python and server-rendered pages | ASP.NET Core and Angular | The author asked for it (ADR-006, ADR-017) |
| Three real sources | One real source (USGS) plus the simulator | ADR-020 |
| Mailpit for email evidence | A small SMTP server (`tools/smtp-sink.py`) | Mailpit could not be downloaded in the build environment |
| An independent AI reviewer in a fresh context | **Not done at first, done afterwards.** Three fresh reviewers on stripped copies of the code; every claim reproduced before a fix | [evidence/independent-review/](../evidence/independent-review/README.md), REVIEW_LOG R-035 to R-038 |
| Typed client generated from OpenAPI | Types generated, calls written by hand | A generated client was not needed for 24 endpoints, and the type generator cannot share the project's TypeScript version (ADR-017) |
| Email verification | Dropped at the author's request | Known limit, REVIEW_LOG R-019 |

## Open items (not hidden)

| Item | Why it matters | What would close it |
|---|---|---|
| Slack never posted to a live webhook (R-008) | Real behaviour may differ from the documentation | One real webhook and one "Send test" |
| Webhook never sent over real TLS to a public server (R-031) | The last hop of the third channel is untested | One public endpoint and one "Send test" |
| One unexplained connection error line at startup (R-026); one unexplained test failure in a reviewer's own run | Cause unknown, no lost request or repeatable failure ever observed | Log the underlying exception when it appears |
| Email addresses are not verified (R-019) | The system can be used to send mail to third parties (the test-message limit now holds under parallel requests) | A confirmation link before a channel is enabled |
| No live news or market feeds (ADR-020) | The brief's first and second examples are not served by real data | Two source adapters |
| **From the independent review, deliberately not fixed** | | |
| Deleting a rule deletes its alert history (ADR-003 amended) | A user can erase what an admin would want to see | Copy rule name and owner onto each alert |
| No retention or cleanup; data grows without bound | Disk and query cost over time; also constrains what may be deleted | A retention design (deleting old events would let re-fed items notify again) |
| One serial dispatcher, no per-user fairness (ADR-013) | A stalling destination delays everyone (fan-out per rule is capped at 5) | Parallel sending with per-destination limits |
| USGS: revised magnitudes are not re-notified, items first appearing more than 3 hours after they happened are dropped | A quake can be under-alerted | Alert on revision; decide how to treat late items |
| Keys protecting channel settings are stored unencrypted next to the database (ADR-014) | A backup of the app folder holds data and key together | An external key store |
| User creation is unbounded apart from the sign-in rate limit; the limit depends on the caller's address | Abuse and shared buckets behind a proxy | Cap on users; forwarded-header handling |
| Channels cannot be edited; the owner cannot retry a failed alert; no warning before a session expires; dates show no time zone | Usability gaps | Small features |
| Slack text from events is interpreted by Slack (`*bold*`, emoji) | Cosmetic injection | Escape or neutralise formatting characters |
| Accessibility not audited: no skip link, focus not managed after actions, errors not linked to fields | Screen-reader and keyboard users | An audit and fixes |

## Numbers (from the repository, not estimates)

| | |
|---|---|
| Commits | 22, one per milestone, and three for the independent review's fixes |
| Production code | about 2,900 lines C#, about 1,700 lines TypeScript (generated types and tests excluded) |
| Test code | about 3,800 lines C#, about 1,000 lines of frontend specs |
| API | 24 paths, 30 operations, described in `docs/openapi.json` |
| Decisions / review entries | 21 / 38 |
| Deliberate bugs or reverted fixes used to test the tests | 97, of which 94 were caught and 3 were harmless duplicates of other checks, with reasons recorded |
| Independent review | 40 findings: 19 fixed, 9 partly fixed, 11 documented as not fixed, 1 result with nothing wrong |
| Real-browser journey | passed three times in a row after each change to the interface |
| GitHub Actions | passed on every commit since it was added |

## Where to look

1. [README.md](../README.md): what it is, how to run it, limits.
2. [AI-USAGE.md](../AI-USAGE.md): how the AI was directed and where it was wrong.
3. [REVIEW_LOG.md](../REVIEW_LOG.md): the mistakes, checks and rejected output, with a summary table at the top.
4. [DECISIONS.md](../DECISIONS.md): 21 decisions with the alternatives that were rejected.
5. [evidence/](../evidence/): mutation tables, real messages, screenshots, the extensibility diff.
