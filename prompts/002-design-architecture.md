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

- Reviewed the data model to assess whether it supports the planned functionality, including per-channel notification tracking, retries and suppressed notifications.
- Checked the consistency between the architecture diagram, event processing sequence and the notification state machine.
- Reviewed the ADRs to understand the alternatives considered and the trade-offs behind the recommended decisions.
- Checked that external service assumptions, including USGS, Slack and market data APIs, are clearly marked as unverified where no real API request has been performed.
- Reviewed the market price-band approach and identified its thresholds as initial values that require further validation.
- Checked whether the API outline addresses user data isolation and prevents users from accessing each other's data.
- Reviewed the changes to ADR-008 and ADR-009 to understand why notification uniqueness must be tracked separately for each channel.
- Reviewed the identified security risks, particularly webhook configuration and the protection of user-controlled destinations.

## Decision

After reviewing the architecture and decisions the core building started.

## Notes from the AI

- The self-review found one inconsistency in the earlier framing: ADR-008 originally described uniqueness per (rule, event), which contradicts the requirement to track delivery per channel. ADR-009 fixes it and ADR-008 now points to it.
- ADR-012 (price bands), the severity mapping tables and the retry delays are starting values, not validated numbers.
- The generic webhook channel will need stronger SSRF protection than Slack (private address blocking), because its destination is fully user-controlled.
