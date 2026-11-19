# 003 – Core implementation

**Role:** Implementer, then reviewer of its own output.

## Prompt (verbatim)

> Continue with the implementation of the core

## Response (summary)

Built in slices, each verified before moving on:
1. Core library (domain, typed filters, matcher, cooldown, severity mapping, retry policy, validation) with unit tests written from the design document.
2. Infrastructure (EF Core on SQLite, ingestion, source polling, dispatcher, retry service, settings encryption, simulator).
3. Email and Slack channels with validation, escaping and failure classification.
4. Integration tests against a real SQLite file, channel tests against a stubbed HTTP handler.

## What was checked

*Written by the AI from the review log and the evidence folder. The author did not write this section.*

- Mutation checks: 15 deliberate bugs, 14 caught, 1 equivalent (details in `evidence/001-matcher-mutation-check.md`, `evidence/002-pipeline-mutation-checks.md` and REVIEW_LOG R-007).
- Found and fixed a real bug through the integration tests (R-003) and two by reading the code (R-004, R-005).
- Re-ran the concurrency test 5 times to rule out flakiness.

## Decision

The author replied with the next instruction, in their own words: **"continue in english"**. That accepted the core as the base and set the language for the rest of the work. Two items stayed open and were not hidden: Slack behaviour not yet verified against the real service (R-008) and the SMTP transport never having talked to a real server (R-009).

## Notes from the AI

- Tests written first for the matcher were derived from the specification, not from the code.
- Slack status code semantics were written from memory and had to be verified (done in 004).
- The dispatcher is at-least-once: a crash between sending and saving can cause one duplicate send (ADR-003).
