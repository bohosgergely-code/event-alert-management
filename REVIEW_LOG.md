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

## R-008 – Slack behaviour is from model knowledge, not verified (OPEN)

- **AI claim:** Slack webhooks answer 200 with body `ok`; 404 and 410 mean the webhook or channel is gone (permanent); 400 means invalid payload; 429 means rate limited (transient); `<`, `>` and `&` are the characters to escape in message text.
- **Check:** Not yet done against Slack documentation or a real webhook. Unit tests only prove the code does what the code assumes.
- **Decision:** Still open. To do: read the official documentation and send one real message to a test webhook. If no workspace is available, state clearly that Slack was only tested against a stub.

## R-009 – SMTP transport has never talked to a real server (OPEN)

- **Observation:** `MailKitEmailTransport` (connection, status-code classification) has no automated test. Email rendering and validation are tested, the actual SMTP exchange is not.
- **Decision:** Still open. To do: run Mailpit, send a real alert, capture a screenshot as evidence, and test the failure path with a closed port.
