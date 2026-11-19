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
- **Added after the independent review:** the dispatcher decides with the state of the rule and channel *at send time*. A rule switched off, a channel paused or a channel type switched off after an alert was queued gives a `Suppressed` alert (never sent, visible with its reason, and an administrator can send it on request), not a delivery failure.
- **Added after the independent review:** deleting a rule deletes its alerts, including the history of those already sent, because they belong to the rule. This was reproduced as a real loss of history. It is a known limit: keeping the history would mean copying the rule name and owner onto every alert. The delete confirmation says it.

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

**Status:** Accepted (refined by ADR-009: uniqueness is per rule, event and channel; and by ADR-021: how the cooldown treats severity)

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

**Status:** Designed, not implemented (no market source was built, see ADR-020). Values were never tuned against real prices.

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
- Claiming is atomic, but saving after a send does not check that this dispatcher still owns the alert, and the poller has no equivalent guard across processes. This is **not ready for several instances**; the per-source lock on ingestion (ADR-021 context) covers one process only.
- Sending is one at a time. A destination that stalls delays everyone behind it (up to the timeout, 10 seconds for webhooks). Fan-out per rule is capped at five channels, which bounds the damage but is not fairness.

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
- **Added after the independent review:** the keys that protect the settings are stored unencrypted in a folder next to the database (the start-up log warns about it). A backup of the application folder therefore contains both the data and the means to read it, so the claim "protects against backups" above holds only if the keys are kept apart. An empty key location silently uses throw-away keys, after which every stored channel reads as unreadable.

---

## ADR-015: Foreign resources look exactly like missing ones; everything is closed by default

**Status:** Accepted

**Context.** Every user endpoint takes ids from the caller. If the answer for "someone else's channel" differs from "no such channel", the API leaks which ids exist. A forgotten authorization attribute on a new endpoint would also leave it open.

**Options.**
1. Return 403 for resources that belong to someone else.
2. Return 404 for both foreign and missing resources, and make every lookup include the owner.
3. Rely on each developer remembering to check ownership.

**Decision.** Option 2, plus a fallback authorization policy that requires a signed-in user on every endpoint that does not say otherwise. Only the two sign-in endpoints are anonymous. Rule creation reports a foreign channel id with the same message as a missing one.

**Consequences.**
- Ownership is part of the query, not a check after loading, so a forgotten check shows up as "not found" rather than as a data leak.
- A test walks every route the application exposes and verifies anonymous callers get 401, normal users get 403 on `/api/admin/*`, and admin tokens cannot use user endpoints. A new endpoint is covered automatically.
- Debugging is slightly harder because "not found" can mean "not yours". Acceptable.

---

## ADR-016: Sign-in hardening that fits a minimal scheme

**Status:** Accepted. Implements ADR-007.

**Context.** ADR-007 accepts that knowing an email address is enough to act as that user. The admin key is the one real secret, so it needs protection that costs little.

**Decision.**
- Tokens are signed JWTs with issuer, audience and lifetime checked and only HMAC-SHA256 accepted (blocks the "alg none" trick).
- The application refuses to start without a signing key of at least 32 characters.
- The admin key is compared in constant time. An empty configured key disables admin sign-in entirely (it does not mean "no password").
- Sign-in endpoints are rate limited per client address (default 10 per minute).
- Errors that reach clients are generic. Details go to the server log only.

**Consequences.**
- The development settings file contains a placeholder signing key and admin key so the app runs out of the box. They are for local use only and must be replaced elsewhere. This is stated in the README.
- The rate limiter keeps its counters in memory, so it resets on restart and does not span several instances. Fine for one process.
- Anyone who knows (or guesses) an email address can sign in as that user. An email-verified or password-based flow is the production answer and remains a non-goal.

---

## ADR-017: Angular 22, pinned one week behind the newest release

**Status:** Accepted

**Context.** The scaffolding tool installs whatever version is newest. When this frontend was started, Angular 22.3.0 had been published the same day. A same-day release is the one most likely to contain mistakes that are fixed days later, and the scaffold's version ranges would have picked it up silently.

**Options.**
1. Accept the newest version the ranges resolve to.
2. Pin every Angular package to the same exact version from a few days earlier (22.2.7).
3. Use the previous major version.

**Decision.** Option 2, with exact versions in `package.json` and a committed lock file.

**Consequences.**
- Builds are reproducible and not at the mercy of a release made yesterday.
- All Angular packages must be changed together. Changing one at a time causes version conflicts, as happened while pinning.
- Angular 22 uses TypeScript 6 and Vitest. The OpenAPI type generator (`openapi-typescript` 7) supports only TypeScript 5, so it is run on demand through `npx` with its own TypeScript instead of being forced into the project's dependencies. The generated file is committed.
- Option 3 was rejected because the previous major is close to the end of its support period.

---

## ADR-018: Frontend structure and session handling

**Status:** Accepted

**Context.** The interface must stay small enough to finish and still be honest about security and failure.

**Decisions.**
- Standalone components, lazy-loaded routes, signals for anything that arrives asynchronously, template forms for input. No UI component library: a small hand-written stylesheet with light and dark colours.
- Types for requests and responses are generated from the API description (types only, no generated client). One `ApiService` holds every call.
- The signed-in session lives in `sessionStorage`. The token is attached by an interceptor only to calls under `/api/`, never to sign-in calls or other addresses. A 401 anywhere except sign-in ends the session. An expired session counts as signed out even before the server says so.
- Route guards send users and administrators to the pages that fit them. They are a convenience: the API enforces access (ADR-015).
- Screens that change by themselves (deliveries, history) refresh every few seconds.
- During development `/api` is proxied to the API, so no cross-origin setup is needed. For other setups the API allows the origins listed in `Cors:Origins`.

**Consequences.**
- A token in browser storage can be read by any script that runs on the page. There is no user-supplied HTML anywhere in the application (Angular escapes all bound text), which lowers the risk, but it is a known limit of this scope.
- No component library means the look is plain. Accessibility basics are covered (labels, focus outlines, roles for alerts, colour contrast in both colour schemes) but not audited.

---

## ADR-019: Webhook channel, and how a destination the user fully controls is made safe

**Status:** Accepted

**Context.** The third channel posts to a URL the user types. Unlike Slack, where the host is fixed to Slack's own, here any address is possible, so the server could be pointed at things only it can reach: cloud metadata services (`169.254.169.254`), internal admin pages, databases. This is server-side request forgery (SSRF). The channel also has to let the receiver check that a message really came from us.

**Options for the destination.**
1. Check the URL text only when it is saved.
2. Also resolve the host name when it is saved and check the address.
3. Check the address that is actually used at the moment the connection is made, and refuse if any resolved address is internal.

**Decision.** Option 3, plus the cheap early checks of option 1 for a clear message to the user.
- Only `https`, no user name or password in the URL, no internal-looking names (`localhost`, single-label names, `.local`, `.internal`, ...).
- Addresses written as numbers are looked at in their real form: .NET turns `2130706433`, `0x7f.0.0.1`, `127.1` and `0177.0.0.1` into `127.0.0.1` before the check sees them.
- The decisive check is in the connection step: resolve, refuse if **any** address is internal, then connect to the address that was checked. A name that answered with a public address when saved and an internal one later (DNS rebinding) is therefore still refused.
- IPv4 uses a block list of internal and reserved ranges. IPv6 uses an allow list (only global unicast), with the reserved blocks inside it removed. IPv4 hidden inside IPv6 (`::ffff:127.0.0.1`), 6to4 and NAT64 forms are refused.
- Redirects are never followed and connections are not kept longer than two minutes, so addresses are checked again.
- A blocked address is a permanent failure: no retries against an internal target.

**Options for proving origin.** Every message carries `X-EventAlert-Timestamp` and `X-EventAlert-Signature: sha256=<hex>`, where the hex is HMAC-SHA256 over `timestamp + "." + body` with a secret the user chooses (at least 16 characters, required). The timestamp lets the receiver reject old messages. A receiver recomputes the value and compares.

**Consequences.**
- Requests go straight from this application, not through a proxy, by default. A proxy would make the connection itself and the address check could no longer see the real destination. `Webhook:UseSystemProxy=true` is an explicit opt-in that says protection then depends on the proxy.
- The signing secret has to be readable by the server (it signs every message), so it is stored encrypted (ADR-014), not hashed.
- The secret and the path of the URL are never shown after saving (only the host).
- A destination that is reachable only from inside a company network cannot be used. That is the point, and also a limit.
- The live path over real TLS to a public server has not been exercised in this environment (see REVIEW_LOG R-031).
- **Added after the independent review:** the text check on the address is only the first line. An outside reviewer showed that several odd spellings of internal addresses passed it and were stopped only by the connection guard. They are now refused earlier too (unusual spellings folded, numeric host names refused), but the connection guard remains the decisive protection, and `Webhook:UseSystemProxy=true` switches it off.
- Request logging is removed for the webhook and Slack clients: the default would print the full address, which contains secrets, on every send.

---

## ADR-020: Live news and market feeds are not built; the categories still work end to end

**Status:** Accepted (a scope cut, made late and on purpose)

**Context.** The brief names breaking news, market movements and natural disasters as examples. Three kinds of real data were planned. By the time the core, API, interface, two channels and a third were finished and verified, only natural disasters (USGS) had a live source. The news and market feeds had not been touched, and the hosts for them had been unreachable from the build environment when first tried.

**Options.**
1. Build quick adapters for an RSS feed and a price API in the remaining effort.
2. Leave the two categories without live feeds, keep them fully supported by the rest of the system, and say so everywhere a user or reviewer would look.

**Decision.** Option 2. The brief calls these examples ("that kind of thing"). A hurried adapter would carry the least evidence per unit of effort: neither was checked against a real response, which is how the USGS adapter found its real design flaw (REVIEW_LOG R-010), and a market adapter would also need the price-band design (ADR-012) tuned against real prices.

**What still works for news and markets.** Rules, filters (keywords; ticker, size and direction), severity, cooldown, de-duplication, delivery, retries, admin views, and the simulator. An administrator can create news and market test events that go through the real pipeline (tests: keyword rules end to end; market rules on ticker, size and direction end to end).

**What the user sees.** The rule editor states, for News and Markets, that no live feed is connected and that such rules react to test events created by an administrator. Two ready-made examples (Bitcoin moves, breaking-news words) exist for these categories and carry the same caveat.

**Cost of adding a source later.** One class implementing the source contract, a captured real response as a fixture, and one registration, as the USGS adapter shows. ADR-012 describes the price-band approach for markets. It is a design, not implemented.

**Consequences.**
- The product, as delivered, watches the real world for natural disasters only. This is the largest gap against the brief's examples and is stated in the README first.
- Assumption A7 (news has no reliable severity) is untested against real news, because no real news was ever read.

---

## ADR-021: A cooldown must not hide the event that matters most

**Status:** Accepted. Refines ADR-008. Came out of the independent review.

**Context.** A rule's cooldown suppresses further alerts for a while to reduce noise. As first built, the first matching event took the slot, whatever its importance. An outside reviewer reproduced the consequence: with a one hour cooldown and two events arriving together, a minor event (severity 1) was sent and a catastrophe (severity 5) was suppressed. That defeats the point of the product.

**Options.**
1. Leave it and document it.
2. Process the most severe event first, and let an event that is more severe than the one that started the window through ("escalation").
3. Remove cooldown.

**Decision.** Option 2. Within a batch, events are matched most severe first (earliest first among equals). The severity that started the window is stored on the rule (`LastNotifiedSeverity`). An event with a higher severity is never held back; an equal or lower one is, as before.

**Consequences.**
- Several events arriving together produce one alert, for the most severe, not for the earliest.
- A user in a long cooldown still hears about something worse. A sequence 2, then 4, then 3 gives alerts for 2 and 4; the 3 is held back because the window is now at 4.
- A suppressed alert is still recorded, visible with its reason, and an administrator can send it on request.
- The cooldown window still starts when the alert is queued, not when it is delivered, and a rule edit does not reset it (both noted by the reviewer, left as they are).
- The data model changed (one column). The database is created, not migrated, so an existing local database file has to be deleted (the README says so).

