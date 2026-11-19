# Third channel: generic webhook

## The extensibility claim as a diff

What changed in the repository for the third channel (production code only):

| File | Change |
|---|---|
| `src/EventAlertManagement.Infrastructure/Channels/WebhookChannel.cs` | new, 186 lines |
| `src/EventAlertManagement.Infrastructure/Security/PublicAddressPolicy.cs` | new, 56 lines |
| `src/EventAlertManagement.Infrastructure/Security/SsrfGuard.cs` | new, 58 lines |
| `src/EventAlertManagement.Infrastructure/Hosting/ServiceCollectionExtensions.cs` | **+1 line**: `services.AddWebhookChannel(config);` |

Checked with `git diff --name-only HEAD` over these paths, which printed nothing: `src/EventAlertManagement.Core`, `Infrastructure/Pipeline`, `Infrastructure/Dispatch`, `Infrastructure/Persistence`, `src/EventAlertManagement.Api`, `src/EventAlertManagement.Web`, `docs/openapi.json`.

So the channel contract, matching, ingestion, outbox, dispatcher, retry rules, API and Angular app were not touched. The API listed the new type for users (`/api/channels/types`) and admins (`/api/admin/channel-types`, including the global off switch) without any change, and the existing Angular screen accepted its settings through the generic JSON box (`evidence/generic-ui-proof/webhook-internal-refused.png`, `evidence/generic-ui-proof/webhook-added.png`, kept as a fixed copy because a later commit replaces the JSON box with a proper form).

## Tests

382 passing (5 opt-in skipped). New: address policy table (49 cases), URL checks (about 45 forms), signing, request format, failure classification, handler settings, real-socket guard tests, the unchanged pipeline delivering through the new channel, and the API surface.

Independent oracles used: RFC 4231 HMAC test vector, an HMAC computed in Python for a fixed input, and Python's `ipaddress` module for the address table (see REVIEW_LOG R-030 for where it was wrong).

How each dangerous URL form was refused (probe output):

| URL | What the check saw | Reason |
|---|---|---|
| `https://2130706433/` | host `127.0.0.1` (IPv4) | internal address |
| `https://0x7f.0.0.1/` | host `127.0.0.1` (IPv4) | internal address |
| `https://127.1/` | host `127.0.0.1` (IPv4) | internal address |
| `https://0177.0.0.1/` | host `127.0.0.1` (IPv4) | internal address |
| `https://[::ffff:127.0.0.1]/` | IPv6 literal | internal address (IPv4 inside IPv6) |
| `https://example.com@127.0.0.1/` | host `127.0.0.1` | credentials in the URL |
| `https://LOCALHOST./x` | `localhost.` (name) | internal name |
| `https://intranet/` | single-label name | internal name |

## Mutation checks (one deliberate bug at a time, full suite after each)

| # | Mutation | Result |
|---|---|---|
| W1 | Cloud metadata range (169.254/16) allowed | Caught |
| W2 | 10.0.0.0/8 allowed | Caught |
| W3 | IPv4 inside IPv6 not unwrapped | Caught |
| W4 | One internal address among public ones passes | Caught |
| W5 | Connect-time check skipped | Caught (real sockets) |
| W6 | http accepted | Caught |
| W7 | Credentials in the URL accepted | Caught |
| W8 | IP literals not checked | Caught (12 tests) |
| W9 | Timestamp left out of the signature | Caught |
| W10 | Redirects followed | Caught |
| W11 | HTTP 429 treated as permanent | Caught |
| W12 | Blocked address treated as retryable | Caught |
| W13 | Short signing secrets accepted | Caught |
| W14 | Masked text reveals the path instead of the host | Caught |
| W15 | System proxy used by default | Caught |
| W16 | Single-label internal names accepted | Caught |

## Real browser

The journey now also: selects the new type in the existing screen, is refused for a webhook aimed at `169.254.169.254` with a clear message, adds a valid one, and sees it masked (`https://hooks.example.com/… (signed)`) with neither the path token nor the secret visible. Passed twice in a row.
