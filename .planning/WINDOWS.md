---
schema_version: 1
open_count: 5
waived_count: 0
fixed_count: 2
total_count: 7
last_updated: 2026-09-22T06:14:40.884Z
---

# Broken Windows Ledger

> Cross-phase defect register. With `workflow.windows_enforce` enabled, `/gsd-ship` blocks while `open_count > 0`.
> Waive with `gsd-tools windows waive <id> "<reason>"` (reason required).
> Mark fixed with `gsd-tools windows fixed <id>`.

| id | phase | kind | file | line | description | status | reason | recorded_at | resolved_at |
|----|-------|------|------|------|-------------|--------|--------|-------------|-------------|
| 1 | 04 | deviation | tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthControllerTests.cs |  | Fixed text-based file round-trip that corrupted SQLite binary; switched to ReadAllBytes/WriteAllBytes with a pool clear (outside 04-01's files_modified, human-approved) | fixed |  | 2026-09-20T19:41:48.225Z | 2026-09-20T19:42:06.043Z |
| 2 | 04 | deviation | tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyLoginMethodUsersTests.cs |  | Reordered construct-then-corrupt so the single-log-entry regression assertion still holds under eager construction-time SQLite initialization (outside 04-01's files_modified) | fixed |  | 2026-09-20T19:41:48.345Z | 2026-09-20T19:42:06.156Z |
| 3 | 04 | deviation | e2e/60-concurrent-logins.bats | 89 | The second bella burst tolerates 200 or 401 instead of requiring all 200, because JellyfinSecurity's TwoFactorAuthProvider rate-limits app-password attempts per IP and refuses one of five concurrent logins. The 500 guard and the exactly-one-account invariant stay strict. Tighten this back if JellyfinSecurity leaves the e2e stack. | open |  | 2026-09-20T23:07:41.001Z |  |
| 4 | 04 | todo | src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs | 61 | WR-01 (04-REVIEW.md): GetStatusAsync captures the cache-validity timestamp before the refresh call, so the effective snapshot lifetime is shorter than the documented 60s by the duration of the Emby request. Bounded today by the 5s HTTP timeout. | open |  | 2026-09-20T23:07:53.740Z |  |
| 5 | 04 | todo | src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs | 100 | WR-02 (04-REVIEW.md): Dispose() disposes the refresh semaphore with no coordination against in-flight GetStatusAsync callers. A caller unblocked from WaitAsync after disposal throws ObjectDisposedException on Release(), which Jellyfin turns into an HTTP 500 rather than a refusal. Shutdown-only. | open |  | 2026-09-20T23:07:53.860Z |  |
| 6 | 04 | todo | src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs | 163 | WR-04 (04-REVIEW.md): EnsureInitialized catches only SqliteException, IOException and UnauthorizedAccessException. It runs inside DI singleton construction, so another exception such as ArgumentException from a malformed path takes down plugin registration instead of degrading as designed. | open |  | 2026-09-20T23:07:53.975Z |  |
| 7 | 06 | deviation | CLAUDE.md |  | Task 2's automated acceptance check 'rg -c Jellyfin.Controller CLAUDE.md >= 2' cannot pass against the required one-line bullet, because rg -c counts matching lines not occurrences; substantive intent (3 occurrences) confirmed via rg -o \| wc -l | open |  | 2026-09-22T06:14:40.884Z |  |

````json
[
  {
    "id": 1,
    "kind": "deviation",
    "phase": "04",
    "file": "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthControllerTests.cs",
    "line": null,
    "description": "Fixed text-based file round-trip that corrupted SQLite binary; switched to ReadAllBytes/WriteAllBytes with a pool clear (outside 04-01's files_modified, human-approved)",
    "status": "fixed",
    "reason": "",
    "recorded_at": "2026-09-20T19:41:48.225Z",
    "resolved_at": "2026-09-20T19:42:06.043Z"
  },
  {
    "id": 2,
    "kind": "deviation",
    "phase": "04",
    "file": "tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyLoginMethodUsersTests.cs",
    "line": null,
    "description": "Reordered construct-then-corrupt so the single-log-entry regression assertion still holds under eager construction-time SQLite initialization (outside 04-01's files_modified)",
    "status": "fixed",
    "reason": "",
    "recorded_at": "2026-09-20T19:41:48.345Z",
    "resolved_at": "2026-09-20T19:42:06.156Z"
  },
  {
    "id": 3,
    "kind": "deviation",
    "phase": "04",
    "file": "e2e/60-concurrent-logins.bats",
    "line": 89,
    "description": "The second bella burst tolerates 200 or 401 instead of requiring all 200, because JellyfinSecurity's TwoFactorAuthProvider rate-limits app-password attempts per IP and refuses one of five concurrent logins. The 500 guard and the exactly-one-account invariant stay strict. Tighten this back if JellyfinSecurity leaves the e2e stack.",
    "status": "open",
    "reason": "",
    "recorded_at": "2026-09-20T23:07:41.001Z",
    "resolved_at": null
  },
  {
    "id": 4,
    "kind": "todo",
    "phase": "04",
    "file": "src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs",
    "line": 61,
    "description": "WR-01 (04-REVIEW.md): GetStatusAsync captures the cache-validity timestamp before the refresh call, so the effective snapshot lifetime is shorter than the documented 60s by the duration of the Emby request. Bounded today by the 5s HTTP timeout.",
    "status": "open",
    "reason": "",
    "recorded_at": "2026-09-20T23:07:53.740Z",
    "resolved_at": null
  },
  {
    "id": 5,
    "kind": "todo",
    "phase": "04",
    "file": "src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs",
    "line": 100,
    "description": "WR-02 (04-REVIEW.md): Dispose() disposes the refresh semaphore with no coordination against in-flight GetStatusAsync callers. A caller unblocked from WaitAsync after disposal throws ObjectDisposedException on Release(), which Jellyfin turns into an HTTP 500 rather than a refusal. Shutdown-only.",
    "status": "open",
    "reason": "",
    "recorded_at": "2026-09-20T23:07:53.860Z",
    "resolved_at": null
  },
  {
    "id": 6,
    "kind": "todo",
    "phase": "04",
    "file": "src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs",
    "line": 163,
    "description": "WR-04 (04-REVIEW.md): EnsureInitialized catches only SqliteException, IOException and UnauthorizedAccessException. It runs inside DI singleton construction, so another exception such as ArgumentException from a malformed path takes down plugin registration instead of degrading as designed.",
    "status": "open",
    "reason": "",
    "recorded_at": "2026-09-20T23:07:53.975Z",
    "resolved_at": null
  },
  {
    "id": 7,
    "kind": "deviation",
    "phase": "06",
    "file": "CLAUDE.md",
    "line": null,
    "description": "Task 2's automated acceptance check 'rg -c Jellyfin.Controller CLAUDE.md >= 2' cannot pass against the required one-line bullet, because rg -c counts matching lines not occurrences; substantive intent (3 occurrences) confirmed via rg -o | wc -l",
    "status": "open",
    "reason": "",
    "recorded_at": "2026-09-22T06:14:40.884Z",
    "resolved_at": null
  }
]
````
