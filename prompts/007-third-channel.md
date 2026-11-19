# 007 – Third channel (generic webhook)

**Role:** Implementer, then adversarial reviewer.

## Prompt

Continue with the third channel.

## Response (summary)

- Built the generic webhook channel: signed JSON posts, with protection against a destination that points inside a network (checked when typed and again when the connection is made).
- Proved the extensibility claim with a diff: three new production files, one added line in the composition root, nothing else touched.
- 142 new tests, 16 mutation checks, a real-socket test of the guard, and two new steps in the real-browser journey.

## What I checked

- Every address in the policy table against an independent source (Python's `ipaddress`). Python was the unsafe one in two cases.
- Why each tricky URL was refused (probe output), so that nothing passes a test for the wrong reason.
- That nothing outside the three new files changed (`git diff` over the core, pipeline, dispatcher, persistence, API and frontend printed nothing).
- Opened the screenshots: found the secret still visible in the form after a successful add.

## Decision

Extensibility claim accepted with evidence. One thing is explicitly not verified: delivery over real TLS to a public server (R-031).

## Follow-up commit

After the proof, a proper two-field form replaced the JSON box for this channel (separate commit, R-033), and the secret is cleared from the form after saving.

## Notes from the AI

- An idea to test through a direct network connection was blocked, rightly: the environment requires its proxy and bypassing it is not allowed. It was dropped and recorded instead of worked around.
- A standard-library "is this address global" helper is not a safe policy by itself.
