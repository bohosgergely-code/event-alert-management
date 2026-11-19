# 007 – Third channel (generic webhook)

**Role:** Implementer, then adversarial reviewer.

## Prompt (verbatim)

> Continue with the third channel

## Response (summary)

- Built the generic webhook channel: signed JSON posts, with protection against a destination that points inside a network (checked when typed and again when the connection is made).
- Proved the extensibility claim with a diff: three new production files, one added line in the composition root, nothing else touched.
- Mutation checks, a real-socket test of the guard, and two new steps in the real-browser journey.
- A follow-up commit gave the channel a proper two-field form and cleared the secret from the form after saving (R-033).

## What was checked

*Written by the AI from the review log and the evidence folder. The author did not write this section.*

- Every address in the policy table against an independent source (Python's `ipaddress`). Python was the unsafe one in two cases (R-030).
- Why each tricky URL was refused (probe output), so that nothing passes a test for the wrong reason.
- That nothing outside the three new files changed (`git diff` over the core, pipeline, dispatcher, persistence, API and frontend printed nothing).
- Opened the screenshots: found the secret still visible in the form after a successful add.

## Decision

The AI listed what was left: the RSS news and market sources, the author's own wording in the prompt files, and a final pass, and recommended skipping the two sources. The author's answer, in their own words: **"Skip the sources, do the final pass"**. That decision is recorded in ADR-020. One thing was explicitly not verified: delivery over real TLS to a public server (R-031).

## Notes from the AI

- An idea to test through a direct network connection was blocked, rightly: the environment requires its proxy and bypassing it is not allowed. It was dropped and recorded instead of worked around.
- A standard-library "is this address global" helper is not a safe policy by itself.
