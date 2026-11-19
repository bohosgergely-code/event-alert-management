# 009 – Independent fresh-context review

**Role:** Orchestrator of three independent reviewers, then verifier and fixer.

## Prompt (verbatim)

> Do the independent fresh-context review

### Prompts the AI then gave to the three reviewer agents

These were written by the AI on the author's instruction and are included because they are prompts used in the process. They are word for word, including the scratch paths of that session. Each reviewer worked in its own folder.

#### Reviewer 1: backend pipeline and data integrity

~~~text
You are an independent senior reviewer seeing this project for the first time. Review it for CORRECTNESS and DATA INTEGRITY. Do not assume the authors were careful; assume nothing works until you have verified it.

YOUR WORKING COPY (read and modify freely, it is discarded afterwards): /tmp/claude-0/-home-user-event-alert-management/f1cfde50-55bc-5340-b69a-120e4c190b3c/scratchpad/review-backend
Work ONLY inside that directory. It is a plain folder (no git history). Do not go looking for other copies of this project on the machine or for the authors' notes: some documents were deliberately removed so that your judgement stays independent.

THE PROJECT: EventAlertManagement, a .NET 8 + Angular app. Users define alert rules; events from sources are matched against rules; notifications are delivered by email / Slack / signed webhook; admins operate it. The specification you review against is README.md, docs/ASSUMPTIONS.md, docs/ARCHITECTURE.md, DECISIONS.md and docs/PLAN.md. The code is in src/ and tests/.

YOUR FOCUS (backend logic in src/EventAlertManagement.Core and src/EventAlertManagement.Infrastructure; the webhook/SSRF code is covered by another reviewer, skip it):
- The event -> notification pipeline: ingestion (de-duplication, cooldown, unique constraints, transaction boundaries), matcher and filters, severity mapping, the outbox dispatcher (claiming with leases, retry/backoff, transient vs permanent failures, manual retry), source polling and health, the USGS adapter.
- Concurrency and crash scenarios: two dispatchers, a crash between send and save, polling while requests write, SQLite specifics (WAL, DateTime stored as text and compared in SQL, case sensitivity).
- Time handling (UTC, daylight saving, cooldown boundaries), off-by-one errors, null handling, anything that silently produces WRONG RESULTS rather than errors.
- Places where the code does not do what docs/ARCHITECTURE.md or DECISIONS.md says it does.
- DESIGN BLIND SPOTS: important situations the specification never considers. For example: what happens to queued notifications when a rule is deleted or edited, or a channel is deleted or disabled; a user floods the system with rules; events arrive out of order or late; the clock changes; very many rules or events; unbounded data growth with no cleanup; an event revised by its source.

HOW TO WORK: read the code carefully. Run `dotnet test` once in your working copy for the baseline. Where you suspect a bug, PROVE it: write a small throwaway xUnit test or script (add files under tests/ in your copy only) and run it. A reproduced finding is worth far more than a suspicion. Do not "fix" production code; only report. Do not contact any host on the internet.

REPORT FORMAT (concise, no style nitpicks, no praise): a numbered list ordered by importance. For each finding: severity (important / medium / low), confidence (reproduced / read-only reasoning / suspicion), file:line, what is wrong, a concrete scenario with inputs that produces a wrong outcome, and how you verified it. Put unverified suspicions in a separate section at the end. Finish with a short list of claims in README/ARCHITECTURE/DECISIONS that you found false or unsupported. Aim for the 8-12 most valuable findings.
~~~

#### Reviewer 2: security of the API and the webhook

~~~text
You are an independent application-security reviewer seeing this project for the first time. Assume the authors made mistakes; assume nothing is secure until you have verified it.

YOUR WORKING COPY (read and modify freely, it is discarded afterwards): /tmp/claude-0/-home-user-event-alert-management/f1cfde50-55bc-5340-b69a-120e4c190b3c/scratchpad/review-security
Work ONLY inside that directory. It is a plain folder (no git history). Do not go looking for other copies of this project on the machine or for the authors' notes: some documents were deliberately removed so that your judgement stays independent.

THE PROJECT: EventAlertManagement, a .NET 8 + Angular app. Users define alert rules; events are matched; notifications are delivered by email / Slack / signed webhook to destinations the USER supplies; admins operate it. Sign-in is intentionally minimal (email only for users, a shared key for admin): do not report that on its own, but DO report anything that makes it worse than documented. The specification is README.md, docs/ASSUMPTIONS.md, docs/ARCHITECTURE.md, DECISIONS.md, docs/PLAN.md. Code is in src/ and tests/.

YOUR FOCUS:
1. SSRF and the webhook channel (src/EventAlertManagement.Infrastructure/Channels/WebhookChannel.cs, Security/SsrfGuard.cs, Security/PublicAddressPolicy.cs, Channels/SlackChannel.cs). Try hard to get the server to contact an internal address: unusual IP notations, IPv6 forms, IPv4-in-IPv6, DNS tricks, redirects, differences between how the URL is parsed at validation and at connection time, ports, userinfo, schemes, trailing dots, unicode/IDN hosts, proxy settings, connection pooling and DNS caching. Check the address policy for ranges that are wrongly allowed. Check what an attacker can learn from error messages and timing.
2. Authentication and authorization of the HTTP API (src/EventAlertManagement.Api): token handling, role checks, EVERY endpoint's ownership checks (can user A read, modify, delete or trigger anything belonging to user B? mass assignment? guessable ids?), admin endpoints, rate limiting (can it be bypassed, e.g. through forwarded headers or many sources; does it cover what it should?), CORS, error responses that leak details, model validation gaps, request size limits, JSON edge cases.
3. Injection and output encoding: event text from external sources is untrusted. Check email (HTML and headers), Slack formatting, webhook JSON, and the Angular frontend (unsafe bindings, script or markup injection, open redirects, tokens in URLs or logs).
4. Secrets: channel settings encryption (ASP.NET Data Protection key handling), what is logged, what the API returns, what is stored in plain text, development settings that could leak into a real deployment, the token the frontend stores.
5. Abuse and denial of service: unbounded loops or lists, expensive queries per request, a normal user causing the server to send many messages to third parties or exhausting resources.

HOW TO WORK: read the code carefully. Run `dotnet test` once in your working copy for the baseline. When you suspect a vulnerability, PROVE it with a throwaway test or script (add test files under tests/ in your copy only; you may also start the API locally on a free port with a temporary database and use curl). A reproduced exploit is worth far more than a suspicion. Do not modify production code to fix things; only report. Use only localhost: do not contact any host on the internet.

REPORT FORMAT (concise, no style nitpicks, no praise): numbered list ordered by severity. For each: severity (critical / high / medium / low), confidence (reproduced / read-only reasoning / suspicion), file:line, the weakness, a concrete exploit scenario with exact inputs, and how you verified it. Put unverified suspicions in a separate section. Finish with security-relevant claims in README/ARCHITECTURE/DECISIONS that are false or unsupported. Aim for the 8-12 most valuable findings.
~~~

#### Reviewer 3: frontend, API contract, tests and product fit

~~~text
You are an independent senior reviewer seeing this project for the first time, looking at the FRONTEND, the API CONTRACT, the TESTS and whether the PRODUCT fits the brief. Assume the authors made mistakes; verify rather than trust.

YOUR WORKING COPY (read and modify freely, it is discarded afterwards): /tmp/claude-0/-home-user-event-alert-management/f1cfde50-55bc-5340-b69a-120e4c190b3c/scratchpad/review-frontend
Work ONLY inside that directory. It is a plain folder (no git history). Do not go looking for other copies of this project on the machine or for the authors' notes: some documents were deliberately removed so that your judgement stays independent.

THE PROJECT: EventAlertManagement, a .NET 8 + Angular 22 app. The original brief, verbatim and nothing more was given: "We want users to be able to set up alerts so they get notified when something important happens in the world - like breaking news, market movements, natural disasters, that kind of thing. Should work for both email and Slack. Make it flexible enough that we can add more channels later. We need an admin view too." The specification is README.md, docs/ASSUMPTIONS.md, docs/ARCHITECTURE.md, DECISIONS.md, docs/PLAN.md, docs/openapi.json. Code is in src/ (frontend in src/EventAlertManagement.Web), tests/ and e2e/.

YOUR FOCUS:
1. Frontend correctness: state handling with signals under zoneless change detection (stale or missing updates after async responses, subscriptions or timers that never stop), form logic (src/app/core/rule-form.ts: does the request it builds always match what the API accepts? round trips rule -> form -> rule), route guards and session handling (core/auth.service.ts, auth.interceptor.ts, guards.ts), error handling, race conditions (double submits, navigating away mid-request, out-of-order responses when filters change quickly), edge cases (empty lists, very long text, paging beyond the last page, deleting the last item on a page).
2. Contract drift: compare docs/openapi.json and src/app/core/api-schema.ts with the hand-written calls in core/api.service.ts and with how screens use fields. Does the frontend assume anything the API does not guarantee (enum values, nullability, optional fields, date formats)?
3. Accessibility and usability basics by reading templates: labels, keyboard use, focus after actions, announcement of errors and success, meaning conveyed by colour only, table semantics, phone width, anything that would confuse a first-time user.
4. PRODUCT FIT against the brief: what would a product manager call missing, misleading or unfinished? Look at the end-to-end story a user and an admin actually experience (what happens after an alert fails, how a user knows a rule is working, how much of the brief is demonstrably delivered rather than only designed). Be specific and honest, not generic.
5. Do the automated tests (tests/, the Angular *.spec.ts files, e2e/journey.cjs) really catch regressions, or are some tautological, over-mocked or testing implementation details? Name specific tests that give false comfort and the real defect each would miss.

HOW TO WORK: read the code carefully. Run `dotnet test` and, in src/EventAlertManagement.Web, `npm ci` then `npm test -- --watch=false` once for the baseline. Where you suspect a bug, PROVE it with a throwaway spec (Vitest through `ng test`) or script in your copy only. Do not modify production code; only report. Use only localhost: do not contact any host on the internet.

REPORT FORMAT (concise, no style nitpicks, no praise): a numbered list ordered by importance. For each: severity (important / medium / low), confidence (reproduced / read-only reasoning / suspicion), file:line, what is wrong, a concrete scenario, and how you verified it. Put unverified suspicions in a separate section. Then a short section "Product fit vs the brief". Aim for the 8-12 most valuable findings.
~~~

## Response (summary)

Three fresh reviewer agents, each on a stripped copy of the code (the authors' own assessments and the git history removed), reported 40 findings in total. Every claim was reproduced with an independent probe, or turned into a test written first that failed on the old code, before any fix; each fix was then reverted once to check that a test notices. 19 findings were fixed, 9 partly fixed, 11 documented as not fixed with reasons, and one result recorded where nothing was wrong (the address protection could not be bypassed). Full table: [../evidence/independent-review/README.md](../evidence/independent-review/README.md).

## What was checked

*Written by the AI from the review log and the evidence folder. The author did not write this section.*

- Reproduced the seven backend claims with my own probes before any change (`evidence/independent-review/01-backend-report-verification.md`): among them, 19 of 20 rounds of overlapping ingests failed.
- Security findings became tests written first: 22 of 24 failed on the old code.
- Frontend claims reproduced with controlled response orders (the wrong filter's rows on screen; a checkbox contradicting the server; an empty page with no pager).
- Each fix reverted individually: 31 further reverts, 30 caught and one harmless duplicate explained (the address-spelling fold, which a second check also covers; REVIEW_LOG R-035).
- The browser journey was extended for the new behaviour and passed three times in a row.
- Slips during the fixing are listed in REVIEW_LOG R-038.

## Decision

The author's next instruction, in their own words: **"Fill in the prompt files with my own wording"**. The findings that were deliberately not fixed are listed with their reasons in `evidence/independent-review/README.md` and `docs/OUTCOME.md`.

## Notes from the AI

- The reviewers' findings were mostly interactions between features, which the AI's own tests had been thin on (R-036). Mutation checks cannot find what the author of the tests never thought to test.
- The first attempt to isolate the reviewers with git worktrees failed (the clone tracked only one branch). Isolation was done by copying the code into plain folders with the self-assessment documents and the history removed, which is stricter.
