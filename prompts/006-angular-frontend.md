# 006 – Angular frontend

**Role:** Implementer, then reviewer of its own output through a real browser.

## Prompt

There is no need for user e-mail validation at this point. Continue with the Angular frontend.

## Response (summary)

- Scaffolded Angular 22 (pinned), generated API types from the OpenAPI description, built the user screens (sign-in, channels, rules with presets and preview, history) and the admin screens (overview, deliveries with attempts and retry, events with test event creation, sources, channel types).
- 60 unit and component tests, plus a real-browser journey against the real stack.

## What I checked

- Mutation checks on the frontend logic: 10 of 10 caught.
- Ran a real browser through the whole product three times in a row. Opened every screenshot and read it.
- Found and fixed product problems that only the browser journey exposed (R-022 stale confirmation, R-023 screen that never refreshed, R-028 wording).
- Found two problems that were mine, not the app's (R-024 API not started, R-021 versions not really pinned), and one that was the rate limiter doing its job (R-025).
- Left one unexplained, harmless log line open (R-026).

## Decision

Email verification was dropped from scope at the author's request (R-019 stays listed as a known limit in the README). Frontend accepted as the user and admin interface.

## Notes from the AI

- The generated API types were unusable until the API itself was fixed to say which fields are always present (R-020).
- Angular 22 and TypeScript 6 are newer than the AI's own knowledge. Conventions were read from the generated scaffold and checked by building, not assumed.
- Test infrastructure traps (zoneless change detection, stale shared state) cost more time than the application code did.
