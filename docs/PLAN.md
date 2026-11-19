# EventAlertManagement – Analysis and Implementation Plan

## 1. Purpose of this document

This is the starting point for the project. It turns a deliberately vague product brief into a set of explicit decisions, a scoped deliverable list, an architecture, and a plan for how the work will be executed and validated with AI agents.

The brief, as received:

> "We want users to be able to set up alerts so they get notified when something important happens in the world — like breaking news, market movements, natural disasters, that kind of thing. Should work for both email and Slack. Make it flexible enough that we can add more channels later. We need an admin view too."

The assignment is judged on how the work was approached, not on the finished code. So this repository keeps the reasoning visible: plans, decisions, prompts, review findings and evidence sit next to the code.

---

## 2. Approach in five points

1. **The brief is short on decisions, not on features.** Most of the effort up front goes into making assumptions explicit, not into writing code.
2. **Build one thin slice that works end to end** (event in → match → notification out → visible in admin) before widening anything.
3. **Narrow "something important" to something testable.** Three categories (news, markets, natural disasters), one or two real data sources, plus a simulator for repeatable demos.
4. **Validation is a deliverable, not an afterthought.** Every AI output is checked, and the checks and rejections are logged.
5. **Record everything as it happens.** Prompts, decisions and findings are written down while working, not reconstructed afterwards.

---

## 3. From a vague brief to explicit decisions

| # | What the brief leaves open | Decision | Why |
|---|---|---|---|
| 1 | What counts as "important"? | The system does not judge importance itself. Each event has a **category** and a **severity (1–5)**. A user's **alert rule** decides what matters to them: category, filters and a minimum severity. | Objective importance is a machine-learning problem. User-defined rules are deterministic and testable. |
| 2 | Where does the data come from? | A **source adapter** interface. Real sources: USGS earthquake feed (no API key), a public RSS news feed, a public crypto/market price API. Plus a **simulator** source. | Real feeds can fail during a demo. The simulator and an admin "inject event" action keep everything demonstrable. |
| 3 | How are events detected? | Scheduled **polling** with de-duplication. Market moves are detected by threshold rules (for example "±5% in 24h"). No streaming, no ML. | Polling is enough for this scope and is simple to reason about. |
| 4 | What does "set up an alert" mean? | An **AlertRule**: category, filters (keywords, region, ticker + threshold), minimum severity, target channels, cooldown. | This is the core of the product, so it gets the most design attention. |
| 5 | How do email and Slack work? | Email over **SMTP** (Mailpit locally). Slack through a per-user **Incoming Webhook**. | Slack OAuth apps add a lot of setup for little extra value here. The trade-off is recorded as a decision. |
| 6 | "Add more channels later" | A **channel plugin contract** and a registry. Adding a channel means one new class and one registration line. | The claim is only credible if demonstrated, so a third channel (generic webhook) is added without touching the core. |
| 7 | What is the admin view for? | Operating the system: events, rules, users, **delivery status per notification**, source health, enabling/disabling channels, manual retry, injecting test events. | The most valuable admin question is "why did this not arrive?", not plain CRUD. |
| 8 | Authentication | Kept minimal: users are identified by email with a simple sign-in, admin is protected by a token or role. Real identity management is an explicit non-goal. | It should be a stated limitation, not a silent gap. |
| 9 | Duplicates and noise | Event de-duplication, a unique constraint on (rule, event), and per-rule cooldown. | News feeds repeat themselves, and AI-generated code usually forgets this. |
| 10 | Reliability | **Outbox pattern**: a notification is saved first, then a background worker delivers it with retries and backoff. | "We called send" is not the same as "it was delivered". |

**Open questions we would normally put to the product manager** (we proceed with the assumptions above and list them in the README): expected scale, acceptable delay, immediate alerts versus digests, multi-language needs, audit requirements.

**Explicit non-goals:** full authentication and authorization, Slack OAuth app distribution, machine-learning importance scoring, streaming ingestion, production-grade scaling, polished visual design.

---

## 4. Technology

| Layer | Choice | Notes |
|---|---|---|
| Backend | **ASP.NET Core Web API** (C#, current LTS) | Controllers or minimal APIs, OpenAPI/Swagger enabled |
| Persistence | **EF Core** with **SQLite** | Zero setup. The provider can be swapped for SQL Server later. |
| Background work | .NET `BackgroundService` (polling and dispatching) | Hosted in the API process, so there is one thing to run |
| Email | **MailKit** over SMTP, tested against **Mailpit** | Mailpit gives visible proof that emails really arrived |
| Slack | `HttpClient` to an Incoming Webhook | Webhook host is validated against an allowlist |
| Frontend | **Angular** with **TypeScript** | Standalone components, a typed API client generated from the OpenAPI spec |
| Tests | xUnit (backend), Jasmine/Karma or Jest (frontend) | At least one integration test with a real database |
| Local run | Docker Compose (API, Angular, Mailpit) | Optional but convenient |

Using a generated, typed client from the OpenAPI document keeps frontend and backend contracts in sync and removes a common source of AI-introduced mismatches.

---

## 5. Architecture

```
Sources (USGS, RSS news, Market, Simulator)
        │  scheduled polling
        ▼
  Normalizer ──► Event (with dedupe key) ──► Database
                                               │
                          Matcher: Event × AlertRule
                                               │  UNIQUE(rule, event)
                                               ▼
                               Notification (outbox, status)
                                               │
                               Dispatcher (retry with backoff)
                                               ▼
                    Channel registry:  Email | Slack | Webhook (added later)
                                               │
                             DeliveryAttempt log ──► Admin UI
```

**Core entities:** User, ChannelConfig (per user and channel type), AlertRule, Source, Event, Notification, DeliveryAttempt.

**Channel contract (conceptually):** a channel declares its type, validates its own configuration, renders a notification into its format, and sends it, returning a clear success or failure result. The dispatcher knows nothing about specific channels.

**Notification states:** `Pending → Sent`, or `Pending → Retrying → Failed` after the maximum attempts. A failed notification can be retried manually from the admin view.

**All timestamps are stored in UTC.**

---

## 6. Deliverables and execution order

The order is chosen so that something runs end to end as early as possible, and so that the riskiest assumptions are tested first.

| Phase | Output | Why this comes here |
|---|---|---|
| 1. Framing | Assumptions document, decision log (first ADRs), scope and non-goals | A wrong assumption is cheapest to fix before any code exists. AI is used as an interviewer and devil's advocate; humans decide. |
| 2. Design | Domain model, API outline, architecture diagram, ADRs for the main trade-offs | AI proposes two alternatives for key choices (for example synchronous send versus outbox); the choice and reasoning are recorded. |
| 3. Core slice | Event model, simulator source, matcher, outbox and dispatcher, Email and Slack channels | The minimum needed to prove the product idea. Done against the simulator first, then a real source. |
| 4. Real source | USGS earthquake adapter using fixtures captured from the real API | Proves the adapter contract against real, imperfect data. |
| 5. User and admin UI | Angular app: rule editor and channel setup for users; admin views for events, deliveries, source health, retry, inject event | Functional first, polished never. |
| 6. Extensibility proof | A third channel (generic webhook) added with no change to core code | Turns the "flexible" requirement into evidence. |
| 7. Validation pass | Tests, real email in Mailpit, Slack payload verification, independent AI review of the code | This is evaluated work, not a clean-up phase. |
| 8. Wrap-up | README, finalized review log, curated prompt history, evidence folder | The documentation must reflect what actually happened. |

**If scope has to shrink, cut in this order:** RSS news source, market source, third channel polish, admin filtering and paging, Docker Compose. **Never cut:** matcher, outbox, Email, Slack, delivery list in admin, tests, review log.

---

## 7. How AI agents are used

| Role | Job | Rule |
|---|---|---|
| Interviewer ("PM") | Asks questions about the brief and lists ambiguities | Humans decide; each AI suggestion is accepted or rejected with a reason |
| Architect | Proposes alternatives and trade-offs | First proposal is never accepted unchallenged; a "what breaks?" round follows |
| Implementer | Writes code one slice at a time | One prompt, one slice, one commit |
| Test author | Writes tests from the specification, not from the code | Otherwise tests simply confirm the bugs |
| Reviewer ("red team") | Reviews the diff in a fresh context with only the spec and the change | A fresh context reduces self-agreement; findings go to the review log |

---

## 8. Validation plan

This is where AI output is questioned. Known weak spots and the concrete checks for each:

| Risk area | Check |
|---|---|
| **Invented APIs or field names** (USGS, RSS, market API, Slack payload) | Call the real endpoints with `curl`; build fixtures from real responses, never from AI-written samples |
| **Slack payload format** | Send to a real test webhook if possible; otherwise a local fake receiver and a comparison to official docs, clearly labelled as such |
| **SSRF through user-supplied webhook URLs** | Allowlist the host (`hooks.slack.com`), require HTTPS, block internal addresses |
| **Injection** (malicious headline in an HTML email or Slack message) | Escaping tests using a hostile title |
| **Duplicate notifications** | Same event ingested twice and matcher run twice results in exactly one notification |
| **False "sent" status** | Email is visibly present in Mailpit; with a broken SMTP setup, status becomes `Failed` and retries run |
| **Time zones** | Everything in UTC; test around a daylight-saving boundary |
| **Admin protection** | Every admin endpoint returns 401/403 without credentials (tested) |
| **Secrets** (webhook URLs in DB and logs) | Masked in UI and logs; searched for in log output |
| **Mock-only tests** | At least one integration test with a real database; break a line of code on purpose and confirm a test fails |
| **"Extensible" in words only** | Third channel added; the diff shows core code untouched |
| **Retry storms and blocking I/O** | Backoff with a maximum attempt count; no blocking calls on request threads |

### Review log

Every check is recorded in `REVIEW_LOG.md` in this form:

`AI claim or output → check performed → result → decision (accept / reject / rewrite) → commit`

The goal is at least eight honest entries, including several where the AI was wrong and was corrected. If there are none, the review was not strict enough.

---

## 9. Repository layout

```
README.md                  How to run, assumptions, non-goals
DECISIONS.md               Architecture decision records
REVIEW_LOG.md              What AI got wrong and what was done about it
docs/
  PLAN.md                  This document
  ASSUMPTIONS.md
  ARCHITECTURE.md
prompts/                   One file per prompt: role, prompt, summarized answer, decision
evidence/                  Screenshots, curl output, test runs, Mailpit captures
src/
  EventAlertManagement.Api/       ASP.NET Core Web API
  EventAlertManagement.Core/      Domain, matching, channel contracts
  EventAlertManagement.Infrastructure/  EF Core, sources, channels
  EventAlertManagement.Web/       Angular application
tests/
  EventAlertManagement.Tests/
fixtures/                  Real captured source responses
```

**Commit milestones** (each with a meaningful message): framing and assumptions, architecture and ADRs, domain and simulator, matcher and outbox, email and Slack channels, USGS source, UI, tests, review fixes, final documentation.

---

## 10. Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Scope creep (auth, design polish, scoring) | Core slice is never finished | Non-goals are written down and visible in the README |
| External API or Slack unavailable during a demo | Demo fails | Fixtures, simulator, inject-event action, fake Slack receiver |
| No Slack workspace to test with | Slack path unproven | Create a free workspace and webhook; otherwise label the fake receiver honestly |
| AI produces polished code nobody understands | Cannot defend it | Every module must be explainable; simplify anything that is not |
| Documentation written after the fact | Loses credibility | Keep logs live; failed attempts stay in the history |
| Angular and .NET contracts drift | Runtime errors | Generate the typed client from OpenAPI; one integration check across the boundary |

---

## 11. Definition of done

- [ ] A user creates a rule (category, filter, channel); a simulated event produces a **real email visible in Mailpit** and a **Slack message** (real, or clearly labelled fake).
- [ ] The same event ingested twice produces exactly one notification.
- [ ] A broken channel results in a `Failed` status in the admin view, and manual retry works.
- [ ] Admin view shows events, deliveries with status, source health, and can inject an event; it is not reachable without credentials.
- [ ] A third channel exists and was added without changing core code.
- [ ] At least one real source (USGS) works with live data.
- [ ] Tests pass, including at least one integration test.
- [ ] `REVIEW_LOG.md` has at least eight entries, with at least three AI mistakes that were corrected.
- [ ] `DECISIONS.md` has at least five records; the README states assumptions and non-goals.
- [ ] Prompt history and evidence are in the repository, with a commit per milestone.
