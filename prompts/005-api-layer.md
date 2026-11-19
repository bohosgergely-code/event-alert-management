# 005 – API layer

**Role:** Implementer, then adversarial reviewer of its own output.

## Prompt (verbatim)

> Continue with the API layer

## Response (summary)

- Infrastructure addition first: admin switch per channel type, respected by ingestion and the dispatcher.
- Authentication (signed tokens, roles, fail-closed fallback policy, sign-in rate limit), channels, rules (with presets and preview), user notification history, and the admin area (overview, events with injection, notifications with attempts and retry, rules, users, sources with poll-now, channel types).
- 24 API paths described by OpenAPI, exported to `docs/openapi.json`; a test that walks every route the application exposes.

## What was checked

*Written by the AI from the review log and the evidence folder. The author did not write this section.*

- Mutation checks on authorization and security logic: 14 of 14 caught.
- Ran the real application end to end with workers and a real SMTP server (`evidence/005-api-verification.md`).
- Found and fixed five problems along the way (REVIEW_LOG R-013 to R-017), two of them only visible by making a real HTTP request and reading the server log.
- Raised one open risk that was not fixed: unverified email addresses (R-019).

## Decision

The AI raised the open risk that anyone can register any email address as a channel (R-019) and recommended a confirmation link. The author's answer, in their own words: **"There is no need for user e-mail validation at this point."** The risk stays documented as a known limit in the README. The author gave this answer together with the next instruction, "Continue with the Angular frontend".

## Notes from the AI

- The first generated DTOs compiled but would have returned 500 on every request with a body (R-015). Unit tests did not catch it; a real request did.
- The error handler hides details from clients by design, so the server log is the first place to look when a request returns a generic 500.
