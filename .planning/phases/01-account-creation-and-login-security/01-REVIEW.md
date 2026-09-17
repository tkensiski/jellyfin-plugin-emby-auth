---
phase: 01-account-creation-and-login-security
reviewed: 2026-09-17T00:00:00Z
depth: standard
files_reviewed: 13
files_reviewed_list:
  - src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs
  - src/Jellyfin.Plugin.EmbyAuth/PluginServiceRegistrator.cs
  - src/Jellyfin.Plugin.EmbyAuth/Configuration/PluginConfiguration.cs
  - src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html
  - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs
  - tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs
  - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthSettingsTests.cs
  - e2e/30-migration-modes.bats
  - e2e/40-emby-outage.bats
  - README.md
  - CHANGELOG.md
  - docs/how-it-works.md
  - docs/settings.md
findings:
  critical: 0
  warning: 1
  info: 2
  total: 3
status: resolved
resolution:
  WR-01: fixed in 6f1dcde — two tests added for the CreateUserAsync failure branch
  IN-01: no change needed — 6f1dcde makes the CHANGELOG sentence accurate as written
  IN-02: fixed in d929b0a — both failure outcomes now read as logged at Error
  additional: 6f1dcde widened the CreateUserAsync catch to catch (Exception ex), which
    the review flagged for visibility but did not score. A DbUpdateException from
    CreateUserAsync previously escaped Authenticate as an HTTP 500, contradicting
    D-02 and plan 01-01's must-have. Approved by the user before the fix was applied.
---

# Phase 01: Code Review Report

**Reviewed:** 2026-09-17
**Depth:** standard
**Files Reviewed:** 13
**Status:** issues_found

## Summary

I reviewed the AUTH-04 failure-path work (`CreateAccountAsync`/`SavePasswordAsync` catch widening and the guarded cleanup delete), the new settings-source constructor seam, the `JellyfinPasswordFirst` deletion, the new unit tests and test doubles, the two rewritten e2e files, and the doc/changelog updates.

The security-critical claims hold up under tracing. I confirmed by reading the actual behavior, not by trusting the summaries:

- `CreateAccountAsync`'s cleanup-delete path calls `DeleteUserAsync` exactly once, never retries, logs exactly one Error entry (`LogSaveFailed` if the delete recovers, `LogDeleteFailed` if it does not — verified in code and confirmed by `RefusesTheLogin_WhenTheSaveAndTheCleanupDeleteBothFail`), and always throws `AuthenticationException` regardless of which of the two calls failed. No path returns a bare `User` or lets `saveFailure`/`deleteEx` escape as their original type.
- No log call or exception message in `EmbyAuthenticationProvider.cs` includes a password or hash. `RefusesTheLogin_WhenTheSaveAndTheCleanupDeleteBothFail` explicitly asserts the typed password and the derived hash are absent from every captured log entry.
- No `[Fact(Skip = ...)]` survives in the tree (`rg -n "Skip" tests/**/*.cs` matches only an unrelated `handler.Requests.Skip(1)` LINQ call in `EmbyClientTests.cs`). `dotnet test` on the two required-reading test files: 40/40 passing, 0 skipped.
- `JellyfinPasswordFirst`, `SavedPasswordMatches`, and `LogSavedPasswordUnreadable` are fully gone — zero matches anywhere outside `.planning/`, including `docs/migration.md` and `.claude/rules/plugin.md`, which were not in the plan's file list but could plausibly have held a stale reference.
- The narrow `catch (ArgumentException ex)` around `CreateUserAsync` (left untouched by this phase) is not an oversight: `01-RESEARCH.md` documents that the team read Jellyfin 12.1's actual `UserManager.CreateUserAsync` source and verified it throws only `ArgumentException`. I traced the same source (via the review's scratch copy of `Jellyfin.Server.Implementations/Users/UserManager.cs`) and confirm the explicit `throw` statements match. The one path the research note doesn't account for — `SaveChangesAsync()` itself throwing a `DbUpdateException` for a transient/infrastructure reason unrelated to the duplicate-name pre-check — is a real, if narrow, residual gap against CONTEXT.md's own D-02 promise ("every exception type from `CreateUserAsync`... is caught"), but I could not find evidence this phase was supposed to touch that line, and no account is left half-made if `CreateUserAsync` itself fails (nothing has committed yet). I did not classify it as a finding for this phase, since it predates the diff and the design decision was made with the actual upstream source in hand — but flag it here for visibility since it does not fully match D-02's literal wording.
- `dotnet build src/Jellyfin.Plugin.EmbyAuth` is 0 warnings / 0 errors, confirming the single `[SuppressMessage("Design", "CA1031...")]` on `CreateAccountAsync` is suffient and that `SavePasswordAsync`'s widened `catch (Exception ex)` does not need its own suppression (it always rethrows a wrapped exception, so CA1031 does not fire on it).

One coverage gap survives from this phase's own additions (below).

## Warnings

### WR-01: `FakeUserManager.CreateUserThrows` is dead — the `CreateUserAsync` failure branch of `CreateAccountAsync` has no unit test

**File:** `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs:104` (property, added this phase), consumed by `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs:154-162`

**Issue:** `FakeUserManager` (new in this phase) exposes three configurable failure points — `CreateUserThrows`, `UpdateUserThrows`, `DeleteUserThrows` — matching the three `IUserManager` calls the plugin makes. Every test in `EmbyAuthenticationProviderTests.cs` uses `UpdateUserThrows` and/or `DeleteUserThrows`; grep confirms `CreateUserThrows` is set nowhere. This leaves the `catch (ArgumentException ex) { LogCreateAccountFailed(...); throw new AuthenticationException(InvalidLogin, ex); }` branch in `CreateAccountAsync` (the "Jellyfin cannot create an account... rename the user on Emby" refusal, also referenced in `docs/how-it-works.md`'s Limits section: "If Jellyfin does not allow an Emby user name, the plugin cannot create the account") completely unexercised at the unit level, and I found no e2e coverage either (`rg -n "rename the user|LogCreateAccountFailed"` over `e2e/*.bats` is empty).

This matters because 01-01-SUMMARY.md's own coverage table (D1–D4) and 01-02-SUMMARY.md's claim ("TEST-01 is now fully closed... every account check... all have unit tests") assert complete failure-path coverage for `EmbyAuthenticationProvider`, but the `CreateUserAsync`-throws-`ArgumentException` path — one of exactly three failure points the phase's own test double was built to simulate — is silently excluded. I traced the branch by hand (it correctly throws `AuthenticationException` with no cleanup-delete attempt, which is correct since nothing was committed) and it appears correct, but that correctness rests on manual inspection, not on a test that would catch a regression here.

**Fix:** Add a test alongside the two existing "save fails" tests, e.g.:

```csharp
[Fact]
public async Task RefusesTheLogin_WhenCreateUserFailsBecauseJellyfinRejectsTheName()
{
    var userManager = new FakeUserManager { CreateUserThrows = new ArgumentException("bad name") };
    var handler = new StubHttpMessageHandler().Then(AliceUserList).Then(AliceAuthenticateResponse);
    var provider = CreateProvider(handler, userManager, out _);

    await Assert.ThrowsAsync<AuthenticationException>(() => provider.Authenticate("alice", "alice-pass", null));

    Assert.Equal(["CreateUserAsync"], userManager.Calls);
    Assert.Null(userManager.LastDeletedId);
}
```

## Info

### IN-01: CHANGELOG.md's "Changed" entry slightly overstates what changed on the creation side

**File:** `CHANGELOG.md:13`

**Issue:** "A failure while creating or saving an account now refuses the login instead of returning a server error." The "saving" half is accurate and is exactly what this phase's diff changed (`catch (Exception ex) when (ex is DbUpdateException or ResourceNotFoundException)` widened to `catch (Exception ex)` in both `CreateAccountAsync` and `SavePasswordAsync`). The "creating" half was not touched by this phase — `CreateUserAsync`'s `catch (ArgumentException ex)` already refused with `AuthenticationException` before this phase's diff (confirmed via `git diff 1370eb56bec5d8c17c4114a52d240726ece04efa^..HEAD`, which shows no change to that catch). The sentence reads as if account-creation failure handling is new in this release; it is only the save/cleanup handling that is.

**Fix:** Narrow the sentence to what changed, e.g. "A failure while saving an account's password (on creation or on an existing account) now refuses the login instead of returning a server error." Low priority — no functional impact, just a documentation-accuracy nit for a file that otherwise records the removal and the D-07 note correctly.

### IN-02: `docs/how-it-works.md`'s account-creation-window paragraph describes only the double-failure case as logged

**File:** `docs/how-it-works.md:22`

**Issue:** "If that save fails, the plugin deletes the new account and refuses the login. If the delete also fails, the account stays on the Default login method with no password. The plugin logs the account name at Error level in the Jellyfin log..." As written, the Error-level log sentence reads as attached only to the "delete also fails" case. In the code, both outcomes log at Error level with the account name — `LogSaveFailed` (delete succeeds) and `LogDeleteFailed` (delete fails) are both `LogLevel.Error` and both take `embyLogin.Name`. Nothing here is factually wrong (an administrator does get an Error-level entry either way), but the paragraph could be read as implying the successful-delete case is silent or logged at a lower level, which it is not.

**Fix:** Optional wording tweak, e.g. "...refuses the login, and logs the account name at Error level either way." Not required for accuracy; purely a clarity nit.

---

_Reviewed: 2026-09-17_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
