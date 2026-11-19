# Review log

Every entry follows the same shape: what the AI produced or claimed, the check performed, the result, and the decision.

---

## R-001 – Tests passed on the first run (suspicious)

- **AI output:** 60 unit tests for matcher, severity mapping, cooldown, retry and rule validation, all green immediately.
- **Check:** Mutation check. Four deliberate bugs injected into the matcher one at a time (see `evidence/001-matcher-mutation-check.md`).
- **Result:** All four were caught. The tests are not decorative.
- **Decision:** Accepted.

## R-002 – Channel contract differs from the design document

- **AI output:** The design (`docs/ARCHITECTURE.md` section 6) described `Render` and `Send` as separate channel operations. The implemented contract has only `SendAsync`, plus `ValidateSettings` and `MaskSettings`.
- **Check:** Compared contract against design while writing the code.
- **Result:** Rendering is channel-internal and does not need to be public in the contract. Keeping it separate would force every future channel to expose a message type the dispatcher never uses. `MaskSettings` was added because the API must never return secrets, which the design required but did not assign to any operation.
- **Decision:** Accepted deviation; design document to be updated to match.

## R-003 – Dispatcher failed on every send (AI code bug, found by tests)

- **AI output:** The dispatcher recorded a delivery attempt with `notification.Attempts.Add(new DeliveryAttempt { ... })`.
- **Check:** First run of the dispatcher integration tests against a real SQLite file.
- **Result:** 7 of 90 tests failed with `DbUpdateConcurrencyException: expected 1 row, affected 0`. Cause: the attempt's `Id` is a GUID generated in code. When such an entity is added through a collection of an already tracked parent, EF treats it as existing and issues an UPDATE instead of an INSERT. In production every delivery would have failed on save, after the message was already sent, causing repeated sends.
- **Decision:** Rewrote to `db.DeliveryAttempts.Add(...)` with a comment explaining why. All dispatcher tests pass. A mock-based test would not have caught this; this is the reason for testing against a real database.

## R-004 – One failing source would have broken the others (found by reading the code)

- **AI output:** `PollAsync(Source)` called `ChangeTracker.Clear()` after a failure, while `PollDueAsync` was looping over tracked sources.
- **Check:** Review before running anything.
- **Result:** After the first failure, every remaining source in the loop would have been detached, so their health and cursor updates would silently never be saved.
- **Decision:** Rewrote polling so each poll is its own unit of work, starting from an empty tracker and loading the source by id. Added the test `One_failing_source_does_not_stop_the_others_in_the_same_cycle`.

## R-005 – Meaningless condition in ingestion

- **AI output:** `if (newCursor is not null || items.Count >= 0) { source.Cursor = newCursor ?? source.Cursor; }`. The second part is always true.
- **Check:** Read-through.
- **Result:** Dead logic that suggests intent that is not there (it looked like "only update when something changed").
- **Decision:** Replaced by the plain assignment.

## R-006 – A failing concurrency test was the test's fault, not the code's

- **Observation:** `Two_dispatchers_running_at_once...` expected 15 sends but saw exactly 10, on every run.
- **Check:** The count was exactly the batch size, and there were no duplicate sends. That pointed away from a claiming bug (which would show duplicates or random numbers).
- **Result:** Each simulated dispatcher ran only one round of 10. The test did not drain the queue.
- **Decision:** Fixed the test so each dispatcher loops until nothing is due. Ran it five times in a row, all green. Then proved the test has teeth by making claims always succeed (mutation M5), which made it fail.

## R-007 – Mutation checks on the pipeline and the security code

- **Check:** 15 deliberate bugs across matcher, ingestion, dispatcher, Slack and email channels, one at a time (see `evidence/002-pipeline-mutation-checks.md`).
- **Result:** 14 caught. One survived (M14, removing the explicit character check on email addresses).
- **Analysis of the survivor:** The inputs that check was meant to block (line breaks, commas, angle brackets) are also rejected by `MailAddress.TryCreate` together with the round-trip equality check. Removing only the round-trip check made a test fail, so that is the check that really protects. The explicit list is redundant defence in depth.
- **Decision:** Kept both. Not a gap, but recorded so nobody assumes the character list is the only guard.

## R-008 – Slack behaviour was written from memory (PARTLY CLOSED)

- **AI claim:** Slack webhooks answer 200 with body `ok`; 404 and 410 mean the webhook or channel is gone; 400 means invalid payload; 429 means rate limited; only `<`, `>` and `&` need escaping.
- **Check:** Read Slack's official documentation (incoming webhooks page, rate limits page, the changelog entry that defines webhook errors, and the message formatting page). Details in `evidence/004-usgs-and-slack-verification.md`.
- **Result:** All claims confirmed. Documented: 200 `ok`; 400 `invalid_payload`; 403 `action_prohibited`; 404 `channel_not_found` and `no_service`; 410 `channel_is_archived`; 429 with a `Retry-After` header; 500 `rollup_error` described as "likely not your fault"; escape exactly `&`, `<`, `>`. The status classification in `SlackChannel` matches.
- **Improvement noted, not done:** Slack sends `Retry-After` on 429. We ignore it and use our own backoff (first retry after 1 minute). Acceptable at this scale (limit is about 1 message per second per webhook).
- **Still open:** no message has been posted to a real webhook. Unit tests use a stub. Until that is done, Slack must be described as "verified against documentation, not against the live service".

## R-009 – SMTP transport had never talked to a real server (CLOSED, with a caveat)

- **Check:** Ran a real SMTP server (`tools/smtp-sink.py`, built on aiosmtpd) and pointed the real `MailKitEmailTransport` at it through opt-in tests (`EAM_SMTP_TEST_PORT`). The sink answers 450 or 550 for special recipients so the failure paths can be exercised.
- **Result:** 4 of 4 pass: a real message is accepted; a 450 reply becomes a transient failure; a 550 reply becomes a permanent failure; an unreachable server is transient and the error text contains no address details. The received message (`evidence/003-real-smtp-message.eml`) has correct headers, and the HTML part escapes `<b>` in the title while the plain text part keeps it as typed, which is correct.
- **Caveat:** Mailpit could not be downloaded in this environment (GitHub releases returned 403), so it was not used. The sink proves the SMTP exchange, not Mailpit's web interface. If Mailpit is available on the author's machine, repeat once and add a screenshot.

## R-010 – First real data changed the design (USGS)

- **AI output:** The USGS adapter design assumed events could simply be polled and de-duplicated by id.
- **Check:** Captured a real response and examined it with an independent script before writing the parser.
- **Findings:**
  1. Fields, URL and id uniqueness are as assumed.
  2. **Design flaw found by reasoning about the data:** the feed holds a whole day of quakes. Without a limit, the first poll would notify users about up to 36 old earthquakes. Fixed with a maximum event age (default 3 hours) and tests (2, 4 and 36 events for 1, 3 and 24 hours, counted independently in Python).
  3. `updated` differs from `time` on every feature and 9 of 36 features were still `automatic`, so magnitudes are revised after publication. We keep the first version and do not re-notify. Documented as a limitation.
  4. The "2.5+" feed contains a magnitude of 2.48 (rounding), so a filter on exactly 2.5 would not behave as the feed name suggests.
  5. With the starting severity table, a real M5.0 quake is severity 2 and the strongest quake of the day (M6.1) is severity 3. The table is a guess; users can filter on magnitude directly, so this is not blocking, but it should be tuned.
- **Decision:** Adapter written against the real fixture. A malformed feature is skipped, but an unexpected document shape raises an error, so a format change shows up as a failed poll in source health rather than as silence.

## R-011 – Test failures after seeding a second source were test mistakes

- **Observation:** Two tests failed after the USGS source was seeded at startup.
- **Check:** Read the exception (`UNIQUE constraint failed: Sources.Key`) and the failing assertion (expected 1 polled source, got 2).
- **Result:** Both tests assumed an empty source table. Product code was correct. A source row without a registered adapter is polled and reports "No adapter is registered" in its health, which is the intended behaviour.
- **Decision:** Fixed the tests, not the code.

## R-012 – Mutation checks on the USGS adapter

- **Check:** Five deliberate bugs (age limit removed, wrong shape returns empty, severity hard-coded, HTTP errors ignored, depth read from the wrong coordinate).
- **Result:** All five caught (see `evidence/004-usgs-and-slack-verification.md`).
- **Also done:** an opt-in live test (`EAM_LIVE_TESTS=1`) runs the production code path against the real service. It returned 36 events with the strongest at M6.1, matching the saved fixture.

## R-013 – Filters sent to the wrong category were silently dropped (design risk, caught before it shipped)

- **AI output:** The first version of the filter parser ignored unknown JSON properties, which is the default.
- **Check:** Thought through what a mistaken request would do, then wrote the test first.
- **Result:** `{"tickers":["BTC"]}` sent to a *Disasters* rule would parse into an empty disaster filter and the rule would then match every earthquake. A typo such as `includeKeyword` would do the same to a news rule. Both are silent and noisy, the worst combination.
- **Decision:** Unknown properties are now rejected (`UnmappedMemberHandling.Disallow`). The matcher treats a stored rule that fails to parse as "never matches", never as "matches everything". Tests at three levels; removing the setting makes them fail (mutation A12).

## R-014 – Channel type names were compared three different ways (found by a test)

- **Observation:** The test for "admin switches a channel type off" failed: a send still went out.
- **Cause:** The switch stored `fake`; the channel registry and ingestion compared names ignoring case, but the dispatcher's database query compared exactly (SQLite's default). New matches were suppressed while already queued ones were still sent.
- **Decision:** The dispatcher now compares in memory ignoring case, like the other two. The admin endpoint also only accepts registered types and stores their canonical spelling, so the problem cannot be created through the API either.

## R-015 – Compiles, passes review, fails at the first request (AI pattern)

- **AI output:** DTOs written as C# records with validation attributes written as `[property: Required]`.
- **Check:** First real request to the sign-in endpoint returned 500 with a generic body (the error handler correctly hides details). The server log showed: validation metadata on a record must be on the constructor *parameter*.
- **Result:** Every endpoint taking a body would have failed. The build was clean; unit tests of the controllers' logic would not have caught it, only a real HTTP request does.
- **Decision:** Attribute targets corrected. Lesson recorded: after generating web code, make a real request and read the log. Hiding errors from clients is right, but it makes the log the first place to look.

## R-016 – Settings were read before the final configuration existed

- **AI output:** `Program.cs` read the authentication settings eagerly at the top of the file.
- **Check:** Every API test failed at start-up, even though the test host provided the settings.
- **Result:** Configuration added by the hosting environment after that line (test host, environment variables injected later) was missed. In production this would look like "my setting is ignored".
- **Decision:** Settings are now bound as options, resolved lazily, and validated when the host starts (so a missing key still stops the application immediately). A test checks that it refuses to start with a short key.

## R-017 – A failing test caused by shared state, not by the product

- **Observation:** The "failed delivery can be retried" test could not find its failed notification.
- **Cause:** Scripted results were consumed in queue order. Earlier tests in the same class leave undelivered notifications behind, and the dispatcher handles the oldest first, so the scripted failure hit a leftover from another test.
- **Decision:** Scripted results are now matched by event title instead of by position. Product code unchanged. Three runs in a row green.

## R-018 – Security and authorization mutation checks, and a real end-to-end run

- **Check:** 14 deliberate bugs in ownership checks, role requirements, token validation, masking, sanitizing, rate limiting, filter strictness and retry rules (`evidence/005-api-verification.md`).
- **Result:** All 14 caught.
- **Real run:** Started the actual application with its background workers, a real SMTP server and real HTTP calls: created a channel and a rule, injected three events (strong quake in Japan, weak quake in Japan, strong quake in Chile). Exactly one email was delivered, the user's history shows it as sent. The live USGS poller stored 4 real events by itself. A request to register `https://169.254.169.254/...` as a Slack webhook was refused. No errors in the server log.

## R-019 – Email channel can be used to send mail to anyone (OPEN, not fixed)

- **Observation:** While writing the channel test endpoint: a user can register *any* address as an email channel, press "test" (limited to once per 10 seconds) and create rules that send alerts to it. Nobody confirms they own the address.
- **Risk:** The system can be used to send unwanted mail to third parties, and it is our mail server's reputation on the line. This is a real abuse path, not a theoretical one.
- **Mitigations in place:** per-user limits (20 channels, 50 rules), test rate limit, cooldown per rule, simulated events marked as such, admin kill switch per channel type.
- **Not done, and the proper fix:** send a confirmation link to a new address and only enable the channel once it is clicked (double opt-in). That needs a token table and one more endpoint. Recommended before any real deployment; left out of this scope and listed in the README.

## R-020 – The first generated API types made everything optional

- **AI output:** TypeScript types generated from the OpenAPI description came out with every field optional (`id?: string`) and enum names with full namespaces.
- **Check:** Read the generated types before using them.
- **Result:** Optional everywhere would force `!` or checks on every field, and hide real mistakes (a missing field would compile). The cause was in the API's description, not in the generator: non-nullable fields were not marked required.
- **Decision:** Fixed at the source: the API now marks non-nullable properties as required and uses short schema names. Regenerated: `id: string`, `lastNotifiedAt?: string | null`, enums as string unions. `tools/export-openapi.sh` makes the export repeatable.

## R-021 – "Pinned" was not really pinned

- **Observation:** The scaffold was created with Angular CLI 22.2.7 (chosen deliberately, see ADR-017), but `npm ls` showed 22.3.0 packages, a release from the same day.
- **Cause:** The generated `package.json` used `^` ranges. Installing single packages one at a time also failed with version conflicts, and the type generator refused to install next to TypeScript 6.
- **Decision:** All Angular packages set to the exact same version in one edit, lock file regenerated. The generator (supports TypeScript 5 only) runs through `npx`. `--force` was rejected because it would hide a real incompatibility.

## R-022 – A confirmation message that did not say what it confirmed (found by the browser test)

- **Observation:** The browser test read "1 alert queued" for an event that should have created none.
- **Cause:** The message from the previous test event was still on screen. The app never said which event it referred to. A real person creating two test events in a row could misread it the same way.
- **Decision:** Product fix, not a test workaround: the message now names the event.

## R-023 – A screen that never updated by itself (found by the browser test)

- **Observation:** The browser waited 20 seconds for a delivery to show as "Sent" and gave up, although the server had delivered it within a second.
- **Cause:** The deliveries screen loaded once. An administrator watching an alert get delivered would see it stuck on "Pending" until reloading, on the one screen where freshness matters most.
- **Decision:** Deliveries refresh every 5 seconds without flicker (an opened row stays open) and have a Refresh button. History refreshes every 10 seconds.

## R-024 – Not a bug: I had not started the API (my mistake)

- **Observation:** Sign-in returned "The server had a problem" and the journey failed, once with a server-looking message. The API log had no error.
- **Check:** Asked what else could produce a 5xx: curl to the API returned nothing, no API process was running, and the dev server's log showed `ECONNREFUSED` from its proxy. The API log said `command not found`: I had written an environment variable name containing dots in front of the start command, which the shell treats as a command.
- **Result:** The browser and the app were fine; the backend was simply not running. Time spent here would have been wasted on a phantom bug if the proxy log and process list had not been checked first.
- **Decision:** Lesson recorded: when a 5xx is not in the API log, check that the API is alive and what sits in between before suspecting the code. Launch lines for the stack are now in `e2e/README.md`.

## R-025 – The rate limiter worked, and the screen explained it

- **Observation:** Running the journey twice within a minute failed at sign-in with "Too many requests".
- **Check:** Looked at the failure screenshot instead of assuming.
- **Result:** The limit (10 sign-ins per minute) did its job and the message was clear (`evidence/e2e/rate-limit-message.png`). One journey legitimately signs in several times.
- **Decision:** The E2E stack starts the API with a higher limit (documented). The default stays at 10.

## R-026 – One unexplained connection error in the server log (OPEN, no impact found)

- **Observation:** In some runs the API log shows exactly one line, `An error occurred using the connection to database`, in the first seconds after start, when real traffic and the first background poll overlap. With connection-level logging the failed open is followed immediately by a successful one.
- **Checks:** Could not reproduce without traffic (3 starts), with 3 consecutive full browser journeys the only error line was that one, and the browser test fails on any 5xx response, so none occurred. A stress test fired 40 parallel sign-ins at the moment of startup plus 3 more rounds of 40: all 160 returned 200, no error lines, 160 users created.
- **Action taken:** Write-ahead logging is switched on for the database (the usual remedy when several writers share one SQLite file). This is hardening, not a proven fix, since the cause was not found.
- **Status:** Open. No lost request or wrong result was ever observed. If it appeared in production the next step is to attach an interceptor that logs the actual exception.

## R-027 – Frontend mutation checks, and a test-writing trap

- **Check:** 10 deliberate bugs in the interceptor, guards, session handling, filter building, unit conversion, list parsing and error wording (`evidence/006-frontend-verification.md`).
- **Result:** All 10 caught.
- **Trap:** Component tests that changed a plain property on a host component did not re-render under Angular's zoneless change detection and failed, although the component was right. Rewritten to drive the component's inputs directly.

## R-028 – Found by looking at the screens, not by any test

- **Observation:** The rule preview read "Of the last 10 natural disasters events".
- **Decision:** Replaced the label with a proper noun phrase per category. Lesson: passing assertions say nothing about wording, so every screenshot was opened and read.

## R-029 – The extensibility claim, checked as a diff

- **Claim (ADR-004, ARCHITECTURE section 6):** a new channel is one class and one registration, and the core does not change.
- **Check:** Built the third channel (generic webhook) last, on purpose, then listed exactly what changed (`evidence/007-third-channel.md`).
- **Result:** Three new production files (the channel, an address policy, a connection guard) and **one added line** in the composition root. No change in Core, ingestion, dispatcher, persistence, the API, the OpenAPI document or the Angular app. The API offered the new type to users and to admins by itself, and the existing screen accepted its settings through the generic JSON box in a real browser.
- **Decision:** Claim accepted, with evidence. The remaining rough edge is user experience (a JSON box instead of two fields), not structure.

## R-030 – "Is this address public?" is easy to get subtly wrong

- **AI output:** The first policy was written from memory of the usual ranges.
- **Check:** Compared every address in a table against Python's `ipaddress` module as an independent opinion.
- **Result:** They agreed on everything except two cases where **Python is the unsafe one**: it calls multicast (`224.0.0.1`) and a NAT64 address that embeds `127.0.0.1` (`64:ff9b::7f00:1`) "global". A policy copied from a standard-library helper would have let the NAT64 form through. The policy was made stricter (IPv6 allow list, reserved blocks removed) and both cases are tests.
- **Second finding:** checking a host name only when the settings are saved is not enough, because the name can later answer with an internal address (DNS rebinding). The decisive check moved to the moment of connecting and is tested with real sockets: a server on the loopback address never sees a connection when the name resolves to it, including when public and internal addresses are mixed.

## R-031 – Not verified: delivery over real TLS to a public server (OPEN)

- **What is verified:** signing against the RFC 4231 test vector and an independent Python calculation; the request format and failure handling against a stub; the address guard with real sockets (blocked cases and the allowed case, including the original host name in the request); the whole ingestion-to-delivery path with only the wire replaced.
- **What is not:** a real HTTPS request to a public server. This environment's network goes through a mandatory proxy, and the channel deliberately does not use proxies (ADR-019). An early idea to test through a direct connection was dropped, because bypassing the proxy is not allowed here, and I did not call third-party echo services without being asked to.
- **Status:** Open. Anyone with a public test endpoint can run one real send from the Channels screen ("Send test") and look at the signature on the receiving side.

## R-032 – Small things the build and a screenshot caught

- A .NET 9 method (`Convert.ToHexStringLower`) was used in a .NET 8 project. Caught by the compiler.
- Mutation checks: 16 deliberate bugs in the policy, the guard, URL checks, signing, status handling and handler settings. All 16 caught (`evidence/007-third-channel.md`).
- The handler settings (no redirects, no proxy, guard attached) were first untested, so a wrong setting would have passed everything. Moved into a method and pinned by tests; mutation W10 and W15 then fail as they should.
- My browser script assumed the form still had its previous type selected (script error, not an app error).
- A screenshot showed the JSON box still holding the secret after a successful add. Fixed in the next commit.
