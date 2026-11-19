# 000 – Planning

**Role:** Planner / critical thinking partner.

## Prompts (verbatim, in the order sent)

HTML entities that the chat interface had escaped (`&amp;`, `&quot;`, `&#39;`) are written as the plain characters here. Hungarian prompts are followed by an English gloss marked as a translation by the AI.

### Prompt 1: the assignment

> Kaptam egy házi feladatot. Elemezd, készíts egy elemzést és implementációs tervet mit kellene létrehozni. Vedd figyelembe hogy kb 4-5 óra áll rendelkezésre.
> Task: Feature Design & Build from a Vague Brief
> Scenario
> A product manager has handed you the following brief — exactly as written, nothing
> more:
> "We want users to be able to set up alerts so they get notified when
> something important happens in the world — like breaking news, market
> movements, natural disasters, that kind of thing. Should work for both
> email and Slack. Make it flexible enough that we can add more channels
> later. We need an admin view too."
> You may use any technology, language, or framework you choose.
> There is no further specification. No wireframes. No schema proposal. No agreement
> on what "something important" means, where the data comes from, or how events
> are detected.
> Your Task
> Using AI agents, take this brief from ambiguity to a working implementation.
> Before you start building, define your own plan of attack. Decide what
> deliverables you think this brief requires, what order to tackle them in, and why. Your
> plan is itself part of the evaluation — we want to see how you scope and structure
> work, not just how you execute a predefined checklist.
> Then execute against your own plan. As you go, critically assess what the AI
> produces: don't assume outputs are correct just because they look plausible.
> Surface the checks you run, the things you reject, and the course-corrections you
> make — that judgment is as important as the work itself.
> Suggested Timeframe
> Plan for 24 hours. A partial but well-reasoned solution beats a complete but
> undocumented one — use the time to think, plan, and show your work, not to
> maximize output.
> A Note on What We're Evaluating
> We are not evaluating the solution itself. We are evaluating how you arrived at it.
> A working, polished implementation with no visible process tells us very little. What
> we want to see is how you directed the AI: how you framed the problem, what
> decisions you made and why, where you course-corrected, and how you validated
> output before accepting it. The artifacts of that process — plans, decision logs,
> prompt drafts, notes, intermediate outputs — are the submission. The code is
> evidence, not the point.
> We pay particular attention to how you evaluated the validity of what the AI
> produced — the assumptions you challenged, the hallucinations or shortcuts you
>
> caught, the code you rewrote or rejected, and the sanity checks you applied to
> generated artifacts.
> AI Tooling
> Use any AI tool(s) you prefer — there is no required or favored product.
> What to Submit
> Please share your results with us via a GitHub repository that must contain:
> • commits for each major milestone, with meaningful commit messages
> • your prompt history — all prompts used during the process
> • your plans, working artifacts, and evidence of how you worked — design
> docs, decision logs, prompt drafts, intermediate outputs, notes, scratch files,
> screenshots, etc.
> • your deliverables — as defined by your own plan

*Gloss of the Hungarian opening (AI translation):* "I received a homework assignment. Analyse it, make an analysis and an implementation plan of what should be created. Take into account that about 4-5 hours are available."

### Prompt 2: language, scope and technology

> megvan a git init a c:\Projects\EventAlertManagement alatt.  Legyen EventAlertManagement a project neve. A feltöltendő md fájlok tartalma legyen angol nyelvű, olyan nyelvezetben hogy ember számára érthető legyen. Idő keret ne legyen feltűntetve benne, az nem releváns. Munkaterv időkerete se legyen feltűntetve csak a fázisok outputok stb. Angular és typescript legyen inkább, a backend lehet asp.net web api. Túl nyúlhat a 4-5 órás kereten is túl, holnap reggelig kell elkészülni vele, de ma kb még 5-6 órát tudok vele foglalkozni. Ha kész van a tervvel az output plan md fájlt töltsd fel a github repo-ba.

*Gloss (AI translation):* "The git init is done under c:\Projects\EventAlertManagement. Let the project's name be EventAlertManagement. The md files to be uploaded should be in English, in language a person can understand. Do not state a time frame in them, it is not relevant. The work plan should not state a time frame either, only the phases, outputs etc. Preferably Angular and TypeScript, the backend can be ASP.NET Web API. It may go beyond the 4-5 hour frame, it has to be finished by tomorrow morning, but I can work on it about 5-6 more hours today. When the plan is ready, upload the output plan md file to the GitHub repo."

### Prompt 3: after the first push was refused

> Telepítettem a Claude GitHub appot, próbáld újra a pusht

*Gloss (AI translation):* "I installed the Claude GitHub app, try the push again."

## Response (summary)

The AI produced `docs/PLAN.md`: decisions for ten ambiguities in the brief, an architecture, a phased deliverable list with a cut order, AI agent roles, a validation plan with concrete checks, a repository layout, risks and a definition of done.

## What I checked

 - Reviewed the PLAND.md file and accepted it.

## Decision

Plan accepted as the starting point. Technology changed from the first draft (Python and server-rendered pages) to ASP.NET Core and Angular, and time estimates were removed, both at the author's request (Prompt 2).
