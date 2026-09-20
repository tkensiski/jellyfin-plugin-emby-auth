---
status: passed
phase: 03-migration-status-and-target
source: [03-VERIFICATION.md]
started: 2026-09-20T07:15:00Z
updated: 2026-09-20T08:05:00Z
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
awaiting: none — resolved

## Tests

### 1. Resolve-time target re-validation, and the T-03-04 mitigation text

expected: A maintainer decision — accept the gap as documented residual risk and correct the T-03-04 wording, or schedule a follow-up plan adding a live enabled-provider check to the three move paths.
result: passed — accepted as residual risk, T-03-04 corrected in f03c323. The severity question was settled by measurement first; see "Resolution" below.

### Resolution

**Severity was measured, not assumed.** Jellyfin 12.1 (commit `ee91c75e`) assigns an account whose provider id names nothing installed to `InvalidAuthProvider`, a deny-everything stub: `Authenticate` throws `AuthenticationException`, every login returns 401. It never falls back to `DefaultAuthenticationProvider`. Confirmed by source and by experiment against the project's own dev stack, with a control in the same run — a passwordless account on the real Default provider returned 200 for an empty password, proving the harness detects the dangerous outcome. So a stale target strands the user; it does not expose the account.

**Decision:** accept the resolve-time gap as residual risk. No live enabled-provider check was added to `ChangePassword`, `MoveAfterLogin`, or `EmbyMigrationTask`. Reaching the gap requires an administrator to uninstall a third-party plugin after configuring it as the migration target, and the outcome is recoverable by an administrator.

**T-03-04 corrected** in `03-06-PLAN.md` (commit `f03c323`): the cell claimed `ChangePassword` "leaves the login method alone" for a since-disappeared target, which is the `Invalid` branch and is reached only for a blank target. It now states that a stale non-blank target resolves to `Move` and is written, and that the outcome is a stranded account.

### What the investigation turned up instead

Chasing severity surfaced a live issue in the shipped feature, which was fixed rather than accepted:

JellyfinSecurity 2.6.1's `TwoFactorAuthProvider` does not check passwords itself. It delegates to the first other enabled provider (`TwoFactorAuthProvider.cs:339-341`), which is Jellyfin's Default, and so inherits Default's rule that a blank password opens an account with no saved password. Its `BlockEmptyPasswordLogin` guard ships `false` (`PluginConfiguration.cs:82`). Measured: a passwordless account on JellyfinSecurity returned HTTP 200 for an empty password. Enrolling in TOTP adds a challenge, but only after the blank password passes as the first factor — and enrollment is reachable using that blank password alone.

The settings page had framed this hazard as specific to Jellyfin's Default method, and told the administrator it "cannot tell" how any other target behaves. That was accurate when written and false once measured. Fixed in `812d4ec` (warning names the delegation mechanism) and `bd54e1c` (`docs/how-it-works.md` records the measurement).

Behavior is unchanged: the plugin still warns about an account with no saved password and still does not refuse the move, per the locked decision behind criterion 9.

### Follow-ups recorded, not actioned

- A disabled-but-installed provider takes the same `InvalidAuthProvider` path, because the enabled filter runs before the id match. Any future move-time check must verify *enabled*, not merely *installed*.
- `UserManager.ChangePassword` on a stale id routes to `InvalidAuthProvider.ChangePassword`, a no-op that reports success. An administrator who sets a password before repairing the login method stores nothing and lands the account on Default with no password. Safe order: repair the login method first.
- Quick Connect does not consult `AuthenticationProviderId` at all, so a stranded user can still obtain a session that way. Not an anonymous path.
- `UserDto.HasPassword` is `[Obsolete]` and hardcoded `true` in Jellyfin 12.1. This plugin correctly reads `user.Password is null` instead; noted so nothing reaches for the API field later.

**What the code does.** `LoginMethodMove.ResolveTarget` (`src/Jellyfin.Plugin.EmbyAuth/LoginMethodMove.cs:69-80`) sorts a target into three cases: blank or whitespace gives `Invalid`, the `RemainOnEmbyLoginMethod` sentinel gives `Remain`, and any other string gives `Move`. It never asks Jellyfin which login methods are enabled. The refusal added by plan 03-06 runs in `EmbyAuthPlugin.UpdateConfiguration`, so it applies when a target is saved and at no other time.

**The consequence.** A target that Jellyfin reported as enabled when an administrator saved it, whose providing plugin is later uninstalled, still resolves to `Move`. `EmbyAuthenticationProvider.ChangePassword`, `MoveAfterLogin`, and `EmbyMigrationTask` then write that provider id to the account unconditionally. The known outcome is a stranded user. Neither the code reviewer nor the verifier found a worse one, and neither could rule one out from this repository alone, because Jellyfin's `UserManager` source is not in it.

**The documentation defect.** Threat T-03-04 in `03-06-PLAN.md:248` covers exactly this scenario and claims the mitigation is that `ChangePassword` "saves the password, leaves the login method alone, logs one Error, and does not throw." That describes the `Invalid` branch. A stale non-blank target reaches the `Move` branch instead, so the mitigation named in the threat register is not the behavior the code provides. Three independent readings agree on this: the 03-06 executor, the phase verifier, and a direct read of the source.

**No test covers this path.** `EmbyAuthenticationProviderTests.cs`, `LoginMethodMoveTests.cs`, and `MoveAfterLoginTests.cs` contain no case for a stale-but-non-blank target.

**Why this is a decision, not a fix.** It contradicts no Phase 3 success criterion. Criterion 6 asks that the settings page offer only enabled login methods and that a value outside that set "is refused with a message" — the save-time refusal satisfies that as written. Closing the gap means giving three classes a live `IUserManager` read, which is an architectural change no Phase 3 plan scoped.

## Summary

total: 1
passed: 1
issues: 0
pending: 0
skipped: 0
blocked: 0

## Gaps
