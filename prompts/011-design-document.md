# 011 – Design document

**Role:** Technical writer working from the repository itself.

## Prompt (verbatim)

> kreálj egy design doksit a feladatból és a kreált kódból

*Gloss (AI translation):* "Create a design document from the task and from the created code."

## Response (summary)

Wrote [../docs/DESIGN.md](../docs/DESIGN.md): the task and how its ambiguities were settled, goals and non-goals, the solution overview, the code structure and extension points, the data model with invariants and the notification state machine, the behaviour of ingestion, delivery and the other flows, the three channels (including the webhook's signature scheme and its layered address protection), the sources, the HTTP API, the frontend, cross-cutting design (security as a threat-to-control table, reliability, time, configuration, limits, observability), how everything was verified, an index of the 21 decisions, the known limits, and a glossary. It describes the system as built; the earlier `ARCHITECTURE.md` now says it is the pre-build design.

## What was checked

*Written by the AI from the evidence. The author did not write this section.*

- Every number and behaviour stated in the document was compared with the code, not remembered: the 24 paths and 30 operations (from the OpenAPI file), the entity fields, the limits table, the configuration keys and defaults, the worker intervals and batch size, the retry delays, the severity tables, the health thresholds, the webhook headers and masking.
- A statement checked against the code turned out to be accurate but easy to misread: the market severity table exists in code but is used only by tests, and the document says so.
- All five Mermaid diagrams were run through the real Mermaid parser, so GitHub will draw them.
- Relative links across all 31 markdown files: none broken.

## Decision

This is the author's own instruction. It also resolved the question left open in `010`: the author's next instruction after "Fill in the prompt files with my own wording" is the one quoted above.

## Notes from the AI

- The document is in English because the author asked earlier for the markdown files in the repository to be in English; the request itself was in Hungarian.
- Where the pre-build design and the code disagree (cooldown escalation, suppression at send time, new limits, the third channel), the code is described and the older document points here.
