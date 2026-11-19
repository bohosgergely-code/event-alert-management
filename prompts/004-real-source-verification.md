# 004 – Verification against real services and the USGS source

**Role:** Implementer and reviewer.

## Prompt

Continue (in English). Close the open items from the review log: verify Slack and email behaviour against reality, and build the first real source.

## Response (summary)

- Network access was blocked at first; checked the environment settings and retried. USGS and Slack documentation became reachable. GitHub releases (Mailpit) stayed blocked.
- Slack: read four official documentation pages and confirmed the assumed behaviour (R-008).
- Email: ran a real SMTP server (aiosmtpd) and tested the real transport, including 4xx and 5xx replies (R-009).
- USGS: captured a real response, analysed it independently in Python, then wrote the adapter and tests against that fixture (R-010).

## What I checked

- Expected values in the tests came from a separate Python script, not from the code under test.
- 5 more mutation checks on the adapter, all caught. Earlier 15 mutation checks still hold.
- Live test against the real USGS service passed.
- Two failing tests were investigated first and turned out to be test mistakes (R-011).

## Decision

Slack is "verified against documentation, not against the live service". Email is verified with a real SMTP server, not with Mailpit. Both statements are in the README.

## Notes from the AI

- A design flaw (first poll flooding users with a day of old quakes) was found by looking at the real data, not by the tests. The tests only protect it now.
- News and market sources are not verified: those hosts were not reachable.
