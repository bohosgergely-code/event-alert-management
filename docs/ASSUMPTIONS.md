# Assumptions and Scope

This document records how the vague product brief was interpreted before any code was written. Each assumption says what we decided, what happens if it is wrong, and how we will find out. Decisions with real trade-offs also have a record in [DECISIONS.md](../DECISIONS.md).

## 1. The brief, restated

Users set up alerts. When "something important" happens in the world (breaking news, market movements, natural disasters), they are notified by email or Slack. New channels must be easy to add later. Admins get their own view.

Our working interpretation, in one sentence:

> A user defines **alert rules** over a stream of normalized **events** collected from public sources. When an event matches a rule, the system creates a **notification** and delivers it reliably through the user's chosen **channels**. Admins can see and operate the whole pipeline.

## 2. Glossary

| Term | Meaning |
|---|---|
| **Source** | An adapter that fetches raw data from the outside world (for example the USGS earthquake feed). |
| **Event** | A normalized record produced by a source: category, severity, title, summary, link, location, time, de-duplication key. |
| **Alert rule** | A user's definition of what they care about: category, filters, minimum severity, channels, cooldown. |
| **Notification** | A message the system intends to send because an event matched a rule. Stored before sending (outbox). |
| **Channel** | A delivery method such as email or Slack. Each channel is a plugin. |
| **Delivery attempt** | One try to send a notification through a channel, with its result. |

## 3. Assumptions

Status values: **Open** (needs validating), **Decided** (backed by an ADR), **Accepted risk** (known weakness we live with).

| ID | Assumption | Status | If it is wrong | How we check |
|---|---|---|---|---|
| A1 | "Important" is defined by each user's rule, not by the system. | Decided (ADR-001) | We would need a scoring model, which is a much bigger project. | Review with the product owner; confirm that user-defined rules are what they mean. |
| A2 | Three categories are enough: **news**, **markets**, **natural disasters**. | Decided | New categories need new source adapters and filter types. | The category list lives in one place so it is cheap to extend. |
| A3 | Polling public sources every few minutes is fast enough. | Decided (ADR-002) | Truly "breaking" alerts could arrive late. | State the delay explicitly in the README; make poll interval configurable per source. |
| A4 | Public, key-free sources are acceptable for real data (USGS earthquakes, an RSS news feed, a public market price API). | Partly tested: true for USGS. News and market sources were not built (ADR-020). | Rate limits, terms of use or outages could block ingestion. | Capture real responses as fixtures; add source health to the admin view. |
| A5 | A **simulator source** and an admin "inject event" action are legitimate parts of the product (for demos and testing), not a hack. | Decided | Demos would depend on real-world events actually happening. | Keep them clearly labelled as simulated in the UI and in event data. |
| A6 | **Severity is a number from 1 to 5**, mapped per source. Earthquake severity is derived from magnitude. Market severity is derived from the size of the price move. | Open | Alerts could feel too noisy or too quiet. | Mapping tables live in configuration; tune against recorded real data. |
| A7 | **News has no reliable severity.** We cannot tell "breaking" from ordinary from an RSS feed. News events get a neutral default severity, and keyword filters are the main control for news rules. | Accepted risk | A minimum-severity filter is meaningless for news. | Say so in the UI and README rather than pretend otherwise. |
| A8 | A user can have several rules, and a rule can target several channels. | Decided | Simpler model, but less flexible. | Covered by the data model. |
| A9 | Alerts are **immediate** (one notification per matching event). Digests and summaries are out of scope. | Decided | Users may be flooded during busy periods. | Mitigated with per-rule cooldown (A10). |
| A10 | Each rule has a **cooldown** (minimum time between notifications for that rule), and an event is only ever notified once per rule. | Decided (ADR-008) | Duplicate or spammy notifications. | Automated tests with repeated and bursty events. |
| A11 | Email goes out via SMTP. Locally we use Mailpit so emails are visible without a real mail account. | Decided | None for this scope. | Check Mailpit after a real run. |
| A12 | Slack delivery uses a **per-user Incoming Webhook URL**, not a distributed Slack app. | Decided (ADR-005) | Users cannot choose channels or workspaces through OAuth; setup is more manual. | Documented as a limitation. |
| A13 | Webhook URLs are secrets. They are masked in the UI and logs, and only allowed to point at Slack's webhook host. | Decided | Leaked URLs let anyone post to a user's Slack channel; open URLs allow server-side request forgery. | Dedicated tests. |
| A14 | **Authentication is minimal.** Users identify themselves by email through a simple sign-in; admin access is protected by a role or token. No passwords, password reset or single sign-on. | Decided (ADR-007) | Not production-safe. | Clearly stated as a non-goal and tested so that admin endpoints reject anonymous calls. |
| A15 | One deployment, one database, one process. No horizontal scaling, no message broker. | Decided (ADR-006) | Would not survive large traffic. | Design keeps the dispatcher behind an interface so a queue could replace it. |
| A16 | All timestamps are stored and processed in **UTC**. Display conversion is a UI concern. | Decided | Time-based bugs (cooldown, polling) around daylight saving. | A test around a daylight-saving boundary. |
| A17 | Message content is English only. | Decided | Localization would be needed later. | Out of scope. |
| A18 | Event text from external sources is **untrusted input**. | Decided | Malicious headlines could inject markup into email or Slack. | Escaping tests with hostile titles. |

## 4. Questions we would ask the product owner

We cannot ask, so each question has a default (the assumption above) that we proceed with.

1. How many users and events per day should this handle? (Default: tens of users, hundreds of events.)
2. What delay between an event and the alert is acceptable? (Default: a few minutes.)
3. Should users get one message per event, or digests? (Default: one per event, with cooldown.)
4. Which regions and languages matter? (Default: global, English.)
5. Who counts as an admin, and what must they be able to do? (Default: see events, rules, delivery status; retry deliveries; disable channels; inject test events.)
6. Do we need an audit trail of who changed what? (Default: no, only delivery history.)
7. Are there legal or terms-of-service limits on the data sources we may use? (Default: public, free feeds only.)

## 5. Where this interpretation may be wrong (devil's advocate)

These are challenges to our own approach, kept here so they are not forgotten.

- **"We never decide what is important" may dodge the real question.** If the product owner expects the system to *recognize* a major event on its own, user rules will feel like a disappointment. Mitigation: ship sensible preset rules per category (for example "earthquakes of magnitude 6 or more") so users do not start from a blank page.
- **A rule model that is too rich will take too long to build and too long to explain.** Mitigation: start with category + keyword + minimum severity + (for markets) ticker and percentage threshold. Anything more needs a reason.
- **Simulated data can hide real-world problems.** The simulator makes demos reliable, but it never produces malformed or duplicated feeds. Mitigation: at least one real source with fixtures captured from live responses.
- **Polling real public feeds may be rate-limited or change format.** Mitigation: source health in the admin view, and fixture-based tests so a format change is noticed.
- **Slack webhooks per user are clumsy.** A real product would use an OAuth-based app. We accept this for scope and say so.
- **An admin view can easily become a CRUD screen.** Its real value is answering "why did this alert not arrive?" Mitigation: build the delivery list with status, error and retry first; everything else second.

## 6. Scope

### In scope

- Event ingestion from sources through a common adapter contract: simulator, USGS earthquakes (real), and and (planned, not built, ADR-020) an RSS news feed and a market price API.
- User-defined alert rules (category, filters, minimum severity, channels, cooldown).
- Matching events to rules, with de-duplication.
- Reliable delivery (outbox, retries with backoff) through **email** and **Slack**.
- A channel plugin contract, proven by a **third channel** (generic webhook) added without changing core code.
- A user interface to manage channels and rules.
- An admin interface: events, rules, users, deliveries with status, source health, channel enable/disable, manual retry, inject test event.
- Automated tests, including an integration test, and a documented review of AI-generated code.

### Out of scope (non-goals)

- Production-grade authentication, authorization and user management.
- Slack OAuth app distribution and workspace installation flows.
- Machine-learning or automatic importance scoring.
- Streaming or push-based ingestion.
- Digests, scheduling and quiet hours.
- Horizontal scaling, message brokers, high availability.
- Localization and accessibility polish beyond sensible defaults.
- A polished visual design.

## 7. Source-specific notes

Hypotheses are marked with the result of checking them against the real service. Mismatches and surprises are logged in [REVIEW_LOG.md](../REVIEW_LOG.md).

| Source | Hypothesis | Status |
|---|---|---|
| USGS earthquakes | A public GeoJSON summary feed exists without an API key; each feature has a stable id, magnitude, place, time and URL. | **Verified** with a real response (`fixtures/usgs/`, `evidence/004-usgs-and-slack-verification.md`). Ids are unique. `updated` differs from `time` for every feature, and 9 of 36 features had status `automatic` (not yet reviewed), so magnitude revisions are real. See R-010. |
| RSS news | A public RSS feed provides title, link, publication date and a unique id or link. | Never tested, source not built (ADR-020). |
| Market prices | A public API returns current price and 24h change for a small set of tickers without a key, within rate limits. | Never tested, source not built (ADR-020). |
| Slack | An Incoming Webhook accepts a JSON body with text and responds with a plain "ok" on success. | **Verified against Slack's official documentation** (success, error codes, rate limiting, escaping). Not yet verified by posting to a real webhook. See R-008. |
