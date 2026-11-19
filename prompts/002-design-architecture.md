# 002 – Design: architecture, data model, API

**Role:** Architect.

## Prompt

Continue with the design phase of the plan in `docs/PLAN.md`: domain model, API outline, architecture diagram, and decision records for the main trade-offs. Present alternatives for the key choices, then recommend one.

## Response (summary)

The AI produced:
- `docs/ARCHITECTURE.md`: system diagram, code structure and contracts, event-to-notification sequence, notification state machine, data model with severity mapping, source and channel designs, API outline, frontend structure, cross-cutting concerns, testing approach and open risks.
- Six new records in `DECISIONS.md` (ADR-009 to ADR-014): notification granularity, typed JSON filters, matching inside the ingestion transaction, market price bands, single dispatcher with leases, and encrypted channel settings.
- ADR-008 was amended: uniqueness became (rule, event, channel) instead of (rule, event), because per-channel delivery status cannot be expressed otherwise.

## What I checked

*(To be completed by the author. Suggested checks:)*
- Does the data model support every feature in the plan, such as per-channel retry and "why did I not get this alert" (suppressed notifications)?
- Is each ADR's list of alternatives real, or a straw man?
- Is there anything claimed about an external service (USGS revision behavior, Slack responses, market API limits) that is stated as fact rather than "to verify"?
- Is the market price-band idea sound? Walk through one ticker over a day by hand.
- Is the transaction boundary in ADR-011 consistent with the sequence diagram?
- Does the API outline leave a way for user A to read user B's data?

## Decision

*(To be completed by the author.)*

## Notes from the AI

- The self-review found one inconsistency in the earlier framing: ADR-008 originally described uniqueness per (rule, event), which contradicts the requirement to track delivery per channel. ADR-009 fixes it and ADR-008 now points to it.
- ADR-012 (price bands), the severity mapping tables and the retry delays are starting values, not validated numbers.
- The generic webhook channel will need stronger SSRF protection than Slack (private address blocking), because its destination is fully user-controlled.
