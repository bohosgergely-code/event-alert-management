# EventAlertManagement

Users define alert rules; the system watches public event sources (news, markets, natural disasters) and notifies them by email or Slack. Admins can see and operate the whole pipeline.

This repository is built as a worked example of taking a vague product brief to a working system with AI agents. The reasoning is part of the deliverable: see [docs/PLAN.md](docs/PLAN.md), [docs/ASSUMPTIONS.md](docs/ASSUMPTIONS.md), [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), [DECISIONS.md](DECISIONS.md), [REVIEW_LOG.md](REVIEW_LOG.md) and [prompts/](prompts/).

## Status

| Area | State |
|---|---|
| Domain model, matcher, cooldown, retry policy | Done, unit tested |
| Ingestion with de-duplication, outbox, dispatcher | Done, integration tested on real SQLite |
| Simulator source | Done |
| Email and Slack channels | Done, tested against stubs. **Not yet verified against a real SMTP server or Slack** (see REVIEW_LOG R-008, R-009) |
| USGS earthquake source | Not started |
| Web API (users, rules, admin) and authentication | Not started |
| Angular user and admin interface | Not started |
| Third channel (generic webhook) | Not started |

## Run the tests

Requires the .NET 8 SDK.

```bash
dotnet test
```

## Known limits

- Delivery is at-least-once. A crash between sending and recording can cause one duplicate message.
- News has no reliable severity, so keyword filters are the main control for news rules.
- Authentication will be minimal and is not production-safe.
