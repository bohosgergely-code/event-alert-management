# 008 – Final pass

**Role:** Reviewer, reading the repository the way an outside reader would.

## Prompt (verbatim)

> Skip the sources, do the final pass

## Response (summary)

- Inventory of the repository, a scan for leftovers, broken links, secrets and time estimates; a fresh clone from GitHub reproduced the build and all tests from nothing.
- Found that the README overclaimed (it said the system watches news and markets, but only natural disasters have a live source), that the rule editor implied the same, that a plain `dotnet run` listened on a different port than the frontend expects, and that several statuses and counts were stale. All corrected (R-034).
- Added `docs/OUTCOME.md` (plan against reality, definition of done, open items), `AI-USAGE.md` (how the AI was directed, what it got wrong, where the plan was not followed), an index at the top of the review log, ADR-020 for the scope cut, `tools/verify.sh`, and a GitHub Actions workflow (which then passed).

## What was checked

*Written by the AI from the review log and the evidence folder. The author did not write this section.*

- A market-event end-to-end test was added because "markets work with test events" had only matcher-level tests behind it.
- The README run instructions were followed literally against the real stack (API, web, proxy, user and admin sign-in).
- Link check over every markdown file; scan of tracked files for secrets.

## Decision

The AI reported what still fell short of the plan: no independent reviewer in a fresh context had been used. The author's answer, in their own words: **"Do the independent fresh-context review"**.

## Notes from the AI

- The plan promised an independent reviewer; the first version of the final pass said honestly that it had not happened and that mutation checks and real runs were a weaker substitute.
