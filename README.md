# EventAlertManagement

Users define alert rules; the system watches public event sources (news, markets, natural disasters) and notifies them by email or Slack. Admins can see and operate the whole pipeline.

This repository is built as a worked example of taking a vague product brief to a working system with AI agents. The reasoning is part of the deliverable: see [docs/PLAN.md](docs/PLAN.md), [docs/ASSUMPTIONS.md](docs/ASSUMPTIONS.md), [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), [DECISIONS.md](DECISIONS.md), [REVIEW_LOG.md](REVIEW_LOG.md) and [prompts/](prompts/).

## Status

| Area | State |
|---|---|
| Domain model, matcher, cooldown, retry policy | Done, unit tested |
| Ingestion with de-duplication, outbox, dispatcher | Done, integration tested on real SQLite |
| Simulator source | Done |
| Email channel | Done. Verified with a real SMTP server (not Mailpit), see REVIEW_LOG R-009 |
| Slack channel | Done. Verified against Slack's documentation, **not yet against a live webhook** (R-008) |
| USGS earthquake source | Done. Built on a real captured response and checked against the live service |
| RSS news and market sources | Not started (hosts were not reachable when checked) |
| Web API: sign-in, channels, rules (presets, preview), notification history, admin area | Done. 24 endpoints, OpenAPI in `docs/openapi.json`, verified end to end |
| Angular user and admin interface | Done. 60 tests, real-browser journey passed three times, screenshots in `evidence/e2e/` |
| Third channel (generic webhook) | Done, with evidence that the core was not touched (`evidence/007-third-channel.md`). **Not yet verified over real TLS to a public server** (R-031) |

## Run the application

```bash
dotnet run --project src/EventAlertManagement.Api
```

In the Development environment the API starts with placeholder secrets from `appsettings.Development.json` (signing key and admin key `dev-admin-key`). **These are for local use only.** Anywhere else the application refuses to start until `Auth:SigningKey` (32+ characters) is set, and admin sign-in stays off until `Auth:AdminKey` is set. Use environment variables or user secrets, never the repository.

Swagger UI is available at `/swagger` in Development. Email goes to an SMTP server on `localhost:1025` by default (for example `python3 tools/smtp-sink.py`, or Mailpit). The local database file (`eventalert.db`) is created on first start; delete it after pulling changes that alter the data model.

## Run the web interface

```bash
cd src/EventAlertManagement.Web
npm install
npm start          # http://localhost:4200 (the API must be running on port 5080)
```

Details in [src/EventAlertManagement.Web/README.md](src/EventAlertManagement.Web/README.md). A scripted end-to-end journey in a real browser is in [e2e/](e2e/).

## Run the tests

Requires the .NET 8 SDK.

```bash
dotnet test
```

Optional checks against real services (skipped by default):

```bash
python3 tools/smtp-sink.py 1025 /tmp/smtp-out &
EAM_SMTP_TEST_PORT=1025 EAM_LIVE_TESTS=1 dotnet test
```

## Known limits

- **Webhook channel:** destinations inside a private network cannot be used (by design, ADR-019). Requests are sent directly, not through a proxy, unless `Webhook:UseSystemProxy` is set, in which case address protection depends on the proxy.
- **The session token is kept in the browser's session storage** (cleared on sign-out and when the tab closes). Any script running on the page could read it. The application has no user-supplied HTML, but this is a known limit of this scope.
- **Email addresses are not verified.** A user can register any address as an email channel, so the system could be used to send unwanted mail to third parties. Limits per user, a test rate limit and an admin switch reduce but do not remove the risk. The proper fix is a confirmation link before a channel is enabled (see REVIEW_LOG R-019).

- Delivery is at-least-once. A crash between sending and recording can cause one duplicate message.
- News has no reliable severity, so keyword filters are the main control for news rules.
- Sign-in is minimal and not production-safe: knowing an email address is enough to act as that user (ADR-007, ADR-016).
