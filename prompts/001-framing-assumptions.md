# 001 – Framing: assumptions, scope, first decisions

**Role:** Interviewer and architect (AI as a product manager's counterpart and devil's advocate).

## Prompt

Continue with the framing phase of the plan in `docs/PLAN.md`: write down the assumptions behind the vague brief, the scope and non-goals, and the first architecture decision records.

## Response (summary)

The AI produced:
- `docs/ASSUMPTIONS.md`: 18 numbered assumptions with status, risk if wrong and how to check; seven questions for the product owner with defaults; a devil's advocate section; scope and non-goals; source hypotheses to verify.
- `DECISIONS.md`: eight decision records (user-defined importance, polling adapters, outbox, channel contract, Slack webhooks, technology stack, minimal authentication, de-duplication and cooldown).

## What I checked

- Does each assumption have a way of being disproved, or is it just a statement? Reviewed the assumptions and confirmed that each one includes a validation method or a way to identify whether it is incorrect.
- Are the source hypotheses in ASSUMPTIONS.md section 7 marked as unverified? (They are, and must be tested with real requests.) Yes. The source-related assumptions are explicitly marked as unverified.
- Do the ADRs name real alternatives and real costs, or are they one-sided? Reviewed the ADRs to ensure that alternative approaches and their trade-offs are documented. 
- Is anything claimed that the AI could not know (for example exact API field names)? Reviewed the documentation for unsupported technical claims. External API details that have not been verified are identified as assumptions or items requiring validation. No unverified API field names should be treated as confirmed facts.

## Decision

The assumptions and decisions were accepted.

## Notes from the AI

- The USGS, RSS and market API details are hypotheses. They have not been verified against the real services yet, and no code relies on them.
- ADR-001 is the decision most likely to need revisiting if the product owner expects automatic detection of important events.
