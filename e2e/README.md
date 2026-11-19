# End-to-end journey

Drives a real browser through the whole product against the real stack.

Start the stack (three terminals, or background jobs):

```bash
python3 tools/smtp-sink.py 1025 /tmp/smtp-e2e                      # real SMTP server
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5080 \
  ConnectionStrings__Default="Data Source=/tmp/eam-e2e.db" Auth__LoginAttemptsPerMinute=100 \
  dotnet run --project src/EventAlertManagement.Api --no-launch-profile   # API with background workers
cd src/EventAlertManagement.Web && npm start                       # Angular dev server (proxies /api to the API)
```

The sign-in limit is raised because one journey signs in several times (the default of 10 per minute protects the admin key). Use a fresh database file for the first run.

Then run it (needs Playwright with Chromium):

```bash
node e2e/journey.cjs
```

Screenshots are written to `evidence/e2e/`. The script fails if any step does not behave as described, or if the browser logs a console error.
