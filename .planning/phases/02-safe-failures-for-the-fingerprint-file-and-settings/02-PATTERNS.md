# Phase 2: Safe Failures for the Fingerprint File and Settings - Pattern Map

**Mapped:** 2026-09-18
**Files analyzed:** 7
**Analogs found:** 6 / 7 (the `.gitignore` line has no analog — it is a one-line addition, not a pattern)

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|-----------------|----------------|
| `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs` (`Load()`) | service (file I/O) | file-I/O, CRUD | itself — `Record()` in the same file (lines 41-65) for the try/catch + `[LoggerMessage]` shape | exact (same file, same class, same catch-list convention) |
| `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html` (`.catch` on `pageshow` and `submit`) | component (inline script) | request-response | itself — `loadEmbyAuthMigration()` (lines 62-79) and the `Run migration now` handler (lines 95-104), both already `.catch`-wrapped | exact (same file, same inline-message convention) |
| `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs` (extend) | test | CRUD | `UnreadableFile_MatchesNothing_AndLogsAnError` (lines 100-108), same file | exact |
| `tests/js/configPage.test.js` | test | request-response, event-driven | no in-repo JS analog; closest structural analogs are `tests/scripts/package.bats` (non-C# suite wiring) and `TestDoubles.cs` (stub conventions) | role-match only — first JS test in the repo |
| `tests/js/package.json` | config | — | no analog (first `package.json` in repo); `Jellyfin.Plugin.EmbyAuth.Tests.csproj` is the closest "test project manifest" analog for exact-pin conventions | no analog |
| `.mise.toml` (`[tools]` + `[tasks.test]`) | config | — | itself — existing `dotnet` pin and existing `bats tests/scripts` line | exact |
| `.gitignore` | config | — | itself — existing `bin/`, `obj/` entries | exact |

## Pattern Assignments

### `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs` — `Load()` (service, file-I/O)

**Analog:** the same file's `Record()` method and the current (defective) `Load()` method.

**Current `Load()`** (lines 90-113, the exact code the plan diffs against):
```csharp
private Dictionary<Guid, string> Load()
{
    if (_fingerprints is not null)
    {
        return _fingerprints;
    }

    _fingerprints = [];
    if (!File.Exists(_filePath))
    {
        return _fingerprints;
    }

    try
    {
        _fingerprints = JsonSerializer.Deserialize<Dictionary<Guid, string>>(File.ReadAllText(_filePath)) ?? [];
    }
    catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
    {
        LogReadFailed(_logger, ex, _filePath);
    }

    return _fingerprints;
}
```
Defect: line 97 (`_fingerprints = []`) runs before the try/catch, so a caught exception at line 109 leaves the empty dictionary cached in the field for the process's life. D-02/D-03/D-04 require: stop pre-assigning the field before the read succeeds — build a local dictionary, assign `_fingerprints` only on a successful deserialize (or on "file does not exist," which is not a failure and should still cache `[]` per D-02's "retry only on failure" framing), and return a **local** empty dictionary on the catch path without touching the field.

**Exception-filter convention to copy** (same file, `Record()`, lines 60-63):
```csharp
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
{
    LogWriteFailed(_logger, ex, _filePath);
}
```
`Load()`'s own catch list (`JsonException or IOException or UnauthorizedAccessException`) stays unchanged per D-04 — do not split it.

**`[LoggerMessage]` convention to copy** (lines 115-119, bottom of class):
```csharp
[LoggerMessage(Level = LogLevel.Error, Message = "Jellyfin cannot read {FilePath}. The plugin moves no user to the Default login method until users log in again through Emby. The next record replaces the file.")]
private static partial void LogReadFailed(ILogger logger, Exception exception, string filePath);
```
This message text is now stale once D-02 lands ("The next record replaces the file" describes today's bug, not the fixed behavior) — the plan should update the message text to describe the retry-on-next-call behavior, keeping the `[LoggerMessage]` partial-method shape and the `{FilePath}` structured-logging placeholder unchanged.

**Locking convention:** both `Record()` (line 45) and `Matches()` (line 81) call `Load()` inside `lock (_lock)`. `Load()` itself must stay lock-free (it is always called from inside the lock already) — do not add a second lock.

---

### `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html` — `.catch` handlers (component, request-response)

**Analog:** the file's own two already-`.catch`-wrapped handlers.

**The exact inline-message element D-08 reuses** (line 48):
```html
<p id="EmbyAuthMigrationSummary"></p>
```

**Every line that currently writes to it — copy this convention exactly (`.textContent`, never `.innerHTML`):**

`loadEmbyAuthMigration()` (lines 62-79, success path line 67-69, failure path line 76-78):
```javascript
function loadEmbyAuthMigration() {
    var summary = document.querySelector('#EmbyAuthMigrationSummary');
    var list = document.querySelector('#EmbyAuthMigrationUsers');
    return ApiClient.getJSON(ApiClient.getUrl('EmbyAuth/Migration')).then(function (status) {
        var ready = status.Users.filter(function (user) { return user.ReadyToMove; }).length;
        summary.textContent = status.Users.length === 0
            ? 'No users are on the Emby login method.'
            : 'Users on the Emby login method: ' + status.Users.length + '. Ready to move: ' + ready + '.';
        list.replaceChildren();
        status.Users.forEach(function (user) {
            var item = document.createElement('li');
            item.textContent = user.Name + ': ' + (user.ReadyToMove ? 'ready' : 'needs one login while Emby runs');
            list.appendChild(item);
        });
    }).catch(function () {
        summary.textContent = 'Jellyfin cannot read the migration status. See the Jellyfin log.';
    });
}
```

`Run migration now` click handler (lines 95-104):
```javascript
document.querySelector('#EmbyAuthRunMigration')
    .addEventListener('click', function () {
        var summary = document.querySelector('#EmbyAuthMigrationSummary');
        ApiClient.ajax({ type: 'POST', url: ApiClient.getUrl('EmbyAuth/Migration/Run') }).then(function () {
            summary.textContent = 'The migration runs. This list updates in a few seconds.';
            setTimeout(loadEmbyAuthMigration, 3000);
        }).catch(function () {
            summary.textContent = 'Jellyfin did not start the migration. See the Jellyfin log.';
        });
    });
```

**The exact convention to apply:** `var summary = document.querySelector('#EmbyAuthMigrationSummary'); ... .catch(function () { summary.textContent = '<fixed string, no interpolation of config fields>'; });` — one `.catch` per promise chain, message assigned via `.textContent` to the same `#EmbyAuthMigrationSummary` element (D-08: reuse the element, add no new DOM node), and the string is always a literal, never built from `config.EmbyServerUrl` / `config.EmbyApiKey` (per `.claude/rules/plugin.md` §Settings and the security note in RESEARCH.md).

**Current defective `pageshow` handler** (lines 81-93) — the `.catch` target for D-06:
```javascript
document.querySelector('#EmbyAuthConfigPage')
    .addEventListener('pageshow', function () {
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(EmbyAuthConfig.pluginUniqueId).then(function (config) {
            document.querySelector('#EmbyServerUrl').value = config.EmbyServerUrl;
            document.querySelector('#EmbyApiKey').value = config.EmbyApiKey;
            document.querySelector('#MigrationMode').value = config.MigrationMode;
            document.querySelector('#AccountAccess').value = config.AccountAccess;
            return loadEmbyAuthMigration();
        }).finally(function () {           // no .catch before this .finally today
            Dashboard.hideLoadingMsg();
        });
    });
```
D-06 target: add `.catch(...)` before `.finally(...)` that writes the message to `#EmbyAuthMigrationSummary` and sets `document.querySelector('.button-submit').disabled = true`. `.button-submit` (line 40) is the only element with that class — `document.querySelector('.button-submit')` is unambiguous, per RESEARCH.md.

**Current defective `submit` handler** (lines 106-121) — the `.catch` target for D-07:
```javascript
document.querySelector('#EmbyAuthConfigForm')
    .addEventListener('submit', function (e) {
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(EmbyAuthConfig.pluginUniqueId).then(function (config) {
            config.EmbyServerUrl = document.querySelector('#EmbyServerUrl').value;
            config.EmbyApiKey = document.querySelector('#EmbyApiKey').value;
            config.MigrationMode = document.querySelector('#MigrationMode').value;
            config.AccountAccess = document.querySelector('#AccountAccess').value;
            ApiClient.updatePluginConfiguration(EmbyAuthConfig.pluginUniqueId, config).then(function (result) {
                Dashboard.processPluginConfigurationUpdateResult(result);
            });                              // no .catch anywhere in this handler today
        });

        e.preventDefault();
        return false;
    });
```
D-07 target: add `.catch(...)` to both the outer `getPluginConfiguration` re-fetch and the inner `updatePluginConfiguration` call (or one `.catch` on the combined chain if the plan flattens it), writing to `#EmbyAuthMigrationSummary`. Note the submit handler currently has no `Dashboard.hideLoadingMsg()` call at all — flag this as a pre-existing gap if the plan touches this handler's structure, but it is out of this phase's D-06/D-07 scope unless the `.catch` addition needs a `.finally` to match the `pageshow` handler's shape.

---

### `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs` (test, CRUD)

**Analog:** `UnreadableFile_MatchesNothing_AndLogsAnError` (lines 100-108), in the same file.

```csharp
[Fact]
public void UnreadableFile_MatchesNothing_AndLogsAnError()
{
    File.WriteAllText(_filePath, "not json");
    var logger = new CapturingLogger<EmbyVerifiedPasswords>();

    Assert.False(CreateStore(logger).Matches(Guid.NewGuid(), HashA));
    Assert.Contains(logger.Entries, entry => entry.StartsWith("Error:", StringComparison.Ordinal));
}
```

D-05 extends this pattern with the second half: after the failed `Matches`, call `Record` and assert the file on disk holds original bytes were not silently reset, and/or assert the retry actually reads real content once the file is fixed. Copy the file-per-test setup already established at the top of the class:
```csharp
private readonly string _filePath = Path.Combine(Path.GetTempPath(), $"emby-auth-tests-{Guid.NewGuid():N}.json");

public void Dispose()
{
    File.Delete(_filePath);
    File.Delete(_filePath + ".tmp");
}

private EmbyVerifiedPasswords CreateStore(ILogger<EmbyVerifiedPasswords>? logger = null) =>
    new(_filePath, logger ?? NullLogger<EmbyVerifiedPasswords>.Instance);
```
Use `File.ReadAllText(_filePath)` to assert the original bytes, following `File_DoesNotContainThePasswordHash` (lines 83-89) for the string-content-assertion style: `Assert.DoesNotContain("BBBB", File.ReadAllText(_filePath), StringComparison.Ordinal);` — swap for whatever positive assertion proves the pre-failure bytes survived (e.g. `Assert.Equal(originalBytes, File.ReadAllText(_filePath))` captured before the induced failure).

**`CapturingLogger<T>` stub** (`TestDoubles.cs`, lines 72-87) — the established stub-naming convention (`Capturing*`, `Fake*`, `Stub*` prefixes) that `tests/js` should mirror for its own stub naming (see below).

---

### `tests/js/configPage.test.js`, `tests/js/package.json` (test / config — first JavaScript in the repo)

**No in-repo JS analog exists.** Two partial analogs, per the mapping context's specific ask:

**1. `tests/scripts/` wiring — how a non-C# suite is registered, for `tests/js` to mirror:**

`.mise.toml` (lines 28-33, current):
```toml
[tasks.test]
description = "Build with warnings as errors, then run the unit tests and the script tests"
run = [
  "dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx",
  "bats tests/scripts",
]
```
D-09's target: add exactly one line, `"node --test"` (no path argument — RESEARCH.md Pitfall 1), to this `run` array. No new `[tools]` entry pattern to copy beyond adding `node = "24.21.0"` alongside the existing `dotnet = "10.0.401"` line (line 2).

`.github/workflows/ci.yml` — the `test` job (lines 34-47) needs **no change**; it already runs `mise run test`, so the new `node --test` line rides along with no new job and no new step. `ci-success` (lines 65-82) needs no change either, since it depends on the existing `[lint, test, e2e]` list.

`.pre-commit-config.yaml` — the `test` hook's `files` pattern (`\.(cs|csproj|slnx|props|html)$|^global\.json$|^\.mise\.toml$|^scripts/|^tests/scripts/`) already matches `.html`, so no change is needed there either; a `configPage.html` edit already triggers `mise run test` locally, confirmed by reading the file.

**2. `TestDoubles.cs` stub-naming and arrangement conventions, for the jsdom `ApiClient`/`Dashboard` stubs to mirror:**

The established convention (from `TestDoubles.cs`, lines 22-257) is: one small hand-written stub class per collaborator, named `Stub*` (network-shaped: `StubHttpMessageHandler`, `StubHttpClientFactory`) or `Fake*` (business-logic-shaped: `FakeUserManager`, `FakeCryptoProvider`), each implementing only the members the plugin actually calls and throwing `NotImplementedException` (C#) or simply omitting (JS) any member not exercised — e.g. `FakeUserManager` implements exactly `CreateUserAsync`, `UpdateUserAsync`, `DeleteUserAsync` with real bodies and configurable `*Throws` properties (lines 104-114) to force a failure path, and every other interface member throws `NotImplementedException`. `CapturingLogger<T>` (lines 72-87) is the "records what happened, exposes it as a list" stub shape — mirrored by `RESEARCH.md`'s `buildDom({ getConfigFails: false })` helper, which the same way exposes a boolean flag that flips a stubbed method between `Promise.resolve()` and `Promise.reject()`.

Applying this convention to `tests/js`, the `ApiClient`/`Dashboard` stub objects (built in `testHelpers.js` per RESEARCH.md's recommended structure) should be object literals with exactly the methods `configPage.html` calls (`getUrl`, `getJSON`, `ajax`, `getPluginConfiguration`, `updatePluginConfiguration` on `ApiClient`; `showLoadingMsg`, `hideLoadingMsg`, `processPluginConfigurationUpdateResult` on `Dashboard`), each a plain function, with failure toggled by flags passed into a `buildDom({ getConfigFails, updateConfigFails })`-shaped factory — this is the direct JS analog of `FakeUserManager`'s `CreateUserThrows` property pattern.

**Test file naming convention:** the C# suite uses one file per tested type (`EmbyVerifiedPasswordsTests.cs` tests `EmbyVerifiedPasswords`), so `configPage.test.js` (one file for the one HTML file under test) matches that convention directly — RESEARCH.md's Assumption A1 already identifies this.

**Suite structure to copy from `EmbyVerifiedPasswordsTests.cs`:** `IDisposable`-per-test-fixture cleanup (lines 18-22) has no JS equivalent needed (jsdom windows are GC'd, no file state), but the **one-assertion-class-per-behavior** naming style (`UnreadableFile_MatchesNothing_AndLogsAnError`, `Records_SurviveARestart`) is worth carrying into the `node:test` `test('...', ...)` description strings for readability and CI-log parity.

---

### `.mise.toml` (config)

**Current `[tools]` block** (lines 1-10) — the exact place to add the `node` pin, following the existing one-tool-per-line, `name = "exact.version"` convention (no ranges, per repo's "pin exact versions" rule):
```toml
[tools]
dotnet = "10.0.401"
bats = "1.14.0"
jq = "1.8.2"
shellcheck = "0.11.0"
shfmt = "3.14.1"
actionlint = "1.7.12"
zizmor = "1.30.1"
prek = "0.5.3"
act = "0.2.89"
```
Add `node = "24.21.0"` here (RESEARCH.md's verified pin).

**Current `[tasks.test]`** — see above. Add `"node --test"` as a third array element.

---

### `.gitignore` (config)

**Current full contents:**
```
bin/
obj/
artifacts/
TestResults/
```
Add `node_modules/` as a fifth line, matching the existing one-directory-per-line, trailing-slash convention.

## Shared Patterns

### Inline-message-on-failure (client-side)
**Source:** `configPage.html:76-78` and `:101-103`
**Apply to:** the new `pageshow` `.catch` (D-06) and `submit` `.catch` (D-07)
```javascript
.catch(function () {
    summary.textContent = 'Jellyfin cannot read the migration status. See the Jellyfin log.';
});
```
Always `.textContent`, never `.innerHTML`; always a fixed string, never interpolating `config.EmbyServerUrl` / `config.EmbyApiKey` (`.claude/rules/plugin.md` §Settings, §API and settings page).

### Fail-closed-on-read-failure, cache-only-on-success (server-side)
**Source:** `EmbyVerifiedPasswords.cs:90-113` (the method being changed) and its sibling `Record()` (lines 41-65) for the surrounding try/catch + `[LoggerMessage]` shape
**Apply to:** `Load()` only, per D-02/D-03/D-04
```csharp
catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
{
    LogReadFailed(_logger, ex, _filePath);
}
```
Keep the combined catch filter (D-04: no split); change only what happens to `_fingerprints` around it.

### `mise run test` composition — one line per suite, no new CI job
**Source:** `.mise.toml:28-33`, `.github/workflows/ci.yml:34-47`
**Apply to:** the `node --test` addition (D-09)
```toml
run = [
  "dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx",
  "bats tests/scripts",
  "node --test",
]
```
This is the exact place CLAUDE.md's "a CI step change is made in the mise task, not only the workflow" rule applies — `ci.yml`'s `test` job body is unchanged (`run: mise run test`, line 47), only the task's `run` array grows.

## No Analog Found

| File | Role | Data Flow | Reason |
|------|------|-----------|--------|
| `tests/js/configPage.test.js` | test | event-driven (DOM events) | First JavaScript file in the repository; no jsdom/`node:test` precedent exists. RESEARCH.md's empirically-verified Code Examples (Patterns 1-3) are the primary source for this file, not an in-repo analog. |
| `tests/js/package.json` | config | — | First `package.json` in the repository; RESEARCH.md's "Installation" section (`npm init -y`, `npm pkg set type=commonjs`, `npm install --save-exact jsdom@28.1.0`) is the source, not an in-repo analog. |

## Metadata

**Analog search scope:** `src/Jellyfin.Plugin.EmbyAuth/`, `src/Jellyfin.Plugin.EmbyAuth/Configuration/`, `tests/Jellyfin.Plugin.EmbyAuth.Tests/`, `tests/scripts/`, repo root config files (`.mise.toml`, `.github/workflows/ci.yml`, `.pre-commit-config.yaml`, `.gitignore`)
**Files scanned:** `EmbyVerifiedPasswords.cs`, `configPage.html`, `EmbyVerifiedPasswordsTests.cs`, `TestDoubles.cs`, `.mise.toml`, `.github/workflows/ci.yml`, `.gitignore`, `.pre-commit-config.yaml`, `tests/scripts/` listing
**Pattern extraction date:** 2026-09-18
