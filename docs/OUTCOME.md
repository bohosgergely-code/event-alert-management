# Outcome

How the work went compared with the plan in [PLAN.md](PLAN.md). The plan is kept as it was written. This page is the honest comparison.

## In three sentences

A working alerting system was built end to end: users define rules, events from a real earthquake feed (and from an admin-controlled simulator) are matched, and alerts are delivered by email, Slack and a signed webhook, with an admin area to operate it, an Angular interface, and a trail of evidence for how each part was checked. Natural disasters are the only category with a live data source; news and markets were cut on purpose (ADR-020) and work with test events only. Four things remain unverified or unexplained and are listed below rather than hidden.

## The definition of done from the plan, checked

| Item (from PLAN.md, section 11) | State | Evidence |
|---|---|---|
| A user creates a rule, an event produces a **real email in Mailpit** and a **Slack message** (real or clearly labelled fake) | **Partly.** Email: a real message reached a real SMTP server (a small local one, because Mailpit could not be downloaded). Slack: verified against Slack's documentation and a stub, never posted to a live webhook. | `evidence/003-real-smtp-message.eml`, `evidence/004-usgs-and-slack-verification.md`, REVIEW_LOG R-008, R-009 |
| The same event ingested twice gives one notification | Done | tests; database unique index; `evidence/002-pipeline-mutation-checks.md` |
| A broken channel gives a `Failed` status in the admin view, and manual retry works | Done | browser journey step "Failed ... retry"; `evidence/e2e/` |
| Admin shows events, deliveries with status, source health, can inject an event, and is closed without credentials | Done | route-by-route security test; `evidence/005-api-verification.md` |
| A third channel exists and was added without changing core code | Done, with a diff as proof | `evidence/007-third-channel.md`, REVIEW_LOG R-029 |
| At least one real source (USGS) works with live data | Done | `fixtures/usgs/`, live test, REVIEW_LOG R-010 |
| Tests pass, including at least one integration test | Done | 383 backend tests (plus 5 that need real services and are skipped by default) and 69 frontend tests; a fresh clone reproduces them |
| REVIEW_LOG has at least eight entries, at least three AI mistakes corrected | Done | 34 entries, more than a dozen are mistakes in AI output that were found and fixed (summary table at the top of the log) |
| DECISIONS has at least five records; README states assumptions and non-goals | Done | 20 records; README and ASSUMPTIONS |
| Prompt history and evidence in the repository, a commit per milestone | **Partly.** Evidence and commits: done. Prompt history: a file per exchange with a summary of the prompt and result. The author's own wording is still to be added (see the note in `prompts/README.md`). | `prompts/`, `git log` |

## What changed from the plan, and why

| Plan | What happened | Why |
|---|---|---|
| Time-boxed schedule | Removed from the documents, work ran as long as needed | Requested; time is not what the work is judged on |
| Python and server-rendered pages | ASP.NET Core and Angular | The author asked for it (ADR-006, ADR-017) |
| Three real sources | One real source (USGS) plus the simulator | ADR-020 |
| Mailpit for email evidence | A small SMTP server (`tools/smtp-sink.py`) | Mailpit could not be downloaded in the build environment |
| An independent AI reviewer in a fresh context | **Not done.** Replaced by mutation checks, independent calculations, real end-to-end runs and reading every screenshot | See [AI-USAGE.md](../AI-USAGE.md), section "Where I did not follow my own plan" |
| Typed client generated from OpenAPI | Types generated, calls written by hand | A generated client was not needed for 24 endpoints, and the type generator cannot share the project's TypeScript version (ADR-017) |
| Email verification | Dropped at the author's request | Known limit, REVIEW_LOG R-019 |

## Open items (not hidden)

| Item | Why it matters | What would close it |
|---|---|---|
| Slack never posted to a live webhook (R-008) | Real behaviour may differ from the documentation | One real webhook and one "Send test" |
| Webhook never sent over real TLS to a public server (R-031) | The last hop of the third channel is untested | One public endpoint and one "Send test" |
| One unexplained connection error line at startup (R-026) | Cause unknown, no lost request ever observed | Log the underlying exception when it appears |
| Email addresses are not verified (R-019) | The system can be used to send mail to third parties | A confirmation link before a channel is enabled |
| No live news or market feeds (ADR-020) | The brief's first and second examples are not served by real data | Two source adapters |

## Numbers (from the repository, not estimates)

| | |
|---|---|
| Commits | 14 at the time of writing, one per milestone |
| Production code | about 2,800 lines C#, about 2,100 lines TypeScript (generated types excluded) |
| Test code | about 3,400 lines C# plus the frontend specs |
| API | 24 paths, 30 operations, described in `docs/openapi.json` |
| Decisions / review entries | 20 / 34 |
| Deliberate bugs injected to test the tests | 66, of which 64 were caught and 2 were harmless equivalents with reasons recorded |
| Real-browser journey | passed on every run after the last fix, three runs in a row before each milestone |

## Where to look

1. [README.md](../README.md): what it is, how to run it, limits.
2. [AI-USAGE.md](../AI-USAGE.md): how the AI was directed and where it was wrong.
3. [REVIEW_LOG.md](../REVIEW_LOG.md): the mistakes, checks and rejected output, with a summary table at the top.
4. [DECISIONS.md](../DECISIONS.md): 20 decisions with the alternatives that were rejected.
5. [evidence/](../evidence/): mutation tables, real messages, screenshots, the extensibility diff.
