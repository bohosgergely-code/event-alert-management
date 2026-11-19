# USGS and Slack verification

## USGS earthquake feed (real response)

Captured: `fixtures/usgs/2.5_day.raw.geojson` (36 features, feed API version 2.8.2).

Checked with an independent Python script (not the C# code):

| Fact | Value |
|---|---|
| Features | 36, all with unique ids |
| Null values in mag, place, time, updated, url, type, status, title | none in this sample (the parser still handles a null magnitude) |
| `time` differs from `updated` | 36 of 36 |
| Status | 27 reviewed, 9 automatic |
| Event type | all `earthquake` |
| Magnitude range | 2.48 to 6.1 |
| Events within 1, 3, 24 hours of the feed's generation time | 2, 4, 36 |
| Severity distribution with the starting table | severity 1: 22, severity 2: 13, severity 3: 1 |
| Magnitude 5.0 or higher | 7 |
| Tsunami flag set | 0 |

The C# tests assert exactly these numbers.

Live check (production code path, real network): 36 events, strongest M6.1.

## Slack documentation checked

| Source page | Confirmed |
|---|---|
| Incoming webhooks guide | `POST` JSON with `text`; success is HTTP 200 with plain text `ok`; error list: `action_prohibited`, `channel_is_archived`, `invalid_payload`, `invalid_token`, `no_active_hooks`, `no_service`, `no_service_id`, `no_team`, `no_text`, `posting_to_general_channel_denied` |
| Changelog "Changes to errors for incoming webhooks" | 400 `invalid_payload`, `user_not_found`; 403 `action_prohibited`; 404 `channel_not_found`; 410 `channel_is_archived`; 500 `rollup_error` ("likely not your fault") |
| Rate limits page | Incoming webhooks: 1 per second, short bursts allowed; HTTP 429 with `Retry-After` (seconds) applies to incoming webhooks too |
| Formatting message text page | Escape `&` as `&amp;`, `<` as `&lt;`, `>` as `&gt;`; do not encode the rest of the text |

Not done: posting to a real webhook (no workspace available in this environment).

## USGS adapter mutation checks

| # | Mutation | Result |
|---|---|---|
| M16 | Age limit removed | Caught (2 tests) |
| M17 | Unexpected document returns empty list instead of error | Caught (4 tests) |
| M18 | Severity hard-coded | Caught (3 tests) |
| M19 | HTTP error status ignored | Caught (2 tests) |
| M20 | Depth read from the wrong coordinate | Caught (1 test) |
