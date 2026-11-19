# Architecture Decision Records

Each record explains one decision: the situation, the options we considered, what we chose and what it costs us. Later decisions can supersede earlier ones; superseded records stay in the file.

Status values: **Accepted**, **Superseded**, **Proposed**.

---

## ADR-001: "Important" is defined by user rules over scored events

**Status:** Accepted

**Context.** The brief asks to notify users when "something important" happens, without saying what important means or who decides.

**Options.**
1. The system decides importance automatically (scoring or machine learning).
2. Each event carries a category and a severity (1–5), and each user's rule decides what is important for them.
3. Users maintain their own raw keyword lists with no categories or severity.

**Decision.** Option 2.

**Consequences.**
- Matching is deterministic and easy to test.
- Severity must be derived per source, which is imperfect (news has no real severity, see A7 in ASSUMPTIONS.md).
- We offer preset rules so users do not start from nothing.
- If the product owner actually wants automatic detection of major events, this decision will need to be revisited.

---

## ADR-002: Polling ingestion through source adapters

**Status:** Accepted

**Context.** Events must come from somewhere. Public feeds mostly offer pull access, not push.

**Options.**
1. Scheduled polling per source.
2. Streaming or webhooks from providers.
3. A third-party aggregation service.

**Decision.** Scheduled polling. Each source implements one adapter contract that returns normalized events. A **simulator** source implements the same contract.

**Consequences.**
- Alerts arrive with a delay of up to one poll interval. The README must say so.
- Sources can be added or replaced without touching matching or delivery.
- Because the simulator uses the real contract, demos exercise the real pipeline.
- Poll intervals and source health need to be visible to admins.

---

## ADR-003: Outbox pattern with a background dispatcher

**Status:** Accepted

**Context.** Sending email or Slack messages can fail or be slow. If sending happens inside the request or the matching step, failures can lose alerts or block the system.

**Options.**
1. Send synchronously when an event matches.
2. Save a notification record first (outbox), then let a background worker deliver it with retries.
3. Use an external message broker.

**Decision.** Option 2, running as a hosted background service in the same process.

**Consequences.**
- A notification exists in the database before any send attempt, so a crash does not lose it.
- Every attempt is recorded, which gives the admin view something real to show.
- Retries use backoff with a maximum number of attempts. After that, the status is `Failed` and an admin can retry manually.
- Delivery is at-least-once, so a rare duplicate send after a crash is possible. We accept this and document it.
- A broker could replace the worker later because the dispatcher sits behind an interface.

---

## ADR-004: Channel plugin contract and registry

**Status:** Accepted

**Context.** The brief says "flexible enough that we can add more channels later". That claim is only meaningful if adding a channel really is cheap.

**Options.**
1. A switch statement on channel type inside the dispatcher.
2. A common channel interface, with implementations discovered through a registry.

**Decision.** Option 2. A channel declares its type, validates its own configuration, renders a notification into its own format, and sends it, returning a clear result. The dispatcher only knows the interface.

**Consequences.**
- Adding a channel means one class and one registration. We will prove this by adding a generic webhook channel late in the project and checking that no core code changed.
- Channel configuration is stored in a generic form (type plus settings), so each channel owns its own validation.
- Per-channel secrets need consistent handling (masking, validation).

---

## ADR-005: Slack through per-user Incoming Webhooks

**Status:** Accepted

**Context.** Slack can be integrated through an installed OAuth app or through simple Incoming Webhooks.

**Options.**
1. A distributed Slack app with OAuth, channel selection and token storage.
2. Each user supplies an Incoming Webhook URL for their own channel.

**Decision.** Option 2.

**Consequences.**
- Much smaller scope and nothing to host for Slack to call back into.
- Setup is manual for the user.
- The webhook URL is a secret and user-provided, so it must be masked and restricted to Slack's webhook host over HTTPS to avoid server-side request forgery.
- We cannot read channel lists or support interactive messages.

---

## ADR-006: ASP.NET Core Web API, EF Core with SQLite, Angular frontend

**Status:** Accepted

**Context.** We need a backend with background work, a database and a typed web client, with limited time to set things up.

**Options.**
1. ASP.NET Core API, EF Core, Angular.
2. A Python backend with server-rendered pages.
3. A Node.js full-stack framework.

**Decision.** Option 1: ASP.NET Core Web API with hosted background services, EF Core on SQLite, and an Angular + TypeScript frontend using a client generated from the OpenAPI document.

**Consequences.**
- Strong typing on both sides, and a generated client reduces contract drift.
- SQLite means no database setup. Switching the EF Core provider is possible later but untested.
- A single process hosts the API and the background workers, which is simple and not scalable (acceptable under A15).
- Two toolchains (.NET and Node) must both be set up and documented.

---

## ADR-007: Minimal authentication

**Status:** Accepted

**Context.** Users and admins need different views, but identity management is not what this project is about.

**Options.**
1. Full identity provider with passwords, reset and roles.
2. A very simple sign-in by email for users, and a role or token for admins.
3. No authentication at all.

**Decision.** Option 2, with an explicit statement that it is not production-safe.

**Consequences.**
- Admin endpoints must still reject unauthenticated requests, and this is tested.
- Anyone who knows an email address can act as that user in the demo setup. Documented as a limitation.
- Easy to replace with a real provider later because the API depends on an identity abstraction, not on the sign-in details.

---

## ADR-008: De-duplication and cooldown

**Status:** Accepted (refined by ADR-009: uniqueness is per rule, event and channel)

**Context.** Feeds repeat items, polling can see the same item twice, and busy periods can produce bursts.

**Options.**
1. Rely on sources to be unique.
2. De-duplicate events by a stable key, enforce one notification per (rule, event), and add a per-rule cooldown.

**Decision.** Option 2.

**Consequences.**
- Events store a de-duplication key built from the source and the source's own identifier. Ingesting the same item twice does not create a second event.
- A database unique constraint on (rule, event) makes duplicates impossible even if the matcher runs twice.
- Cooldown suppresses noise but can hide a genuinely different event inside the cooldown window. We record suppressed matches so admins can see what was skipped.
- A source with unstable identifiers could still produce duplicates. Verified against real feed data (see ASSUMPTIONS.md section 7).

---

## ADR-009: One notification per rule, event and channel

**Status:** Accepted. Refines ADR-008.

**Context.** A rule can deliver to several channels, and each channel can succeed or fail independently. ADR-008 spoke of one notification per (rule, event), which cannot express "email sent, Slack failed".

**Options.**
1. One notification per (rule, event) with a separate delivery record per channel.
2. One notification per (rule, event, channel), each with its own status and attempts.

**Decision.** Option 2. The unique constraint is on (rule, event, channel).

**Consequences.**
- Status, retries and manual retry are per destination, which matches what admins and users actually want to see.
- Simpler model: no extra "delivery" entity between notification and attempt.
- A rule with three channels creates three rows per match. Fine at this scale.
- Cooldown is evaluated per rule, and a suppressed match creates a `Suppressed` row for each of the rule's channels.

---

## ADR-010: Rule filters as typed, versioned JSON

**Status:** Accepted

**Context.** Each category needs different filters (keywords for news, magnitude and region for disasters, tickers and thresholds for markets). We want one rule table, not three.

**Options.**
1. Separate tables per category.
2. One table with every possible filter as a nullable column.
3. Common columns (category, minimum severity, cooldown) plus a JSON document of category-specific filters, validated against a typed model and carrying a version number.

**Decision.** Option 3.

**Consequences.**
- New categories or filters need no schema migration.
- Validation moves into application code. Invalid JSON must never be saved, and matching tests cover every filter type.
- Filters cannot be queried efficiently in SQL. Matching loads enabled rules for the event's category and evaluates in memory, which is fine at this scale.
- The version number lets us read older filter shapes after changes.

---

## ADR-011: Matching runs right after ingestion, in the same transaction

**Status:** Accepted

**Context.** Events must reach rules without being lost or double-processed.

**Options.**
1. A separate matcher worker that scans for unprocessed events.
2. Match inside the ingestion step, creating events and notifications in one transaction.

**Decision.** Option 2.

**Consequences.**
- No "processed" flag on events and no window where an event exists but has not been matched.
- A slow matcher would slow ingestion. Acceptable because matching is in-memory logic over a small rule set.
- If the rule set ever becomes large, matching can move behind the same interface into its own worker.
- Rules created after an event arrived do not retroactively match it. This is intended. The preview endpoint covers "what would this have matched".

---

## ADR-012: Market events use price-band de-duplication

**Status:** Accepted (values to be tuned)

**Context.** A price changes constantly. A naive "alert when change exceeds X" would fire on every poll while the price stays above X.

**Options.**
1. Emit an event on every poll for every ticker and let rules filter.
2. Emit an event only when the absolute move reaches a floor, and de-duplicate by ticker, UTC day, direction and move band.

**Decision.** Option 2. The de-duplication key includes a band (for example every 2.5 percentage points), so a ticker can raise a new event when its move reaches the next band, but not on every poll.

**Consequences.**
- Rules stay simple: threshold filters apply to the event's recorded percentage change.
- Moves smaller than the floor never become events, so very sensitive rules cannot be satisfied. Documented.
- Band size and floor are configuration and need tuning against real price data.
- Daily rollover means a continuing move may alert again the next UTC day. Intended.

---

## ADR-013: Single dispatcher with leases and failure classification

**Status:** Accepted

**Context.** Several notifications may be due at once, a dispatcher may crash mid-send, and not all failures deserve a retry.

**Options.**
1. Send inline as notifications are created.
2. One dispatcher that claims due notifications with an atomic update and a lease, classifies failures as transient or permanent, and retries only transient ones with backoff.
3. Multiple dispatcher instances competing for work.

**Decision.** Option 2. Option 3 is possible later, because claiming is already atomic.

**Consequences.**
- A crashed dispatcher leaves notifications with an expired lease, which the next cycle reclaims. A crash after sending but before recording can cause a duplicate send (at-least-once delivery, as stated in ADR-003).
- Permanent failures such as a revoked webhook are not retried pointlessly.
- SQLite allows a single writer, so one dispatcher also avoids lock contention.
- Retry delays are configurable so tests and demos do not wait for an hour.

---

## ADR-014: Channel settings encrypted with Data Protection

**Status:** Accepted

**Context.** Channel settings contain secrets (Slack webhook URLs) and personal data (email addresses). They must not appear in logs, responses or database dumps in clear text.

**Options.**
1. Store settings as plain JSON.
2. Encrypt settings with ASP.NET Core Data Protection and mask values in all outputs.
3. An external secrets manager.

**Decision.** Option 2.

**Consequences.**
- Protects against casual exposure (database file, backups, logs). It does not protect against someone who has both the database and the Data Protection keys.
- Keys must persist across restarts, or stored settings become unreadable. The key location is part of the run instructions.
- Secrets can no longer be searched in the database, so lookups use ids, not values.
- An external secrets manager would be the production answer. Not in scope.
