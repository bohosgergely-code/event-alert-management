# Frontend verification

## Automated

60 unit and component tests (Vitest), production build clean, initial download 82 kB (everything else loads when first visited).

## Mutation checks (one deliberate bug at a time, full suite after each)

| # | Mutation | Result |
|---|---|---|
| F1 | Token also sent to addresses outside the API | Caught |
| F2 | A refused sign-in also signs the user out | Caught |
| F3 | Admin guard lets normal users in | Caught |
| F4 | Expired sessions still count as signed in | Caught |
| F5 | Filters of another category leak into the request | Caught (several tests) |
| F6 | Cooldown minutes not converted to seconds | Caught |
| F7 | Keyword lists not de-duplicated | Caught |
| F8 | Server error bodies shown to the user | Caught |
| F9 | A magnitude of zero treated as empty | Caught |
| F10 | Sign-out keeps the stored session | Caught |

## Real browser journey (`e2e/journey.cjs`)

Chromium against the real stack: API with background workers, a real SMTP server, Angular dev server. Three consecutive runs passed. 14 screenshots in `evidence/e2e/`.

What it proves, step by step:

1. An anonymous visitor is sent to sign-in; the admin area is closed to them.
2. A user signs in (the address is normalised, upper case works) and sees a helpful empty state.
3. A webhook aimed at an internal address (`169.254.169.254`) is refused with a clear message.
4. An email channel is added and shown masked (`i***@example.com`); "Send test" delivers a real message.
5. A rule is built from a preset, previewed, and created. An incomplete rule is explained before any request is sent.
6. A wrong admin key is refused and the field is cleared; the right key is accepted. An administrator cannot open user pages.
7. Three test events: strong quake in Japan creates an alert, weak quake in Japan does not, strong quake in Chile creates one for a different rule.
8. Deliveries: the Japan alert is Sent, the one to an address the mail server refuses is Failed (permanent, no URL in the message), retry is offered and confirms. The email reached the SMTP server.
9. Switching Email off suppresses the next alert with a visible reason; switching it on again restores delivery.
10. The user's history shows sent and suppressed alerts with the reason. The session survives a reload and is gone after sign-out; the back button does not bring it back.
11. Sign-in fits a 375 px phone screen without horizontal scrolling.
12. The browser logs no script errors and the server returns no 5xx during the whole journey.

Also kept: `evidence/e2e/rate-limit-message.png`, the sign-in screen showing the rate limiter's message.
