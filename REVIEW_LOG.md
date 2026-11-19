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
