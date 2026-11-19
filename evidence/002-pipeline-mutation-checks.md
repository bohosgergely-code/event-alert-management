# Pipeline and security mutation checks

One deliberate bug at a time, full suite after each, then restored. Baseline: 141 tests passing.

| # | Mutation | Result |
|---|---|---|
| M5 | Dispatcher claim always succeeds (no atomic check) | Caught: concurrent dispatcher test |
| M6 | Ingestion no longer skips already stored events | Caught: same event twice test |
| M7 | Cooldown ignored | Caught: cooldown test |
| M8 | Transient failures treated as permanent | Caught: 3 tests |
| M9 | Error sanitizer removed from stored attempt errors | Caught: sanitizer test |
| M10 | Slack host check removed | Caught: 5 tests |
| M11 | Slack text escaping removed | Caught |
| M12 | Email link scheme check removed | Caught: 2 tests |
| M13 | HTML encoding removed from email body | Caught |
| M14 | Explicit invalid-character check on email address removed | **Survived** (redundant, see R-007) |
| M14b | Also removed the address round-trip equality check | Caught: `Name <a@b.com>` case |
| M15 | HTTP 429 treated as permanent | Caught |

Earlier matcher mutations (M1 to M4) are in `001-matcher-mutation-check.md`.
