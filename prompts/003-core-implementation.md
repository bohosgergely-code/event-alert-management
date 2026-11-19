# 003 – Core implementation

**Role:** Implementer, then reviewer of its own output.

## Prompt

Continue with the implementation of the core, following `docs/ARCHITECTURE.md`: domain model, matcher, ingestion with de-duplication and cooldown, outbox dispatcher with retries, simulator source, Email and Slack channels, and tests.

## Response (summary)

Built in slices, each verified before moving on:
1. Core library (domain, typed filters, matcher, cooldown, severity mapping, retry policy, validation) with 60 unit tests written from the design document.
2. Infrastructure (EF Core on SQLite, ingestion, source polling, dispatcher, retry service, settings encryption, simulator).
3. Email and Slack channels with validation, escaping and failure classification.
4. Integration tests against a real SQLite file, channel tests against a stubbed HTTP handler.

Result: 141 tests passing.

## What I checked

- Mutation checks: 15 deliberate bugs, 14 caught, 1 equivalent (details in `evidence/` and `REVIEW_LOG.md`).
- Found and fixed a real bug through the integration tests (R-003) and two by reading the code (R-004, R-005).
- Re-ran the concurrency test 5 times to rule out flakiness.

## Decision

Core accepted as the base. Two items remain open and are not hidden: Slack behaviour is not yet verified against the real service (R-008), and the SMTP transport has never talked to a real server (R-009).

## Notes from the AI

- Tests written first for the matcher were derived from the specification, not from the code.
- Slack status code semantics were written from memory of Slack's behaviour and must be verified.
- Dispatcher is at-least-once: a crash between sending and saving can cause one duplicate send. This is documented in ADR-003.
