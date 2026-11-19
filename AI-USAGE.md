# How the AI was used, and how its output was checked

The brief asked for the process, not just the result. This page is the short version. The long version is [REVIEW_LOG.md](REVIEW_LOG.md) (34 entries) and the `evidence/` folder.

## How the work was directed

One AI assistant did the work in a single continuing collaboration with the author, who set direction and made the calls: stack (Angular and ASP.NET instead of the first proposal), language, no time estimates, no email verification, skip the news and market feeds, finish with a final pass. The AI proposed alternatives and a recommendation, then proceeded unless told otherwise. It was used in the roles the plan named:

| Role | Where it shows |
|---|---|
| Interviewer / devil's advocate | `docs/ASSUMPTIONS.md` (18 assumptions, questions for a product owner, "where this interpretation may be wrong") |
| Architect | `docs/ARCHITECTURE.md`, ADR-001 to ADR-020 (each with the options not taken) |
| Implementer | one slice at a time, a commit per milestone |
| Test author | tests written from the specification, not from the code |
| Reviewer of its own output | read-throughs, mutation checks, real runs (below) |

The phases ran in this order, each with its own commit and prompt file: plan, framing, design, core, real services and the first real source, API, frontend, third channel, final pass.

## The rule that was applied

**Nothing counts as working until something other than the code that was just written has said so.** That "something" was, in rough order of how often it found problems:

1. **A real run.** Starting the actual application, a real SMTP server and a real browser, and reading the server log. This found the problems that tests could not (the request validation that compiled but returned 500 on every request, a screen that never refreshed).
2. **Deliberate bugs ("mutation checks").** Break one line, run the suite, and see whether a test fails. 66 bugs were injected across the matcher, pipeline, security code, API authorization, frontend logic and the webhook guard. 64 were caught; 2 survived and both turned out to be harmless duplicates of other checks (REVIEW_LOG R-007, R-033). Every time a suite passed on its first run, this was done before trusting it.
3. **Independent calculations.** Expected values computed outside the code under test: the earthquake counts and severities from a Python script over the real captured feed, HMAC values from an RFC test vector and Python, address classification compared with Python's `ipaddress` module.
4. **Real data, captured first.** The earthquake parser was written against a response captured from the live service, not against a sample the AI imagined.
5. **Reading the screenshots.** Passing assertions say nothing about wording or layout; reading them found a grammar mistake and a secret left in a form.

## What the AI got wrong (and how it was caught)

Not exhaustive. See the summary table in the review log for all entries.

| Kind of mistake | Example | Caught by | Entry |
|---|---|---|---|
| Compiles, fails at runtime | Request validation attributes on the wrong target made every request with a body return 500 | A real request, then the server log | R-015 |
| Passes unit tests, wrong against a real database | A delivery record saved as an update instead of an insert, so every send would fail after the message was already sent | Integration tests on a real database file | R-003 |
| Logic that breaks something else | Clearing the change tracker on one failed poll would have dropped the updates of every other source in the loop | Reading the code before running it | R-004 |
| Quietly permissive by default | Filters for the wrong category were ignored, leaving a rule that matches every event | Thinking through a mistaken request, then a test | R-013 |
| Inconsistent rules in different places | Channel type names compared with and without case in three places | A failing test | R-014 |
| Settings read too early | Authentication settings read before the final configuration existed | Every API test failing at start-up | R-016 |
| Unusable generated output | API types with every field optional | Reading the generated file | R-020 |
| Library helper as a policy | Python's "is this address global" calls NAT64 and multicast addresses global | Comparing against the stricter table | R-030 |
| A claim from memory | How Slack answers errors and rate limits | Reading Slack's documentation | R-008 |
| A design flaw found only in real data | The first poll would alert on a whole day of old earthquakes | Examining a real response | R-010 |
| Newer API than the target | A .NET 9 method in a .NET 8 project | The compiler | R-032 |

Several apparent bugs were not bugs: the API had not been started (R-024), the rate limiter was working (R-025), a test shared state with another (R-017). Those are logged too, because time spent on a phantom is also a cost.

## Where I did not follow my own plan

The plan promised these. They did not all happen:

- **An independent reviewer in a fresh context was not used.** The plan called for a second AI pass that sees only the diff and the specification, to reduce self-agreement. What stood in for it is the list above: mutation checks, independent calculations and real runs. That is a weaker substitute for finding design blind spots (as opposed to implementation mistakes), and a reviewer running one can still do it.
- **Mailpit was not used,** a small SMTP server stood in (R-009).
- **Slack was never posted to a live webhook** and **the webhook was never sent over real TLS** (R-008, R-031).
- **The news and market sources were not built** (ADR-020).
- **One startup log line is unexplained** (R-026).

## What not to trust without a human

- The severity tables, price-band numbers and retry delays are starting values. They were never tuned against real use.
- The user interface is functional and checked for basics (labels, focus, colour contrast in both schemes, phone width). It has not been through an accessibility audit.
- Security reasoning (the address guard, sign-in handling) is tested, but it was not reviewed by a security specialist, and sign-in is intentionally minimal (ADR-007, ADR-016).
- Anything described as "not verified" in the review log.

## About the prompt history

`prompts/` has one file per exchange: the role, the prompt, a summary of what the AI produced, what was checked and the decision. They are summaries, not transcripts. The author's own words for each prompt and decision are to be added there, and the full session export is the complete record.
