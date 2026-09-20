---
schema_version: 1
open_count: 0
waived_count: 0
fixed_count: 2
total_count: 2
last_updated: 2026-09-20T19:42:06.156Z
---

# Broken Windows Ledger

> Cross-phase defect register. With `workflow.windows_enforce` enabled, `/gsd-ship` blocks while `open_count > 0`.
> Waive with `gsd-tools windows waive <id> "<reason>"` (reason required).
> Mark fixed with `gsd-tools windows fixed <id>`.

| id | phase | kind | file | line | description | status | reason | recorded_at | resolved_at |
|----|-------|------|------|------|-------------|--------|--------|-------------|-------------|
| 1 | 04 | deviation | tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthControllerTests.cs |  | Fixed text-based file round-trip that corrupted SQLite binary; switched to ReadAllBytes/WriteAllBytes with a pool clear (outside 04-01's files_modified, human-approved) | fixed |  | 2026-09-20T19:41:48.225Z | 2026-09-20T19:42:06.043Z |
| 2 | 04 | deviation | tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyLoginMethodUsersTests.cs |  | Reordered construct-then-corrupt so the single-log-entry regression assertion still holds under eager construction-time SQLite initialization (outside 04-01's files_modified) | fixed |  | 2026-09-20T19:41:48.345Z | 2026-09-20T19:42:06.156Z |

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
  }
]
````
