# API verification

## Automated suite

240 passing, 5 opt-in tests skipped (real SMTP server, live USGS). Three consecutive runs identical.

API tests use the real application (`WebApplicationFactory`) with a throwaway SQLite file, no background workers, a capturing email transport and a scripted fake channel.

## Mutation checks (one deliberate bug at a time, full suite after each)

| # | Mutation | Result |
|---|---|---|
| A1 | Channel ownership check removed | Caught |
| A2 | Rule may use another user's channel | Caught |
| A3 | Rule ownership check removed | Caught (3 tests) |
| A4 | Notification history not scoped to the user | Caught (3 tests) |
| A5 | Admin role requirement removed from the admin controller | Caught by the route sweep |
| A6 | Token audience not validated | Caught |
| A7 | Token lifetime not validated | Caught |
| A8 | Channel settings returned unmasked | Caught (2 tests) |
| A9 | "Channel in use" delete check removed | Caught |
| A10 | Channel test error text not sanitized | Caught |
| A11 | Sign-in rate limit removed | Caught |
| A12 | Strict filter parsing removed | Caught (4 tests) |
| A13 | Admin key comparison always true | Caught (2 tests) |
| A14 | Retry allowed for any status | Caught (2 tests) |

## Real end-to-end run (application, workers, real SMTP server, real HTTP)

1. Anonymous call to `/api/rules` returned 401.
2. User signed in (email in mixed case, normalised), created an Email channel (shown as `r***@example.com`) and a rule: Disasters, magnitude 6 or more, region "Japan".
3. Admin injected three events: M6.8 Sendai Japan, M5.1 Tokyo Japan, M6.9 Chile.
4. Only the first created a notification. The background dispatcher delivered it. The SMTP server received one message (`005-e2e-api-email.eml`). History shows `Sent`, 1 attempt.
5. USGS poller ran by itself: source `Healthy`, 4 real events stored.
6. Registering `https://169.254.169.254/services/a/b/c` as a Slack webhook was refused with a clear message.
7. Server log: no errors.

## OpenAPI

`docs/openapi.json` exported from the running application (24 paths, 33 schemas). Input for the Angular client generator.
