---
phase: 04-emby-traffic-under-load-and-failure
reviewed: 2026-09-20T22:20:57Z
depth: standard
files_reviewed: 25
files_reviewed_list:
  - src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs
  - src/Jellyfin.Plugin.EmbyAuth/PluginServiceRegistrator.cs
  - src/Jellyfin.Plugin.EmbyAuth/EmbyAuthPlugin.cs
  - src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs
  - src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs
  - src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs
  - src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj
  - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs
  - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyClientTests.cs
  - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyUserDirectoryTests.cs
  - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs
  - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthControllerTests.cs
  - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyLoginMethodUsersTests.cs
  - tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyMigrationTaskTests.cs
  - tests/Jellyfin.Plugin.EmbyAuth.Tests/MoveAfterLoginTests.cs
  - tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs
  - tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoublesTests.cs
  - e2e/helpers.bash
  - e2e/setup_suite.bash
  - e2e/60-concurrent-logins.bats
  - e2e/70-invalid-settings.bats
  - e2e/80-fingerprint-store.bats
  - docs/how-it-works.md
  - CHANGELOG.md
  - CLAUDE.md
  - .claude/rules/e2e.md
findings:
  critical: 0
  warning: 4
  info: 2
  total: 6
status: issues_found
---

# Phase 04: Code Review Report

**Reviewed:** 2026-09-20T22:20:57Z
**Depth:** standard
**Files Reviewed:** 25
**Status:** issues_found

## Summary

This phase replaces the JSON-file fingerprint store with a SQLite-backed `EmbyVerifiedPasswords`, adds a
single-flight `SemaphoreSlim` guard to `EmbyUserDirectory`'s refresh path, and reorders `EmbyClient`'s Emby
sign-out ahead of the user-name check. I read every file in scope, ran the unit test suite (46/46 passing for
the two files most affected), ran `mise run lint` (dotnet format, shellcheck, shfmt, actionlint, zizmor — all
clean), and ran the three live e2e suites this phase's e2e-facing files target
(`60-concurrent-logins.bats`, `70-invalid-settings.bats`, `80-fingerprint-store.bats`) against real Emby and
Jellyfin containers — all 9 scenarios passed, including the JSON→SQLite legacy import and a second start that
must not re-import. I also independently confirmed the pinned `Microsoft.Data.Sqlite.Core` version (10.0.11)
against the actual `jellyfin.deps.json` inside the pinned `jellyfin/jellyfin:12.1.20260915-010956` image — it
matches exactly, so the "bind to Jellyfin's own copy" strategy (`ExcludeAssets=runtime`) is sound as shipped.

No security defects, injection vectors, or data-loss paths surfaced. The SQL is fully parameterized, shared
cache is never enabled, the legacy-import transaction correctly uses `BEGIN IMMEDIATE` so the `PRAGMA
user_version` marker commits and rolls back atomically with the imported rows, and no log message or test
fixture leaks a password, hash, or API key (verified with a dedicated test and by inspection of every
`[LoggerMessage]`).

What I found instead are four narrower correctness/quality issues: a cache-duration timing nuance in
`EmbyUserDirectory`, a shutdown-only `Dispose()` race on its semaphore, a test in
`EmbyVerifiedPasswordsTests.cs` whose core security assertion I proved (empirically, with a standalone repro)
does not actually observe the data it claims to check because of WAL + connection pooling, and an
under-broad exception filter in `EmbyVerifiedPasswords.EnsureInitialized` that could let a rare filesystem
exception crash plugin construction instead of degrading gracefully. None of these block the phase; all are
worth fixing.

## Warnings

### WR-01: `EmbyUserDirectory.GetStatusAsync` computes the cache-validity window from before the refresh, not after it

**File:** `src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs:61-84`
**Issue:** `now` is captured once at the top of the method, before the code decides whether a refresh is
needed and before it waits on `_refreshGuard`. The value captured there is reused both for the re-check
inside the guard and for computing `ValidUntil` on the new snapshot (`now + (users is null ? RetryDelay :
CacheDuration)`). If the Emby call (or the wait for another caller's in-flight Emby call) takes any
measurable time, the cache is effectively valid for `CacheDuration` minus however long the refresh took,
not for the full `CacheDuration` the class doc promises ("How long the plugin uses a user list before it
reads the list again"). This is bounded today by the 5-second HTTP timeout against a 60-second cache
duration, so the practical impact is small, but it is a real deviation from the documented contract and
would get worse if `RequestTimeout` or `CacheDuration` ever changed independently.
**Fix:**
```csharp
await _refreshGuard.WaitAsync(cancellationToken).ConfigureAwait(false);
try
{
    snapshot = _snapshot;
    var refreshNow = timeProvider.GetUtcNow(); // re-read after acquiring the guard
    if (snapshot is null || snapshot.ServerUrl != settings.ServerUrl || snapshot.ApiKey != settings.ApiKey || refreshNow >= snapshot.ValidUntil)
    {
        var users = await embyClient.GetUsersAsync(settings.ServerUrl, settings.ApiKey, cancellationToken).ConfigureAwait(false);
        snapshot = new Snapshot(settings.ServerUrl, settings.ApiKey, users, timeProvider.GetUtcNow() + (users is null ? RetryDelay : CacheDuration));
        ...
```

### WR-02: `EmbyUserDirectory.Dispose()` can race a concurrent `GetStatusAsync` and turn a graceful degradation into an unhandled exception

**File:** `src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs:65-83,100-101`
**Issue:** `Dispose()` calls `_refreshGuard.Dispose()` with no coordination against in-flight callers.
`SemaphoreSlim.Dispose()` does not cancel or fault a pending `WaitAsync()`, so a caller already blocked
there can still be granted the semaphore after disposal begins; when that caller's `finally` block then
calls `_refreshGuard.Release()`, `SemaphoreSlim` throws `ObjectDisposedException`. Per this repo's own
documented Jellyfin contract (`.claude/rules/plugin.md`: "Jellyfin catches only `AuthenticationException`
from a login method... any other exception escapes the login request and becomes an HTTP 500"), that
exception would propagate out of `GetStatusAsync` and out of `EmbyAuthenticationProvider.Authenticate` as an
unhandled exception, turning what should be a clean login refusal (or a graceful "unavailable") into an
HTTP 500 during the narrow window around plugin/host shutdown. This is a shutdown-only race with low
practical odds of firing on an in-flight login (the window is a single `Release()` call), but it is real and
provable from the API contract of `SemaphoreSlim`.
**Fix:** Either don't dispose the semaphore from an `IDisposable` that shares its lifetime with in-flight
async callers (drop `IDisposable` and let the process exit reclaim the handle, which is what a
`SemaphoreSlim(1,1)` singleton effectively needs), or guard `Release()` calls with a try/catch that treats
`ObjectDisposedException` as a no-op equivalent to "shutting down, no longer relevant."

### WR-03: `Database_DoesNotContainThePasswordHash` does not observe the data it claims to check, because of WAL + connection pooling

**File:** `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs:134-140`
**Issue:** This test calls `store.Record(...)` and then immediately reads `_databasePath` with
`File.ReadAllBytes`, asserting the raw hash fragment `"BBBB"` is absent. With `journal_mode=WAL` and
`Pooling=true` (both set by `EmbyVerifiedPasswords`), a write is durable in the `-wal` sibling file but is
**not** checkpointed into the main `.db` file until the last real connection to that database closes — and
pooling keeps the native connection open even after the C# `using` block disposes it. I confirmed this
empirically with a standalone repro against the same `Microsoft.Data.Sqlite` version (10.0.11): after an
insert through a pooled WAL connection, the main file stayed at its pre-insert header size (4096 bytes) and
contained none of the inserted text, while the `-wal` file (20,632 bytes) contained it in full. This test
does not call `SqliteConnection.ClearAllPools()` before reading the file — unlike the same test class's
sibling helpers elsewhere in this codebase (e.g. `EmbyAuthControllerTests.ValidFingerprintFileContents()`,
which explicitly does this and documents why). As written, the assertion would pass even if
`EmbyVerifiedPasswords.Record` were changed to store the raw password hash instead of its SHA-256
fingerprint, because the bytes it inspects are not the bytes SQLite actually wrote. The class's real
"never store the hash" coverage currently rests entirely on `NoLogEntryNamesAHashOrAFingerprint`, which
checks log entries, not the database file.
**Fix:**
```csharp
[Fact]
public void Database_DoesNotContainThePasswordHash()
{
    CreateStore().Record(Guid.NewGuid(), HashA);

    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); // force the WAL to checkpoint into the main file
    Assert.DoesNotContain("BBBB", Encoding.Latin1.GetString(File.ReadAllBytes(_databasePath)), StringComparison.Ordinal);
}
```

### WR-04: `EmbyVerifiedPasswords.EnsureInitialized`'s exception filter does not cover every exception the initialization path can throw

**File:** `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs:163-201`
**Issue:** `EnsureInitialized` runs synchronously from the constructor, which in turn runs synchronously
during DI singleton resolution (`PluginServiceRegistrator.RegisterServices`). Its catch clause is scoped to
`SqliteException or IOException or UnauthorizedAccessException`. `Directory.CreateDirectory` and the
`SqliteConnectionStringBuilder`/`SqliteConnection.Open()` path can also throw `ArgumentException` (e.g. a
path containing characters invalid on the host OS) or other exception types outside that list. Because this
runs during construction, an exception outside the filtered set is not "the store degrades gracefully and
logs an error" (the documented and tested behavior for every other failure mode in this class) — it is an
unhandled exception that aborts DI construction of `EmbyVerifiedPasswords`, which cascades into
`EmbyAuthenticationProvider`'s own registration failing, which is the entire plugin failing to register.
Today the path is always built from Jellyfin's own `PluginsPath`/`PluginConfigurationsPath`, so this is
low-probability, but the class's own design intent — "never let a store problem become anything worse than a
logged error and a refused login" — is not fully enforced by the code as written.
**Fix:** Widen the catch to `catch (Exception ex) when (ex is SqliteException or IOException or
UnauthorizedAccessException or ArgumentException)`, or catch `Exception` generically here specifically
because this method's entire job is "never let store setup take the plugin down," mirroring the
`CreateAccountAsync` cleanup path's `CA1031` justification pattern already used elsewhere in this codebase.

## Info

### IN-01: `EmbyClient.SignOutAsync`'s unreachable-exception filter does not cover a caller-triggered cancellation

**File:** `src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs:151-152,189-192`
**Issue:** `IsUnreachable` only swallows `TaskCanceledException` when `!cancellationToken.IsCancellationRequested`
(i.e., a timeout, not a real cancellation). If a caller ever threads a live `CancellationToken` into
`AuthenticateAsync` and that token fires while the Emby session is being signed out (after Emby already
accepted the login), the resulting `OperationCanceledException` propagates uncaught out of `SignOutAsync`
and `AuthenticateAsync`, and — per the same HTTP-500 contract cited in WR-02 — out of the login request
itself. This is pre-existing behavior, not introduced by this phase's reordering, and today's only caller
(`EmbyAuthenticationProvider.Authenticate`) always passes `CancellationToken.None`, so it is unreachable in
the current codebase. Flagging for awareness in case a future caller threads a real token through.
**Fix:** None needed unless a future change threads a cancellable token into `AuthenticateAsync`. If it
does, `SignOutAsync`'s catch should treat any cancellation the same as "the sign-out did not happen," never
letting it become an unhandled exception on the login response path.

### IN-02: `PluginServiceRegistrator`'s database path derivation is unguarded against an empty `Assembly.Location`

**File:** `src/Jellyfin.Plugin.EmbyAuth/PluginServiceRegistrator.cs:44-47`
**Issue:** `Path.GetFileNameWithoutExtension(typeof(EmbyVerifiedPasswords).Assembly.Location)` assumes
`Assembly.Location` is non-empty. For a normal Jellyfin plugin load from disk this holds, but
`Assembly.Location` is documented to return `string.Empty` for an assembly loaded from a byte array or in
some single-file/trimmed hosting scenarios. If that ever happens here, `Path.GetFileNameWithoutExtension("")`
returns `""`, and `Path.Combine(pluginsPath, "", dbFileName)` silently collapses to
`pluginsPath/dbFileName` — the database would land directly in the shared plugins directory instead of this
plugin's own subfolder, risking a collision with another plugin that also mishandles this edge case. Not
reachable under Jellyfin's normal plugin-loading model as documented in `.claude/rules/plugin.md`, and not
something this phase introduced (the derivation itself is described as intentionally matching
`BasePluginOfT.cs`'s own convention) — noting for completeness only.
**Fix:** Not urgent; if ever hardened, throw or log clearly if `Assembly.Location` is empty rather than
silently combining into the wrong directory.

---

_Reviewed: 2026-09-20T22:20:57Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
