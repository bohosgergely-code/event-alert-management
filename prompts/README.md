# Prompt history

Every prompt the author sent during the work, word for word, in order, plus the prompts the AI gave to the three independent reviewer agents. The AI's replies are summarised, not transcribed.

## What each file contains

- **Prompt (verbatim):** the author's message exactly as sent. Hungarian messages are followed by an English gloss, marked as a translation by the AI. HTML characters that the chat interface had escaped are written as the plain characters.
- **Response (summary):** what the AI produced, shortened.
- **What was checked:** the checks run on the output. In `003` to `010` this section is written by the AI from the review log and the evidence folder, and says so. In `000`, `001` and `002` it is the author's own text, left exactly as the author wrote it on GitHub.
- **Decision:** what was decided. Where possible it is the author's next instruction, quoted in their own words, because that is the real record of the decision.
- **Notes from the AI:** things the AI wants a reader to know.

Rejected attempts and failed approaches stay in the history. They are part of the evidence.

## What is not included

- Automated messages (hook notices, background-task notices, hand-backs from the reviewer agents) are not prompts from the author.
- The AI's own replies are not transcribed. The exported session is the complete record.

## Index

| File | The author's prompt (short) | Topic |
|---|---|---|
| [000-planning.md](000-planning.md) | the assignment; language, scope and technology; "I installed the GitHub app, try the push again" | Planning and the first push |
| [001-framing-assumptions.md](001-framing-assumptions.md) | "Continue with the framing phase" (Hungarian) | Assumptions, scope, first decision records |
| [002-design-architecture.md](002-design-architecture.md) | "Continue with the design phase" (Hungarian) | Architecture, data model, API, ADR-009 to ADR-014 |
| [003-core-implementation.md](003-core-implementation.md) | "Continue with the implementation of the core" | Domain, pipeline, channels, tests |
| [004-real-source-verification.md](004-real-source-verification.md) | "continue in english"; "was a new prompt file created?" (Hungarian) | Verification against real services, USGS source |
| [005-api-layer.md](005-api-layer.md) | "Continue with the API layer" | Authentication, channels, rules, admin, verification |
| [006-angular-frontend.md](006-angular-frontend.md) | "There is no need for user e-mail validation at this point. Continue with the Angular frontend." | Angular frontend, real-browser journey |
| [007-third-channel.md](007-third-channel.md) | "Continue with the third channel" | Webhook channel, address protection, extensibility proof |
| [008-final-pass.md](008-final-pass.md) | "Skip the sources, do the final pass" | Final pass over the repository |
| [009-independent-review.md](009-independent-review.md) | "Do the independent fresh-context review" | Three independent reviewers, with their prompts |
| [010-prompt-history.md](010-prompt-history.md) | "Fill in the prompt files with my own wording" | This record |
| [011-design-document.md](011-design-document.md) | "kreálj egy design doksit a feladatból és a kreált kódból" (Hungarian: create a design document from the task and the code) | The as-built design document |
