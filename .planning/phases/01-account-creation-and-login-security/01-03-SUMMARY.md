---
phase: 01-account-creation-and-login-security
plan: 03
subsystem: auth
tags: [migration-mode, e2e, bats, jellyfin, emby]

# Dependency graph
requires:
  - phase: 01-account-creation-and-login-security
    provides: EmbyAuthenticationProvider.Authenticate with the saved-password branch from plan 01-01/01-02
provides:
  - "MigrationMode with exactly two members: MoveAfterFirstLogin and KeepEmbyInCharge"
  - "Authenticate with no saved-password comparison and no orphaned private members"
  - "Two e2e tests proving AUTH-01 (30-migration-modes.bats, 40-emby-outage.bats) without the removed value"
affects: [01-04-docs-and-changelog]

# Actuals (#2632)
actuals:
  tokens: 2140
  tasks: 2
  commits: 2

# Tech tracking
tech-stack:
  added: []
  patterns: []

key-files:
  created: []
  modified:
    - src/Jellyfin.Plugin.EmbyAuth/Configuration/PluginConfiguration.cs
    - src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs
    - src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html
    - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthSettingsTests.cs
    - e2e/30-migration-modes.bats
    - e2e/40-emby-outage.bats

key-decisions:
  - "D-05 confirmed by the human at the checkpoint: delete JellyfinPasswordFirst with no compatibility path (option 'proceed')."
  - "e2e/40-emby-outage.bats keeps KeepEmbyInCharge set for the rest of setup_file instead of resetting right after uma's login, removing a race between the move-to-Default event consumer and a plugin-config reset."

patterns-established: []

requirements-completed: [AUTH-02, AUTH-01]

coverage:
  - id: D1
    description: "MigrationMode.JellyfinPasswordFirst, its saved-password branch, and both orphaned private members are removed from the plugin, the settings page loses the matching option, and the settings test uses a surviving member"
    requirement: "AUTH-02"
    verification:
      - kind: unit
        ref: "dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx (113/113 passed)"
        status: pass
      - kind: other
        ref: "rg -n 'JellyfinPasswordFirst|Check the saved Jellyfin password first' src tests e2e (zero matches)"
        status: pass
      - kind: other
        ref: "mise run lint (dotnet format, shellcheck, shfmt, actionlint, zizmor — all clean)"
        status: pass
    human_judgment: false
  - id: D2
    description: "While a user is on the Emby login method, an Emby password change is effective at once and the saved Jellyfin hash follows it, proven end-to-end against real Emby and Jellyfin containers"
    requirement: "AUTH-01"
    verification:
      - kind: e2e
        ref: "e2e/30-migration-modes.bats#Keep Emby in charge: an Emby password change is effective at once and the saved hash follows it"
        status: pass
    human_judgment: false
  - id: D3
    description: "While Emby is unreachable, a user on the Emby login method with an Emby-verified saved hash is refused"
    requirement: "AUTH-01"
    verification:
      - kind: e2e
        ref: "e2e/40-emby-outage.bats#while Emby is unreachable, a verified saved hash does not open an account"
        status: pass
      - kind: e2e
        ref: "e2e/90-jellyfin-log.bats#the Jellyfin log contains no password and no API key"
        status: pass
    human_judgment: false

duration: 25min (this continuation)
completed: 2026-09-17
status: complete
---

# Phase 01 Plan 03: Delete the saved-password migration behavior Summary

**Removed `MigrationMode.JellyfinPasswordFirst` and its saved-password branch, then rewrote the two e2e tests that relied on it to prove AUTH-01 against real Emby/Jellyfin containers.**

## Performance

- **Duration:** ~25 min for this continuation (Task 1 was already implemented and verified by a prior executor; this session committed it, executed Task 2, and ran full verification)
- **Completed:** 2026-09-17T22:18:18Z
- **Tasks:** 2 (plus the D-05 checkpoint, resolved before this continuation started)
- **Files modified:** 6

## Accomplishments
- Deleted `MigrationMode.JellyfinPasswordFirst`, the saved-password branch in `Authenticate`, and the two private members (`SavedPasswordMatches`, `LogSavedPasswordUnreadable`) that only that branch called
- Removed the matching `<option>` from `configPage.html` and rewrote the field description for the one remaining "stay on Emby" choice
- Updated `EmbyAuthSettingsTests` to use `MigrationMode.KeepEmbyInCharge`
- Rewrote `e2e/30-migration-modes.bats`'s migration test to prove that `KeepEmbyInCharge` makes an Emby password change effective on the very next login, and that the saved Jellyfin hash follows the new password (`migration_ready_state` reports the user ready to move)
- Rewrote `e2e/40-emby-outage.bats`'s outage test to prove that a verified saved hash does not open an account while Emby is unreachable, and adjusted `setup_file` to reach that state under `KeepEmbyInCharge` instead of the removed value

## Task Commits

1. **Task 1: Delete the saved-password migration behavior from the code, the settings page, and the settings test** - `9cc6947` (feat)
2. **Task 2: Rewrite the two end-to-end tests that used the removed value** - `102ee99` (test)

**Plan metadata:** pending (this commit)

## Files Created/Modified
- `src/Jellyfin.Plugin.EmbyAuth/Configuration/PluginConfiguration.cs` - `MigrationMode` now has exactly two members
- `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs` - `Authenticate` has no saved-password branch and no orphaned members
- `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html` - one migration-behavior option removed, field description rewritten
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthSettingsTests.cs` - uses `MigrationMode.KeepEmbyInCharge`
- `e2e/30-migration-modes.bats` - the `oscar` test now proves an at-once password change under `KeepEmbyInCharge`
- `e2e/40-emby-outage.bats` - `uma` reaches an Emby-verified saved hash under `KeepEmbyInCharge`; the outage test asserts 401

## Decisions Made
- The D-05 checkpoint was resolved by the human user before this continuation began: proceed with the one-way deletion, accepting that any install still holding the removed value loses its Emby URL and API key at next load (D-07, recorded for `CHANGELOG.md` in plan 01-04).
- Kept `KeepEmbyInCharge` set through the rest of `e2e/40-emby-outage.bats`'s `setup_file` instead of resetting right after `uma`'s login, per the plan's instruction — this removes a race with the move-to-Default event consumer reading configuration after a reset.

## Deviations from Plan

None — plan executed exactly as written. Task 1's edits, verification, and staging were completed by a prior executor before a commit-signing (1Password/Touch ID) checkpoint interrupted that session; this continuation retried the same staged commit unchanged, then executed Task 2 as specified.

## Issues Encountered

The prior executor's session hit two consecutive `git commit` failures with `error: 1Password: failed to fill whole buffer` / `fatal: failed to write commit object` because the SSH-signing Touch ID prompt expired while the user was away from the keyboard. No code or state was lost — the four files remained correctly staged. This continuation, with the user confirmed at the keyboard, retried the identical commit and it succeeded on the first attempt with no signing error.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- `MigrationMode` is now a clean two-member enum (`MoveAfterFirstLogin`, `KeepEmbyInCharge`); AUTH-01 and AUTH-02 are both satisfied and proven end-to-end.
- Plan 01-04 can proceed: it owns `docs/settings.md`, `docs/how-it-works.md`, `CHANGELOG.md`, and the settings-page screenshot, none of which this plan touched.
- No blockers. All 113 unit tests, all 8 packaging script tests, and all 27 e2e tests (including the whole-log password/API-key leak check) pass.

---
*Phase: 01-account-creation-and-login-security*
*Completed: 2026-09-17*

## Self-Check: PASSED

- FOUND: `.planning/phases/01-account-creation-and-login-security/01-03-SUMMARY.md`
- FOUND: commit `9cc6947` (Task 1)
- FOUND: commit `102ee99` (Task 2)
