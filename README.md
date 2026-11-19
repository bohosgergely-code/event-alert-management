# EventAlertManagement

Users define alert rules. The system watches for events, matches them against those rules, and notifies people by **email, Slack or a signed webhook**. Administrators get a view to see and operate the whole pipeline.

It was built from a deliberately vague product brief (*"notify users when something important happens in the world... email and Slack... flexible enough to add more channels... an admin view"*) as a worked example of taking an ambiguous request to a verified system with AI agents. The reasoning is part of the deliverable.

## What it does today, and what it does not

| | |
|---|---|
| **Real events** | **Natural disasters only**, from the live USGS earthquake feed. |
| **News and markets** | The rule engine, matching, delivery and admin area handle them, but **no live feed is connected**: they react only to test events an administrator creates. The rule editor says so. See [ADR-020](DECISIONS.md). |
| **Channels** | Email (SMTP), Slack (incoming webhook), generic signed webhook. Adding a channel took three new files and one added line ([evidence](evidence/007-third-channel.md)). |
| **Users** | Rules (with ready-made examples and a "which recent events would match" preview), channels with a test button, a history of every alert and what happened to it. |
| **Admins** | Overview, every delivery with each attempt and a retry button, test events, source health with "check now", a switch to turn a channel type off for everyone. |

## Start here (for reviewers)

1. [docs/OUTCOME.md](docs/OUTCOME.md): the plan against what happened, the checklist, what is open.
2. [AI-USAGE.md](AI-USAGE.md): how the AI was directed, what it got wrong, where I still fall short of the plan.
3. [evidence/independent-review/](evidence/independent-review/README.md): an independent fresh-context review (three reviewers, 40 findings, what was fixed and what was not).
4. [REVIEW_LOG.md](REVIEW_LOG.md): 38 entries, summary table first.
5. [DECISIONS.md](DECISIONS.md): 21 decisions with the options that were rejected.
6. [docs/PLAN.md](docs/PLAN.md), [docs/ASSUMPTIONS.md](docs/ASSUMPTIONS.md), [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): the plan, the assumptions behind the brief, the design.
7. [evidence/](evidence/) (tables, real messages, screenshots) and [prompts/](prompts/) (one file per exchange).

## Run it

Requires the .NET 8 SDK and Node 22.

```bash
# API on http://127.0.0.1:5080 (Swagger at /swagger). The web interface expects this port.
dotnet run --project src/EventAlertManagement.Api

# Web interface (http://localhost:4200, proxies /api to the API on port 5080)
cd src/EventAlertManagement.Web && npm install && npm start
```

In the Development environment the API starts with **placeholder secrets** from `appsettings.Development.json` (admin key `dev-admin-key`). They are for local use only. Anywhere else the application refuses to start until `Auth:SigningKey` (32+ characters) is set, and admin sign-in stays off until `Auth:AdminKey` is set. Use environment variables or user secrets, never the repository.

Email goes to an SMTP server on `localhost:1025` by default. Run `python3 tools/smtp-sink.py 1025 /tmp/mail` to receive messages as files, or point it at Mailpit. The database file `eventalert.db` is created on first start; delete it after pulling changes that alter the data model.

Sign in as a user with any email address. The admin area is at `/admin/login`.

## Check it

```bash
bash tools/verify.sh        # everything below in one go
dotnet test                 # backend: 418 tests (5 more need real services and are skipped by default)
cd src/EventAlertManagement.Web && npm test    # frontend: 94 tests
```

A scripted journey in a real browser against the real stack is in [e2e/](e2e/). Opt-in checks against real services: `EAM_SMTP_TEST_PORT=1025` (real SMTP server) and `EAM_LIVE_TESTS=1` (live USGS).

## Assumptions and non-goals

The brief left almost everything open. The interpretation, the 18 assumptions with how each was checked, and the scope are in [docs/ASSUMPTIONS.md](docs/ASSUMPTIONS.md). In short: "important" is defined by each user's rule, not by the system; events are polled, not streamed; there is no machine-learning scoring, no digests, no production-grade sign-in.

## Known limits (all also in the review log and the outcome page)

- **Only natural disasters have a live source** (ADR-020).
- **Sign-in is minimal and not production-safe:** knowing an email address is enough to act as that user (ADR-007, ADR-016). The session token lives in the browser's session storage.
- **Email addresses are not verified,** so the system could be used to send unwanted mail to third parties. Per-user limits, a test rate limit, cooldowns and an admin switch reduce the risk but do not remove it (R-019).
- **Slack was verified against its documentation, not against a live webhook** (R-008). **The webhook channel was never sent over real TLS to a public server** (R-031).
- **Delivery is at-least-once:** a crash between sending and recording can cause one duplicate message (ADR-003).
- **A webhook cannot target a private network,** by design (ADR-019). Requests go directly, not through a proxy, unless `Webhook:UseSystemProxy` is set.
- **News has no reliable importance level,** so keywords are the main filter there (A7). Severity tables and thresholds are starting values, never tuned on real use.
- **Revised earthquake values are not re-notified:** the first version of an event is kept.
- **One unexplained connection error line** can appear in the server log at startup (R-026).
- **Deleting a rule also deletes the history of its alerts,** there is no retention (data grows without bound), and delivery is one at a time, so a stalling destination delays everyone. Fan-out per rule is capped at five channels.
- **A cooldown never hides a more severe event** (ADR-021), but an alert queued before its rule or channel was switched off is suppressed, not sent.
- **Late or revised earthquakes:** an event that first appears in the feed more than three hours after it happened is dropped, and a revised magnitude does not alert again.
- **Keys that protect stored channel settings are kept unencrypted next to the database** (ADR-014). `dotnet run` uses the Development profile, which carries the placeholder secrets. The sign-in rate limit is per caller address, so behind a reverse proxy all users share one bucket until forwarded headers are configured.
- **Keywords match anywhere in the text** ("war" also matches "software"). Channels cannot be edited, only paused, tested or deleted.
