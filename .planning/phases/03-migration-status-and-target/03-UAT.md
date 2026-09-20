---
status: testing
phase: 03-migration-status-and-target
source: [03-VERIFICATION.md]
started: 2026-09-20T07:15:00Z
updated: 2026-09-20T07:15:00Z
---

## Current Test

number: 1
name: Resolve-time target re-validation, and the T-03-04 mitigation text
expected: |
  A decision, either:
  (a) accept the resolve-time gap as documented residual risk, and correct the
      T-03-04 mitigation text in 03-06-PLAN.md so it describes what the code does; or
  (b) schedule a follow-up plan that adds a live enabled-provider check to
      ChangePassword, MoveAfterLogin, and EmbyMigrationTask.
awaiting: user response

## Tests

### 1. Resolve-time target re-validation, and the T-03-04 mitigation text

expected: A maintainer decision — accept the gap as documented residual risk and correct the T-03-04 wording, or schedule a follow-up plan adding a live enabled-provider check to the three move paths.
result: [pending]

**What the code does.** `LoginMethodMove.ResolveTarget` (`src/Jellyfin.Plugin.EmbyAuth/LoginMethodMove.cs:69-80`) sorts a target into three cases: blank or whitespace gives `Invalid`, the `RemainOnEmbyLoginMethod` sentinel gives `Remain`, and any other string gives `Move`. It never asks Jellyfin which login methods are enabled. The refusal added by plan 03-06 runs in `EmbyAuthPlugin.UpdateConfiguration`, so it applies when a target is saved and at no other time.

**The consequence.** A target that Jellyfin reported as enabled when an administrator saved it, whose providing plugin is later uninstalled, still resolves to `Move`. `EmbyAuthenticationProvider.ChangePassword`, `MoveAfterLogin`, and `EmbyMigrationTask` then write that provider id to the account unconditionally. The known outcome is a stranded user. Neither the code reviewer nor the verifier found a worse one, and neither could rule one out from this repository alone, because Jellyfin's `UserManager` source is not in it.

**The documentation defect.** Threat T-03-04 in `03-06-PLAN.md:248` covers exactly this scenario and claims the mitigation is that `ChangePassword` "saves the password, leaves the login method alone, logs one Error, and does not throw." That describes the `Invalid` branch. A stale non-blank target reaches the `Move` branch instead, so the mitigation named in the threat register is not the behavior the code provides. Three independent readings agree on this: the 03-06 executor, the phase verifier, and a direct read of the source.

**No test covers this path.** `EmbyAuthenticationProviderTests.cs`, `LoginMethodMoveTests.cs`, and `MoveAfterLoginTests.cs` contain no case for a stale-but-non-blank target.

**Why this is a decision, not a fix.** It contradicts no Phase 3 success criterion. Criterion 6 asks that the settings page offer only enabled login methods and that a value outside that set "is refused with a message" — the save-time refusal satisfies that as written. Closing the gap means giving three classes a live `IUserManager` read, which is an architectural change no Phase 3 plan scoped.

## Summary

total: 1
passed: 0
issues: 0
pending: 1
skipped: 0
blocked: 0

## Gaps
