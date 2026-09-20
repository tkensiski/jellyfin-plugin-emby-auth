---
phase: 03-migration-status-and-target
plan: 07
subsystem: testing
tags: [e2e, docker, bats, jellyfin-plugin, documentation]

# Dependency graph
requires:
  - phase: 03-migration-status-and-target
    provides: "LoginMethodMove.ResolveMigrationTarget/ResolvePasswordSetTarget, the MigrationTarget/PasswordSetTarget settings, MigrationTargetValidation's save-time refusal, and the reshaped GET /EmbyAuth/Migration response (03-04, 03-05, 03-06) — this plan proves all of it against real Emby and Jellyfin servers, and documents the result"
provides:
  - "A checksum-pinned, version-pinned install of JellyfinSecurity v2.6.1's Jellyfin-12 build in the shared end-to-end stack, giving the suite a second real login method"
  - "e2e/50-migration-target.bats: the only evidence in this phase from real servers rather than doubles — the migration hand-over, the no-saved-password case, both server-side target refusals, and the 403 check on the reshaped API"
  - "docs/settings.md, docs/migration.md, README.md, and CHANGELOG.md brought current with the two target settings, the renamed task, and the reshaped API response"
affects: []

# Actuals (#2632)
actuals:
  tokens: 6710
  tasks: 3
  commits: 3

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "A fetch script pins a third-party release by a sha256 committed as a literal, never read from the same host as the archive at run time, and skips the download when a marker file already matches the pinned digest"
    - "A second real Jellyfin plugin mounted read-write into the same container as this plugin, before the stack starts, so Jellyfin loads it at boot with no second restart"
    - "An e2e test reads a third-party login method's provider id from this plugin's own AvailableTargets response, never a hardcoded type name, so a JellyfinSecurity version bump cannot silently break the file"

key-files:
  created:
    - scripts/fetch-jellyfinsecurity.sh
    - e2e/50-migration-target.bats
  modified:
    - scripts/dev-env.sh
    - e2e/compose.yaml
    - e2e/setup_suite.bash
    - e2e/helpers.bash
    - docs/settings.md
    - docs/migration.md
    - README.md
    - CHANGELOG.md

key-decisions:
  - "The plugin folder mounted into the Jellyfin container is JellyfinSecurity_2.6.1.1, not the plan's placeholder JellyfinSecurity_2.6.1.0 — the unpacked meta.json reports version 2.6.1.1 even though the release tag and asset name both say 2.6.1.0; the plan's own instruction to confirm against meta.json and use what it says covers exactly this mismatch."
  - "The zip's real internal layout is a top-level TwoFactorAuth/ folder holding meta.json and every assembly, discovered only by unpacking the actual archive; compose.yaml mounts that nested folder directly rather than the parent artifacts/jellyfinsecurity/ directory the fetch script unpacks into."
  - "fetch-jellyfinsecurity.sh's clean action uses trash, which is macOS-only and absent from the ubuntu-latest CI runners; this is safe because clean is a local-developer convenience no mise task or CI job ever calls — fetch is the only action the automated suite uses."
  - "compute_sha256 prefers sha256sum (present on the Linux CI runners) and falls back to shasum -a 256 (macOS, no sha256sum by default), so the same script verifies correctly on both platforms without a mise-pinned checksum tool."

patterns-established:
  - "A pinned third-party release archive is verified against a digest committed to this repository as a literal, never against a sidecar fetched from the same host as the archive at run time — the same standard T-03-13 already required, now demonstrated end to end."

requirements-completed: [MIGR-01, AUTH-06]

coverage:
  - id: D1
    description: "JellyfinSecurity v2.6.1's Jellyfin-12 build runs beside this plugin in the shared end-to-end stack from a checksum-pinned download; the whole existing suite still passes with it installed; RESEARCH.md's Open Question 3 (whether JellyfinSecurity is inert on a completely unconfigured install) is answered from a live server with recorded evidence."
    verification:
      - kind: e2e
        ref: "mise run e2e (32/32 passing with JellyfinSecurity installed)"
        status: pass
      - kind: other
        ref: "manual live check: GET /Plugins on a fresh stack lists 'Jellyfin Security' 2.6.1.1; an ordinary Jellyfin-local user's login returns 200 with no challenge"
        status: pass
      - kind: other
        ref: "scripts/dev-env.sh up && scripts/dev-env.sh down"
        status: pass
    human_judgment: false
  - id: D2
    description: "A real user migrates from the Emby login method to JellyfinSecurity's login method and logs in again with the same password without a new request reaching Emby; an account with no saved password is named, not moved, and still logs in through Emby; a real server refuses both an unknown migration target and this plugin's own Emby method, and the previously saved settings survive both refusals; a regular user still gets 403 from both migration routes."
    requirement: "MIGR-01, AUTH-06"
    verification:
      - kind: e2e
        ref: "e2e/50-migration-target.bats (5/5 passing standalone)"
        status: pass
      - kind: e2e
        ref: "mise run e2e (32/32 passing as a whole, including e2e/90-jellyfin-log.bats finding no leaked password)"
        status: pass
    human_judgment: false
  - id: D3
    description: "docs/settings.md and README.md describe the Migration target and Password-set target settings and how Migration behavior combines with them; docs/migration.md names the four migration-list states, the renamed task, and the reshaped API response; CHANGELOG.md records the task's key change and the two new settings."
    verification:
      - kind: other
        ref: "every grep-based acceptance criterion in 03-07-PLAN.md Task 3 (Migration target, Password-set target, Remain on Emby Login, no effect, Finish the Emby migration, RecordsUnavailable, AvailableTargets, NoPassword, EmbyAuthMigration, no ReadyToMove or old task name anywhere in docs/README/CHANGELOG)"
        status: pass
      - kind: other
        ref: "mise run test, mise run lint, mise run e2e all green after the docs commit"
        status: pass
    human_judgment: false

# Metrics
duration: 23min
completed: 2026-09-20
status: complete
---

# Phase 3 Plan 7: The Migration Target on a Real Server, and the Documentation Catch-Up Summary

**JellyfinSecurity v2.6.1 (Jellyfin-12 build) installed checksum-verified beside this plugin, proving the migration hand-over, the no-saved-password case, and both server-side target refusals against real Emby and Jellyfin servers, with docs/settings.md, docs/migration.md, README.md, and CHANGELOG.md brought current**

## Performance

- **Duration:** 23 min
- **Started:** 2026-09-20T06:19:22Z (approx., from STATE.md's last session timestamp)
- **Completed:** 2026-09-20T06:42:20Z
- **Tasks:** 3
- **Files modified:** 10 (2 created, 8 modified)

## Accomplishments

- `scripts/fetch-jellyfinsecurity.sh` downloads JellyfinSecurity v2.6.1's `-jf12` asset, checks it against a sha256 pinned as a literal (taken from the published sidecar and never re-read at run time), and unpacks it into `artifacts/jellyfinsecurity/`. It skips the download on a repeat run once the unpacked folder already carries the pinned digest.
- `e2e/compose.yaml` mounts the unpacked plugin into the shared Jellyfin container before the stack starts; `e2e/setup_suite.bash` and `scripts/dev-env.sh` both fetch it first. `e2e/setup_suite.bash`'s Emby user loop gained `yara` and `zack` for the new test file, and `e2e/helpers.bash` gained `precreate_on_emby_method_without_password` plus two new default resets in `reset_plugin_config`.
- Confirmed live, with evidence: `GET /Plugins` lists "Jellyfin Security" 2.6.1.1 after the mount, and an ordinary Jellyfin-local login still returns 200 with no challenge on a completely unconfigured install — RESEARCH.md's Open Question 3 is answered.
- `e2e/50-migration-target.bats` reads JellyfinSecurity's provider id from this plugin's own `AvailableTargets` response — never a hardcoded type name — and proves: a real user migrates to JellyfinSecurity's login method and logs in again with the same password with no new request reaching Emby (ROADMAP criterion 7); an account with no saved password is named, is not moved by the migration task, and still logs in through Emby (AUTH-06); a real server refuses both an unknown migration target and this plugin's own Emby method, with the previously saved settings surviving each refusal (MIGR-01); and a regular user still gets 403 from both migration routes.
- `docs/settings.md`, `docs/migration.md`, `README.md`, and `CHANGELOG.md` now describe both target settings, the `EmbyAuthMigration` task key, the four migration-list states, and the reshaped `GET /EmbyAuth/Migration` response. The save-time-only nature of the refusal is stated correctly throughout — no doc line claims the plugin re-validates the target at migration time.

## Task Commits

Each task was committed atomically:

1. **Task 1: A second real login method in the end-to-end stack, and proof it is inert** - `7357d47` (feat)
2. **Task 2: The hand-over to a second login method, and the refusals, on a real server** - `0dd637f` (test)
3. **Task 3: The documentation catches up with the two settings, the renamed task, and the new response** - `8108dcb` (docs)

**Plan metadata:** this commit (docs)

## Files Created/Modified

- `scripts/fetch-jellyfinsecurity.sh` - pins, downloads, checksum-verifies, and unpacks JellyfinSecurity's `-jf12` build
- `e2e/compose.yaml` - mounts the unpacked plugin into the shared Jellyfin container
- `e2e/setup_suite.bash` - fetches the plugin before the stack starts; adds `yara` and `zack` to the Emby user loop
- `e2e/helpers.bash` - `precreate_on_emby_method_without_password`; `reset_plugin_config` resets the two new target settings
- `scripts/dev-env.sh` - fetches the plugin too, since it reuses the same compose file
- `e2e/50-migration-target.bats` - the hand-over, no-saved-password, refusal, and authorization tests
- `docs/settings.md` - the Migration target and Password-set target sections, and the D-15/D-17 statements
- `docs/migration.md` - the four migration-list states, the renamed task, and the reshaped API response
- `README.md` - the short-form promise and setup steps, reworded for a configurable destination
- `CHANGELOG.md` - the task key rename and the two new settings, under Unreleased

## Decisions Made

- The mounted plugin folder is `JellyfinSecurity_2.6.1.1`, matching the version the unpacked `meta.json` actually reports, not `2.6.1.0` (the version encoded in the release tag and asset filename). The plan's own instruction — confirm against `meta.json` and use what it says if it differs — anticipated exactly this mismatch.
- The zip's real internal layout is a top-level `TwoFactorAuth/` folder; `compose.yaml` mounts that nested folder directly, discovered only by actually unpacking the pinned archive rather than assuming a flat layout.
- `fetch-jellyfinsecurity.sh`'s `clean` action uses `trash`, which is macOS-only. This is safe because `clean` is a local-developer convenience that no `mise` task or CI job ever calls; `fetch` — the action every automated path uses — needs no destructive delete at all.
- `compute_sha256` tries `sha256sum` first (present on the `ubuntu-latest` CI runners) and falls back to `shasum -a 256` (macOS's default), so the same script verifies correctly in CI and in local development without adding a new pinned tool.

## Deviations from Plan

None - plan executed exactly as written. One implementation-time bug was found and fixed before any commit: an initial `trap ... EXIT` used to clean up the downloaded archive fired again when the whole script exited (not just when `fetch()` returned), tripping `set -u` on the now-out-of-scope local variable. Replaced with an explicit `rm -f` at each of `fetch()`'s two exit points before the first commit; caught by actually running the script during development, per this repo's normal practice of proving a script rather than assuming it.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- This was the final plan in Phase 3. All ten of the phase's success criteria are met: the migration status reports the read failure, the task state, and the per-user state; the settings page's Migration section follows the real task to completion; the move-target settings exist with save-time refusal; the renamed classes and task are in place; unit tests cover the migration code against a real `JellyfinDbContext`; and this plan's e2e test is the phase's only evidence from real servers.
- `LoginMethodMove.ResolveMigrationTarget`/`ResolvePasswordSetTarget` still do not check whether Jellyfin currently reports a saved target as enabled at *resolve* time (only at *save* time, per 03-06's `MigrationTargetValidation`) — this residual gap, recorded in `03-06-SUMMARY.md`'s "Resolve-Time Gap" section, remains open for whichever future phase weighs it against a general resolve-time enabled-list check across `ChangePassword`, `MoveAfterLogin`, and `EmbyMigrationTask`.
- No blockers for Phase 4.

---
*Phase: 03-migration-status-and-target*
*Completed: 2026-09-20*

## Self-Check: PASSED

- All 2 created files found on disk (`scripts/fetch-jellyfinsecurity.sh`, `e2e/50-migration-target.bats`); all 8 modified files found on disk.
- All 3 task commit hashes (`7357d47`, `0dd637f`, `8108dcb`) found in `git log`.
- `mise run lint` — green (dotnet format, shellcheck, shfmt, actionlint, zizmor).
- `mise run test` — green (dotnet 200/200, bats script tests, node 58/58).
- `mise run e2e` — 32/32 bats tests passing, including `e2e/50-migration-target.bats` standalone (5/5) and the final `e2e/90-jellyfin-log.bats` log-leak check.
- `scripts/dev-env.sh up` and `scripts/dev-env.sh down` — both succeed.
- Every grep-based acceptance criterion for all three tasks re-run and passing.
