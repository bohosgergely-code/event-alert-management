# EventAlertManagement.Web

Angular 22 single-page application for users and administrators. It talks only to the API in `../EventAlertManagement.Api`.

## Commands

```bash
npm install            # once
npm start              # dev server on http://localhost:4200, /api is proxied to http://127.0.0.1:5080
npm test               # unit tests (Vitest), runs once with: npm run test:ci
npm run build          # production build into dist/
npm run generate:api   # rebuild src/app/core/api-schema.ts from ../../docs/openapi.json
```

Start the API first (`dotnet run --project ../EventAlertManagement.Api`). When the API changes, export its description with `bash ../../tools/export-openapi.sh` and then run `npm run generate:api`. A compile error after that means the screens no longer fit the API, which is the point.

## Layout

| Folder | Purpose |
|---|---|
| `src/app/core` | API client, sign-in session, HTTP interceptor, route guards, error wording, rule form logic, generated API types |
| `src/app/shared` | Small shared pieces: notices, pager, auto refresh |
| `src/app/features/auth` | User and admin sign-in |
| `src/app/features/channels`, `rules`, `history` | User screens |
| `src/app/features/admin` | Overview, deliveries, events (with test event creation), sources, channel types |

## Decisions that matter

- **Typed contract.** `api-schema.ts` is generated and never edited. Request and response types come from it (`core/models.ts`), so a change in the API shows up as a compile error.
- **Rule form logic is separate from the screen** (`core/rule-form.ts`) and unit tested. It sends only the filters of the chosen category, because the API rejects the rest.
- **Session in `sessionStorage`**, cleared on sign-out and when the tab closes. The token is added only to `/api/*` calls, never to other addresses, and a rejected token signs the user out.
- **New channel types need no frontend change**: unknown types fall back to a raw JSON settings box.
- **Zoneless change detection** (the Angular 22 default): screens keep data in signals so they update when responses arrive.
