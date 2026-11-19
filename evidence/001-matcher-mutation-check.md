# Matcher mutation check

Goal: confirm the unit tests actually detect broken matching logic, instead of passing trivially.

Method: one deliberate change at a time in `EventMatcher.cs`, run the whole suite, then restore the file.

| Mutation | Result |
|---|---|
| Severity comparison `<` changed to `<=` | 1 test failed (caught) |
| Magnitude comparison `<` changed to `<=` | 1 test failed (caught) |
| Exclude-keyword check removed | 1 test failed (caught) |
| Corrupt rule JSON returns "match" instead of "no match" | 1 test failed (caught) |
| Original restored | 60 passed |

Baseline: 60 passed, 0 failed. Tests were written from the filter specification in `docs/ARCHITECTURE.md`, before looking at matcher internals.
