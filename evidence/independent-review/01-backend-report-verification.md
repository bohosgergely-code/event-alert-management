# Independent review, part 1: backend pipeline (reproduced before any fix)

A fresh-context reviewer (given only the specification documents and the code, with the authors' own assessments and the git history removed from its copy) reported 12 findings on the backend pipeline. Before changing anything, each claim was reproduced with a separate probe written independently of the reviewer's tests. Results, on the code as it stood at commit `635209e`:

| # | Reviewer's claim | Result of my own probe |
|---|---|---|
| 1 | A rule cooldown suppresses the newer, more severe event in the same batch | **Reproduced.** Batch of "minor thing" (severity 1, 11:00) and "CATASTROPHE" (severity 5, 11:05), cooldown 3600 s: minor = Pending, CATASTROPHE = Suppressed ("Rule cooldown until 13:00") |
| 2a | Disabling a rule does not stop queued notifications | **Reproduced.** The queued message was still sent |
| 2b | Deleting a rule erases its delivery history | **Reproduced.** After deleting the rule: notifications 0, attempts 0 (the event remains) |
| 3 | Switching a channel type off turns queued work into permanent `Failed` | **Reproduced.** Status Failed, no next attempt |
| 4 | Concurrent ingestion of the same item aborts one of the two | **Reproduced, and worse than reported:** 19 of 20 rounds had one of two parallel ingests throw |
| 5 | Deleting a rule while a send is in flight aborts the dispatcher | **Reproduced.** `DbUpdateException` out of the dispatcher run |
| 8 | Manual retry creates duplicate attempt numbers | **Reproduced.** Attempts `#1:PermanentFailure, #1:Success` |

Not yet reproduced (read-only reasoning by the reviewer, checked against the code afterwards): findings 6 (late and revised USGS events), 7 (no retention), 9, 10, 11, 12 (substring keyword matching).

The probe source was a throwaway file and is not kept. Each reproduced finding becomes a permanent regression test together with its fix.
