---
phase: 02-safe-failures-for-the-fingerprint-file-and-settings
reviewed: 2026-09-19T00:00:00Z
depth: standard
files_reviewed: 14
files_reviewed_list:
  - .gitignore
  - .mise.toml
  - .pre-commit-config.yaml
  - CLAUDE.md
  - docs/development.md
  - docs/how-it-works.md
  - docs/settings.md
  - src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html
  - src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs
  - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs
  - tests/js/configPage.test.js
  - tests/js/package-lock.json
  - tests/js/package.json
  - tests/js/testHelpers.js
findings:
  critical: 0
  warning: 4
  info: 2
  total: 6
status: issues_found
---

# Phase 02: Code Review Report

**Reviewed:** 2026-09-19T00:00:00Z
**Depth:** standard
**Files Reviewed:** 14
**Status:** issues_found

## Summary

I read the nullable-`Load()` refactor in `EmbyVerifiedPasswords.cs` against its only two callers, `Record()` and `Matches()`, and against the two consumers that depend on `Matches()` to gate a move to the Default login method (`MoveToDefaultLoginMethod.cs`, `DefaultLoginMethod.cs`). The read-failure path this phase adds is correct: both callers hold `_lock` around every call to `Load()`, a failed read returns `null` without touching the cache, `Record()` returns early on `null` so a failed read can never produce a write, and `Matches()` treats `null` as "no match." The new xUnit tests for this path (`UnreadableFile_KeepsItsRecords_WhenALoginIsRecorded`, `UnreadableFile_IsReadAgain_WhenItBecomesReadable`, `ConcurrentRecords_AreNotWritten_WhenTheFileIsUnreadable`) genuinely constrain this behavior rather than restating the implementation — they assert on file bytes, `.tmp` absence, and log content, not just return values. I found no BLOCKER: nothing lets a password Emby did not verify open an account, and nothing in the FPRT-02 diff can cause a write on a failed read.

The `configPage.html` UI-01/UI-02 changes also look correct on inspection: every failure path uses `textContent`, no rejection value or configured URL/API key is ever interpolated into a displayed message, and the new JS test suite (18 `node:test` cases) includes two tests that specifically plant a URL containing `user:password@host` and an API-key sentinel in the rejection and assert neither string appears in the DOM — a genuine security-relevant test, not a placeholder.

I found four WARNING-level issues, none of which touch the core invariant, and two INFO-level issues. Three of the warnings sit adjacent to the reviewed diff rather than inside it (the write-failure branch of `Record()`, the JS tooling gap) but are visible defects in the files as submitted for this phase, so I have included them.

## Warnings

### WR-01: The write-failure log message and docs describe behavior the code does not have

**File:** `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs:58-68`, `129`; `docs/how-it-works.md:49`

**Issue:** In `Record()`, `fingerprints[userId] = fingerprint;` (line 58) mutates the cached `_fingerprints` dictionary before the `try` block that writes to disk. If `File.WriteAllText` or `File.Move` then throws, the `catch` only logs — it never reverts the mutation. Because `Matches()` (line 88) reads the same cached dictionary, the very next `Matches()` call for that exact user and hash returns `true`, and `MoveToDefaultLoginMethod.OnEvent` (which runs immediately after this same login, via the `AuthenticationResultEventArgs` published at the end of the same request) will move the user to Default on this login. This directly contradicts:
- `LogWriteFailed`'s message (line 129): "The plugin does not move this user to the Default login method until the user logs in again through Emby."
- `docs/how-it-works.md:49`: "...moves no affected user to Default until that user logs in through Emby again."

This is not a security problem — the fingerprint recorded is for a password Emby genuinely just verified, so the in-memory cache diverging from disk does not let an unverified password through. But an administrator reading the log or the docs during an incident would draw an incorrect conclusion about what just happened (the user *did* move, on this login, despite the write failure), which works against the project rule to "fail fast with clear, actionable messages."

**Fix:** Either make the behavior match the message (snapshot/rollback the entry on write failure so `Matches()` also fails until the next successful write), or make the message match the behavior (say that the record was kept in memory for this process and will move the user immediately, but will not survive a restart). The rollback is more defensible given the docstring's stated intent ("the record is lost"):
```csharp
fingerprints[userId] = fingerprint;
try
{
    var temporaryPath = _filePath + ".tmp";
    File.WriteAllText(temporaryPath, JsonSerializer.Serialize(fingerprints));
    File.Move(temporaryPath, _filePath, overwrite: true);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    fingerprints.Remove(userId); // keep the cache honest with what's on disk
    LogWriteFailed(_logger, ex, _filePath);
}
```

### WR-02: The write-failure branch of `Record()` has no test coverage

**File:** `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs`

**Issue:** This phase added strong coverage for the read-failure branch (three dedicated tests plus a concurrency test), but there is no test anywhere in the suite that exercises a write failure (`LogWriteFailed`, the `.tmp`-file-left-behind case on a failed `File.Move`, or the cache-divergence behavior described in WR-01). A `grep` for `LogWriteFailed` across `tests/` and `src/` turns up only the two call sites in `EmbyVerifiedPasswords.cs` itself. Given this method sits directly on the security-critical `Matches()` gate, the untested branch is the more consequential one to leave unverified.

**Fix:** Add a test that makes the write fail (e.g., point `_filePath` at a path whose parent directory does not exist, or make the directory read-only) and assert on the resulting state of `Matches()` for that user, the log entry, and whether a `.tmp` file is left behind.

### WR-03: New JS test files ship with no lint or static-analysis tooling

**File:** `.mise.toml:19-27`; `tests/js/testHelpers.js`; `tests/js/configPage.test.js`

**Issue:** `[tasks.lint]` covers C# (`dotnet format`), shell (`shellcheck`, `shfmt`), and GitHub workflows (`actionlint`, `zizmor`), but nothing was added for the newly introduced JavaScript under `tests/js/`. Every other language in this repo has a dedicated lint step wired into `mise run lint` / CI; the JS test suite is the only exception. Concretely, this let a stale `// eslint-disable-next-line no-await-in-loop` comment (`tests/js/testHelpers.js:189`) into the repo referencing a tool that is not installed or run anywhere in the project (there is no ESLint config in the repository) — see IN-01. A linter would have caught this as an unused/invalid directive on the first run.

**Fix:** Add a JS lint step (this repo's own `.mise.toml` already pins `node`; a package like `eslint` with a minimal flat config, or a lighter tool, could run under `mise run lint`), or, if the JS test suite is intentionally kept lint-free, remove the orphaned `eslint-disable` comment so it does not imply tooling that does not exist.

### WR-04: A settings-load failure on a repeat visit leaves a stale migration list on screen

**File:** `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html:92-97`

**Issue:** The `pageshow` handler's `.catch` (lines 92-94) overwrites `summary.textContent` with the fixed load-failure message and disables Save, but it never calls `list.replaceChildren()` on `#EmbyAuthMigrationUsers`. If a prior `pageshow` succeeded and populated the migration list, and a later `pageshow` (e.g., the admin navigates away and back inside Jellyfin's dashboard SPA, which reuses the DOM and re-fires `pageshow` rather than reloading the page) fails to load settings, the page now shows the generic "cannot load the plugin settings" message directly above a migration list that was never cleared and is not being refreshed — a UI state that implies nothing is up to date when in fact stale per-user "ready"/"needs one login" data is still displayed as if current. This exact sequence (success, then failure, on the same page instance) is not covered by any test in `configPage.test.js`; the existing recovery test (`a later successful load turns Save back on`) only exercises failure-then-success.

**Fix:** Clear the list alongside the summary message in the load-failure catch:
```javascript
}).catch(function () {
    summary.textContent = 'Jellyfin cannot load the plugin settings. Save is turned off until the settings load. See the Jellyfin log.';
    document.querySelector('.button-submit').disabled = true;
    list.replaceChildren();
}).finally(function () {
```

## Info

### IN-01: Orphaned `eslint-disable` directive

**File:** `tests/js/testHelpers.js:189`

**Issue:** `// eslint-disable-next-line no-await-in-loop` has no effect — no `.eslintrc*`/`eslint.config.*` exists in the repository and nothing in `.mise.toml` or CI runs ESLint (confirmed by search). The comment misleads a future reader into thinking this project lints JavaScript.

**Fix:** Remove the comment, or add the ESLint step referenced in WR-03 so the directive does something.

### IN-02: `File.Exists` / `File.ReadAllText` race can misclassify a deleted file as "unreadable" instead of "missing"

**File:** `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs:106-120`

**Issue:** `Load()` checks `File.Exists(_filePath)` and only then calls `File.ReadAllText(_filePath)`. If the file is deleted between those two calls (e.g., an administrator removes it while Jellyfin is running), `File.ReadAllText` throws a `FileNotFoundException`, which is an `IOException` and is caught by the same handler as a genuinely corrupt/locked file — logged as "cannot read" and retried on the next call, rather than being treated as "missing" (which would proceed with a fresh empty dictionary and allow writes immediately). The window is narrow and self-corrects on the very next call once the deletion is no longer mid-flight, so this is not exploitable and does not affect the core invariant. Noting it because it is a real, if narrow, gap between the "missing" and "unreadable" cases the phase context asked me to check.

**Fix:** Not worth the added complexity for a self-correcting single-call race; no action required unless this file is revisited for other reasons.

---

_Reviewed: 2026-09-19T00:00:00Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
