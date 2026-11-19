# Prompt history

This folder keeps the prompts used while building the project, so the way AI was directed is visible and reviewable.

## Convention

One file per meaningful exchange, named `NNN-role-topic.md` (for example `001-framing-assumptions.md`). Each file contains:

- **Role** – which AI role was used (interviewer, architect, implementer, test author, reviewer).
- **Prompt** – the prompt as sent.
- **Response (summary)** – what the AI produced, shortened. Full outputs go to `evidence/` when needed.
- **What I checked** – the checks run on the output.
- **Decision** – accepted, rejected or changed, and why.

Rejected and failed attempts stay in the history. They are part of the evidence.

## Index

| File | Topic |
|---|---|
| [000-planning.md](000-planning.md) | Original assignment and first planning exchange |
| [001-framing-assumptions.md](001-framing-assumptions.md) | Framing phase: assumptions, scope, first decision records |
| [002-design-architecture.md](002-design-architecture.md) | Design phase: architecture, data model, API, ADR-009 to ADR-014 |
| [003-core-implementation.md](003-core-implementation.md) | Core implementation: domain, pipeline, channels, tests |
| [004-real-source-verification.md](004-real-source-verification.md) | Verification against real services, USGS source |
| [005-api-layer.md](005-api-layer.md) | API layer: auth, channels, rules, admin, verification |
| [006-angular-frontend.md](006-angular-frontend.md) | Angular frontend, real-browser journey |
| [007-third-channel.md](007-third-channel.md) | Third channel, SSRF protection, extensibility proof |
