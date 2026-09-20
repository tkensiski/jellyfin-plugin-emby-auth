---
phase: 04-emby-traffic-under-load-and-failure
plan: 07
subsystem: testing
tags: [bats, e2e, sqlite, migration, json-import, jellyfin-plugin, fingerprint-store]

requires:
  - phase: 04-emby-traffic-under-load-and-failure
    provides: "04-05's one-time legacy-JSON import (EmbyVerifiedPasswords.ImportLegacyRecords, the PRAGMA user_version marker, and the live-verified database and legacy-file paths) and 04-06's jellyfin_log_lines/EMBY_INTERNAL_URL helpers and the elton Emby user reserved for this plan"
provides:
  - "e2e/80-fingerprint-store.bats: two tests proving FPRT-04's end-to-end half -- a user ready to move before the JSON-to-SQLite upgrade is still ready after it, and a later start does not import a second time."
  - "e2e/helpers.bash: jellyfin_stop, jellyfin_start, fingerprint_store_pull, fingerprint_store_push, fingerprint_store_remove, legacy_fingerprint_file_write -- helpers that move the fingerprint store between a stopped Jellyfin container and the host."
  - ".claude/rules/e2e.md: sqlite3 named as the host prerequisite 80-fingerprint-store.bats needs."
affects: [04-08]

actuals:
  tokens: 2171
  tasks: 2
  commits: 2

tech-stack:
  added: []
  patterns:
    - "A throwaway container sharing a stopped service container's volumes via `docker run --rm --volumes-from <id> <image> ...`, reusing an image already pinned elsewhere in the same compose file (nginx:1.30.5-alpine, the emby-proxy image), for a file-removal operation `docker compose exec` cannot do against a stopped container."
    - "Building an e2e JSON fixture from a real record the code under test just wrote, rather than a hand-authored fixture, when the value being fixtured (a SHA-256 of a freshly salted password hash) cannot be reproduced any other way."

key-files:
  created:
    - e2e/80-fingerprint-store.bats
  modified:
    - e2e/helpers.bash
    - .claude/rules/e2e.md

key-decisions:
  - "`docker compose exec` refuses to run against a stopped container (confirmed empirically: `service \"jellyfin\" is not running`), so `fingerprint_store_remove` and the write-ahead-log cleanup half of `fingerprint_store_push` cannot use it the way the plan's prose implied. Both instead run their `rm -f` through a throwaway container started with `docker run --rm --volumes-from <jellyfin_container_id> nginx:1.30.5-alpine sh -c 'rm -f ...'`. `--volumes-from` attaches to a container's declared volumes regardless of whether that container is running -- confirmed empirically against the Jellyfin image's own declared `/config` volume -- and nginx:1.30.5-alpine is the exact tag the emby-proxy service already pulls for this stack, so no new pinned dependency was added."
  - "The local `migration_state` function in 80-fingerprint-store.bats is a fresh copy of 30-migration-modes.bats's `migration_user_state` body under the plan's own chosen name, following the file's existing per-file-ownership convention (two files already carry their own copy) rather than extracting a shared helper."
  - "Both tests build their SQLite/jq intermediate output into files under $BATS_TEST_TMPDIR (legacy-rows.json, legacy-fixture.json, delete.log) rather than command-substituting them onto stdout, so a fingerprint can never appear in the bats output even transiently."

requirements-completed: []  # FPRT-04 is declared by four plans in this phase (04-01, 04-05, 04-07, 04-08). Per the shared-ID gate (#2388), it stays Pending in REQUIREMENTS.md until every declaring plan has a SUMMARY.

coverage:
  - id: D1
    description: "A user who was ready to move before the upgrade (a JSON file, no database) is still ready after a restart that imports it."
    requirement: "FPRT-04"
    verification:
      - kind: e2e
        ref: "bats e2e/80-fingerprint-store.bats#a user ready to move before the upgrade is still ready after it"
        status: pass
    human_judgment: false
  - id: D2
    description: "The import leaves the legacy JSON file on disk, untouched, after the restart that imported it."
    requirement: "FPRT-04"
    verification:
      - kind: e2e
        ref: "bats e2e/80-fingerprint-store.bats#a user ready to move before the upgrade is still ready after it (legacy-file-exists assertion)"
        status: pass
    human_judgment: false
  - id: D3
    description: "A second start does not import again: a record deleted from the database between two starts is still absent afterward, with the JSON file still present."
    requirement: "FPRT-04"
    verification:
      - kind: e2e
        ref: "bats e2e/80-fingerprint-store.bats#a later start does not import again, and the store still records a fresh login"
        status: pass
    human_judgment: false
  - id: D4
    description: "The store still works after the import: a fresh Emby login for the same user makes them Ready again."
    requirement: "FPRT-04"
    verification:
      - kind: e2e
        ref: "bats e2e/80-fingerprint-store.bats#a later start does not import again, and the store still records a fresh login (fresh-login assertion)"
        status: pass
    human_judgment: false
  - id: D5
    description: "The plugin's database is at the path derived from Jellyfin's own DataFolderPath rule, proven by the file existing there inside the running container after a login."
    requirement: "FPRT-04"
    verification:
      - kind: e2e
        ref: "docker compose exec -T jellyfin test -f /config/plugins/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.VerifiedPasswords.db (Task 1 acceptance criteria, re-checked live against both dev-env.sh and the e2e stack)"
        status: pass
    human_judgment: false

duration: ~45min
completed: 2026-09-20
status: complete
---

# Phase 4 Plan 7: The Fingerprint Store's Upgrade Path, Proven End to End Summary

**A new `80-fingerprint-store.bats` and six `helpers.bash` functions prove the JSON-to-SQLite upgrade against the real server: a user ready to move before the upgrade is still ready after it, and a later start does not import a second time.**

## Performance

- **Duration:** ~45 min
- **Started:** ~2026-09-20T21:14:00Z (approximate, session start after 04-06's completion)
- **Completed:** 2026-09-20T21:59:00Z
- **Tasks:** 2
- **Files modified:** 3 (1 created, 2 modified)

## Accomplishments

- `e2e/80-fingerprint-store.bats` drives the upgrade an administrator actually experiences: a server with a JSON file and no database, started on this version. The first test builds the legacy JSON fixture from a real record the plugin itself just wrote (the fingerprint is a SHA-256 of a freshly salted password hash, so no other source of a genuine "before the upgrade" file exists), removes the database entirely, restarts Jellyfin, and confirms `elton` is `Ready` with the legacy file still present and untouched. The second test deletes `elton`'s row from the pulled database and restarts again: a second import would have put the row back, so `NeedsEmbyLogin` proves the import does not run twice, and a fresh Emby login still returns `elton` to `Ready` afterward -- the store still works past the import it correctly declined to repeat.
- `e2e/helpers.bash` gained six functions that move the fingerprint store safely between a stopped Jellyfin container and the host: `jellyfin_stop`/`jellyfin_start`, `fingerprint_store_pull`/`fingerprint_store_push`, `fingerprint_store_remove`, and `legacy_fingerprint_file_write`, plus the exported `FINGERPRINT_STORE_DB` and `LEGACY_FINGERPRINT_FILE` path constants. `fingerprint_store_pull` requires `sqlite3` on the host and fails, naming it, rather than skipping.
- `.claude/rules/e2e.md` names `sqlite3` as the host prerequisite the new file needs, and states why it is not a mise pin.
- Confirmed live, twice: the fingerprint database sits at `/config/plugins/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.VerifiedPasswords.db` inside the container (Jellyfin's own `DataFolderPath` derivation), separate from `/config/plugins/EmbyAuth_1.0.0.0/`, where the DLL itself is mounted.

## Task Commits

Each task was committed atomically:

1. **Task 1: helpers that move the fingerprint store between the container and the host** - `e7804ef` (feat)
2. **Task 2: the upgrade, and the start that must not import again** - `b736d08` (test)

**Plan metadata:** *(this commit)*

## Files Created/Modified

- `e2e/80-fingerprint-store.bats` - two tests, one file-local `migration_state` function, `setup_file`/`setup`/`teardown_file`
- `e2e/helpers.bash` - `jellyfin_stop`, `jellyfin_start`, `fingerprint_store_pull`, `fingerprint_store_push`, `fingerprint_store_remove`, `legacy_fingerprint_file_write`, `_fingerprint_store_container_command`, `FINGERPRINT_STORE_DB`, `LEGACY_FINGERPRINT_FILE`
- `.claude/rules/e2e.md` - one line naming `sqlite3` as a host prerequisite

## Decisions Made

See `key-decisions` in the frontmatter above for the `docker compose exec`-on-a-stopped-container finding and its fix, the `migration_state` naming convention, and the file-based redirection of every `sqlite3`/`jq` intermediate.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking issue] `docker compose exec` cannot remove files from a stopped Jellyfin container**

- **Found during:** Task 1, while implementing `fingerprint_store_remove` and the write-ahead-log cleanup half of `fingerprint_store_push`.
- **Issue:** The plan's design stops Jellyfin first (so its SQLite write-ahead log checkpoints before the database is touched), then needs to delete files inside the stopped container. `docker compose exec` refuses to run against a stopped service -- confirmed empirically with `service "jellyfin" is not running` -- so a straightforward `docker compose exec -T jellyfin rm -f ...` cannot do the deletion at the point the plan needs it.
- **Fix:** `fingerprint_store_remove` and the wal/shm cleanup half of `fingerprint_store_push` route their `rm -f` through a throwaway container started with `docker run --rm --volumes-from <jellyfin_container_id> nginx:1.30.5-alpine sh -c 'rm -f ...'`. `--volumes-from` attaches to a container's declared volumes regardless of whether that container is running -- confirmed empirically against the Jellyfin image's own declared `/config` volume (`docker image inspect jellyfin/jellyfin:12.1.20260915-010956` shows `Config.Volumes: {"/cache":{},"/config":{}}`) -- and `nginx:1.30.5-alpine` is the exact tag the `emby-proxy` service already pulls for this stack, so this adds no new pinned dependency.
- **Files modified:** `e2e/helpers.bash`
- **Verification:** each of the six helpers smoke-tested individually against the live e2e stack before being exercised by the bats file (stop, pull, sqlite3 read, remove, restart with the database recreated fresh; separately, legacy-write, pull, push, wal/shm cleanup, restart). `bats e2e/80-fingerprint-store.bats` then passed 2/2 twice standalone and 3/3 alongside `90-jellyfin-log.bats` in one invocation, and `mise run e2e` passed 41/41.
- **Committed in:** `e7804ef` (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (Rule 3: a blocking issue in the mechanism the plan's own design needed)
**Impact on plan:** No change to what the tests prove or how they prove it -- the fix is entirely inside `helpers.bash`'s implementation of `fingerprint_store_remove`/`fingerprint_store_push`. No scope creep: no new pinned dependency, since the image reused is already pulled for this stack.

## Issues Encountered

None beyond the deviation above.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- FPRT-04's end-to-end half is complete. `FPRT-04` stays `Pending` in `REQUIREMENTS.md` (shared-ID gate, #2388) until 04-08, the last declaring plan, also lands its SUMMARY.
- `e2e/helpers.bash`'s six new functions (`jellyfin_stop`, `jellyfin_start`, `fingerprint_store_pull`, `fingerprint_store_push`, `fingerprint_store_remove`, `legacy_fingerprint_file_write`) and the `FINGERPRINT_STORE_DB`/`LEGACY_FINGERPRINT_FILE` constants are available to any later plan that needs to inspect or manipulate the fingerprint store from outside the container.
- Full suite: `mise run e2e` 41/41 (39 carried forward, 2 new), `mise run test` (unit 218/218 unchanged, script 8/8 unchanged, settings-page JS 58/58 unchanged), `mise run lint` clean (dotnet format, shellcheck, shfmt, actionlint, zizmor).
- `scripts/dev-env.sh up` and `down` both re-checked after the `helpers.bash` change, per repo convention.

## Known Stubs

None.

## Threat Flags

None. The threat register's five entries (T-04-29 through T-04-33) are all `mitigate`, and the implementation follows each mitigation as designed: every `sqlite3`/`jq` result is redirected to a file under `$BATS_TEST_TMPDIR` (T-04-29); every container path is one of two fixed exported constants (T-04-30); `teardown_file` starts Jellyfin before anything else (T-04-31); `fingerprint_store_push` removes the write-ahead-log and shared-memory siblings after writing the file, and `fingerprint_store_remove` removes all three together (T-04-32); `fingerprint_store_pull` fails, naming `sqlite3`, rather than skipping when the tool is absent (T-04-33). The `docker run --volumes-from` mechanism this plan's Rule 3 fix introduces is test-only tooling that never ships in the plugin, reuses an already-pinned image, and touches only the e2e stack's own containers.

---
*Phase: 04-emby-traffic-under-load-and-failure*
*Completed: 2026-09-20*

## Self-Check: PASSED

- Both task commits (`e7804ef`, `b736d08`) found in `git log --oneline --all`.
- All 3 key files found on disk: `e2e/80-fingerprint-store.bats`, `e2e/helpers.bash`, `.claude/rules/e2e.md`.
- `shellcheck -x e2e/*.bash e2e/*.bats` -- clean.
- `shfmt -d e2e` -- no diff.
- `bats e2e/80-fingerprint-store.bats` -- 2/2, run twice standalone (KEEP_E2E=1 both times), both green.
- `bats e2e/80-fingerprint-store.bats e2e/90-jellyfin-log.bats` (single invocation, shared stack) -- 3/3, proving the legacy-file-scan check finds no leaked secret and Jellyfin was left running.
- `mise run e2e` -- 41/41 (39 carried forward, 2 new).
- `mise run test` -- unit 218/218 (unchanged), script 8/8 (unchanged), settings-page JS 58/58 (unchanged).
- `mise run lint` -- clean.
- `scripts/dev-env.sh up` and `down` -- both exit 0.
- All Task 1 and Task 2 acceptance-criteria greps re-run and passing (six helper names, `command -v sqlite3`, `sqlite3` in `.claude/rules/e2e.md`, the no-legacy-file-deletion grep finding nothing, the live database-path check inside the running container).
