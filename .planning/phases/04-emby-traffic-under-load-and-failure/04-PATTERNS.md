# Phase 4: The Fingerprint Store, and Emby Traffic Under Failure - Pattern Map

**Mapped:** 2026-09-20
**Files analyzed:** 9 (in scope after the 2026-09-20 scope cut; k6/toxiproxy/load-test files are out of scope and excluded)
**Analogs found:** 9 / 9

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---|---|---|---|---|
| `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs` (rewritten: JSON file → SQLite) | service / model (durable per-user record store) | CRUD (record + point lookup), lock-free read | `src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs` (lock-free-read shape); `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs:329` `SqliteJellyfinDbContextFactory` (SQLite connection lifecycle only, not a store seam) | role-match (read pattern exact; storage engine is new in-repo) |
| `src/Jellyfin.Plugin.EmbyAuth/PluginServiceRegistrator.cs` (edited: swap file-path const/registration for the new store) | config / DI wiring | request-response (singleton construction) | itself, current `:25,33` | exact (same file, small edit) |
| `src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj` (edited: add `Microsoft.Data.Sqlite.Core` 10.0.11) | config | n/a | itself, current `:10-15` (`Jellyfin.Controller`/`Jellyfin.Model` `ExcludeAssets` idiom) | exact |
| `src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs` (edited: single-flight guard around `:55-83`) | service | CRUD/cache refresh, event-driven coordination | itself — no other in-repo coordination primitive exists (see below) | no analog for the guard itself; the surrounding cache shape is self-analogous |
| `src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs` (edited: reorder sign-out ahead of user-name check, `:87-95`) | service (HTTP client to Emby) | request-response | itself, current `:87-96` and `:165-186` (`SignOutAsync`) | exact (same file, small reorder) |
| `e2e/60-concurrent-logins.bats` (new) | test (e2e) | request-response, event-driven (concurrent bursts) | `e2e/40-emby-outage.bats` | exact (same `NN-topic.bats` shape: `setup_file`/`teardown_file`, its own users, `reset_plugin_config`) |
| `e2e/70-invalid-settings.bats` (new) | test (e2e) | request-response, CRUD (settings save) | `e2e/40-emby-outage.bats` (file shape); `e2e/90-jellyfin-log.bats` (log-read pattern to extract into a helper) | exact (file shape) / role-match (log helper) |
| `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs` (rewritten for the new store) | test (unit) | CRUD, concurrency | itself, current full file, especially `:169-178` `ConcurrentRecords_AreAllKept` | exact (same file, same shape, new backing store) |
| `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyClientTests.cs` (edited: split the `[Theory]` at `:179-192`) | test (unit) | request-response | itself, current `:179-192` | exact |

## Pattern Assignments

### `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs` (service/model, CRUD + lock-free read)

**Analog for the lock-free read path:** `src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs`

The repo's only existing precedent for "readers take no lock of the plugin's own" is `EmbyUserDirectory`'s `volatile` snapshot swap. D-14 requires exactly this property for `Matches`/reads after the SQLite migration, so this is the pattern to copy, not the storage mechanism.

**Volatile snapshot field** (`EmbyUserDirectory.cs:46`):
```csharp
private volatile Snapshot? _snapshot;
```

**Immutable snapshot record** (`EmbyUserDirectory.cs:88`):
```csharp
private sealed record Snapshot(Uri ServerUrl, string ApiKey, IReadOnlyList<EmbyUser>? Users, DateTimeOffset ValidUntil);
```

**Read-without-lock, refresh-on-miss shape** (`EmbyUserDirectory.cs:55-69`):
```csharp
public async Task<EmbyUserStatus> GetStatusAsync(EmbyAuthSettings settings, string username, CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(settings);
    var snapshot = _snapshot;
    var now = timeProvider.GetUtcNow();
    if (snapshot is null || snapshot.ServerUrl != settings.ServerUrl || snapshot.ApiKey != settings.ApiKey || now >= snapshot.ValidUntil)
    {
        var users = await embyClient.GetUsersAsync(settings.ServerUrl, settings.ApiKey, cancellationToken).ConfigureAwait(false);
        snapshot = new Snapshot(settings.ServerUrl, settings.ApiKey, users, now + (users is null ? RetryDelay : CacheDuration));
        _snapshot = snapshot;
        ...
    }
    ...
}
```
This snapshot-swap shape is a cache with an expiry, not a durable store — for the new `EmbyVerifiedPasswords`, the read-without-lock property is what transfers; the underlying "refresh" step becomes a SQLite `SELECT` rather than an HTTP call, and there is no expiry to model (every `Record()` commit is durable immediately, per D-14/D-16, so there is no "stale snapshot" window to reason about the way `EmbyUserDirectory` has one).

**What the current `EmbyVerifiedPasswords.cs` does today** (being replaced — do not copy the `Lock`-around-I/O shape; copy only the log-message and API-surface conventions):

Current class shape (`EmbyVerifiedPasswords.cs:18-34`):
```csharp
public sealed partial class EmbyVerifiedPasswords
{
    private readonly string _filePath;
    private readonly ILogger<EmbyVerifiedPasswords> _logger;
    private readonly Lock _lock = new();
    private Dictionary<Guid, string>? _fingerprints;

    public EmbyVerifiedPasswords(string filePath, ILogger<EmbyVerifiedPasswords> logger)
    {
        _filePath = filePath;
        _logger = logger;
    }
```
The constructor's `(path, logger)` shape and the public method names — `Record(Guid userId, string passwordHash)`, `Matches(Guid userId, string? passwordHash)`, `RecordsAvailable()` — are the surface every call site (`EmbyLoginMethodUsers.cs:103`, `EmbyAuthenticationProvider.cs`, `LoginMethodMove.cs`) depends on. Keep the method signatures; replace only the body and the constructor's first parameter (file path → connection/database path, or a factory).

**Fingerprint hashing helper, unchanged** (`EmbyVerifiedPasswords.cs:109-110`):
```csharp
private static string Fingerprint(string passwordHash) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash)));
```

**`[LoggerMessage]` partial convention to preserve** (`EmbyVerifiedPasswords.cs:142-146`):
```csharp
[LoggerMessage(Level = LogLevel.Error, Message = "Jellyfin cannot read {FilePath}. ...")]
private static partial void LogReadFailed(ILogger logger, Exception exception, string filePath);

[LoggerMessage(Level = LogLevel.Error, Message = "Jellyfin cannot write {FilePath}. ...")]
private static partial void LogWriteFailed(ILogger logger, Exception exception, string filePath);
```
D-14's durability fix removes most of the reasons these fire (a per-row commit either succeeds or throws immediately), so these two messages need rewording for the SQLite failure modes (e.g., a locked database, a corrupt file), not a verbatim carry-over — but the `[LoggerMessage]` partial-method convention, parameter naming, and "Error level, never a password or a hash" rule (`.claude/rules/plugin.md` §Verified passwords) carry over unchanged.

**SQLite connection lifecycle to copy** (`tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs:338-358`, `SqliteJellyfinDbContextFactory`) — **not a seam for this store** (it backs `JellyfinDbContext`/EF Core for `LoginMethodMove.cs:41`'s `ExecuteUpdateAsync`), but its open/create-schema/dispose shape is the closest in-repo precedent for how the new store's own connection is opened and torn down:
```csharp
public SqliteJellyfinDbContextFactory()
{
    _connection = new SqliteConnection("DataSource=:memory:");
    _connection.Open();
    _options = new DbContextOptionsBuilder<JellyfinDbContext>()
        .UseSqlite(_connection)
        .Options;

    using var context = CreateDbContext();
    context.Database.EnsureCreated();
}

public JellyfinDbContext CreateDbContext() => new(...);

public void Dispose() => _connection.Dispose();
```
The new store is plain `Microsoft.Data.Sqlite` (no EF Core — D-14/addendum never mentions `Microsoft.EntityFrameworkCore.Sqlite` for the plugin's own store, only for Jellyfin's `JellyfinDbContext`), so copy the *shape* — open a connection, ensure schema, implement `IDisposable`/`ClearAllPools()` per D-16 — not the EF Core machinery itself.

**csproj `ExcludeAssets` idiom to copy verbatim** (`Jellyfin.Plugin.EmbyAuth.csproj:9-16`):
```xml
<ItemGroup>
  <PackageReference Include="Jellyfin.Controller" Version="12.1.0">
    <ExcludeAssets>runtime</ExcludeAssets>
  </PackageReference>
  <PackageReference Include="Jellyfin.Model" Version="12.1.0">
    <ExcludeAssets>runtime</ExcludeAssets>
  </PackageReference>
</ItemGroup>
```
The new reference joins this `ItemGroup` at version 10.0.11:
```xml
<PackageReference Include="Microsoft.Data.Sqlite.Core" Version="10.0.11">
  <ExcludeAssets>runtime</ExcludeAssets>
</PackageReference>
```

---

### `src/Jellyfin.Plugin.EmbyAuth/PluginServiceRegistrator.cs` (config/DI, edited)

**Analog:** itself, current shape.

**Construction and singleton registration to copy the shape of** (`PluginServiceRegistrator.cs:22-35`):
```csharp
public const string VerifiedPasswordsFileName = "Jellyfin.Plugin.EmbyAuth.VerifiedPasswords.json";

...
serviceCollection.AddSingleton(services => new EmbyVerifiedPasswords(
    Path.Combine(services.GetRequiredService<IApplicationPaths>().PluginConfigurationsPath, VerifiedPasswordsFileName),
    services.GetRequiredService<ILogger<EmbyVerifiedPasswords>>()));
```
Per D-14, the new database lives in `BasePlugin.DataFolderPath`, not `PluginConfigurationsPath` — the new registration swaps the path source (`IApplicationPaths.PluginsPath`-derived `DataFolderPath`, per the addendum's citation of `BasePluginOfT.cs`) and the filename constant (a `.db` extension), but keeps the `AddSingleton(services => new EmbyVerifiedPasswords(path, logger))` factory-delegate shape exactly.

---

### `src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs` (single-flight guard, edited)

**No existing coordination primitive found in this repo.** A repo-wide search for `SemaphoreSlim`, `Interlocked`, `Monitor.TryEnter`, and similar found none outside `EmbyVerifiedPasswords`'s own `Lock` (a synchronous, non-`await`-spanning lock, explicitly not usable here per D-14/D-18's `await`-under-lock constraint and the cited Microsoft doc on `SemaphoreSlim.WaitAsync` for coordination across an `await`). State this plainly rather than inventing an analog: **the guard has no in-repo precedent**; the plan should cite the external source already recorded in `04-CONTEXT.md`'s canonical refs (`SemaphoreSlim` with `WaitAsync` plus an explicit double-check after acquiring, since `ConcurrentDictionary.GetOrAdd`/`IMemoryCache.GetOrCreateAsync` are not stampede-safe).

**The surrounding cache shape to preserve** (`EmbyUserDirectory.cs:55-69`, same excerpt as above) is the site the guard wraps — the guard must keep the existing `volatile Snapshot?` read-without-lock property for callers that find a *fresh* snapshot; only the refresh branch (the `if` body that calls `embyClient.GetUsersAsync`) gets the single-flight `SemaphoreSlim` + double-check.

---

### `src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs` (sign-out reorder, edited)

**Analog:** itself, current shape.

**Current order — sign-out unreachable for a token-bearing, no-name response** (`EmbyClient.cs:87-95`):
```csharp
var embyUserName = login?.User?.Name;
if (string.IsNullOrEmpty(embyUserName))
{
    LogResponseWithoutUserName(logger, baseUrl);
    return null;
}

await SignOutAsync(client, baseUrl, embyUserName, login?.AccessToken, cancellationToken).ConfigureAwait(false);
return new EmbyLogin(embyUserName, login?.User?.Policy?.EnableRemoteAccess ?? false);
```

**`SignOutAsync`'s existing no-op-on-empty-token guard, unchanged and load-bearing for the reorder's safety** (`EmbyClient.cs:165-170`):
```csharp
private async Task SignOutAsync(HttpClient client, Uri baseUrl, string embyUserName, string? accessToken, CancellationToken cancellationToken)
{
    if (string.IsNullOrEmpty(accessToken))
    {
        return;
    }
    ...
```
Because `SignOutAsync` already no-ops on a null/empty token, moving the call ahead of the name check is safe for every existing response shape (`{}`, `"not json"` never reach it with a token; the token-bearing no-name row now does). D-01/D-02's reorder logs the fallback name via the **typed** `username` parameter (never a secret) when `embyUserName` is null — keep `LogResponseWithoutUserName`'s existing `[LoggerMessage]` shape and the "never log a secret" convention already enforced by `AssertNoSecretsLogged()` in the test file.

---

### `e2e/60-concurrent-logins.bats` and `e2e/70-invalid-settings.bats` (new)

**Analog:** `e2e/40-emby-outage.bats` (full file shape); `e2e/90-jellyfin-log.bats` (log-read pattern, to extract into `e2e/helpers.bash` per the discretion decision already settled in `04-CONTEXT.md`).

**`setup_file`/`teardown_file` shape to copy** (`e2e/40-emby-outage.bats:4-22`):
```bash
setup_file() {
	load helpers
	reset_plugin_config
	precreate_on_emby_method sam
	precreate_on_emby_method vic
	...
}

teardown_file() {
	load helpers
	docker compose -f "$COMPOSE_FILE" start emby emby-proxy >&3 2>&1
	wait_until Emby emby_ready
	reset_plugin_config
}

setup() {
	load helpers
}
```
Both new files copy this shape: `setup_file` calls `load helpers` then `reset_plugin_config`, uses only Emby users this file itself owns (per `.claude/rules/e2e.md` "Give each test file its own user names" — new names must be added to `e2e/setup_suite.bash:30-32`'s user loop, not created inline in the bats file). `70-invalid-settings.bats` has no service to stop, so it needs no `teardown_file`; it should still call `reset_plugin_config` at the top of `setup_file` per Pitfall 3 in RESEARCH.md (never build a settings payload from a possibly-stale fetched config).

**Existing helpers already sufficient, no new machinery needed** (`e2e/helpers.bash:85-88, 159-170`):
```bash
login_status() {
	local base="$1" user="$2" password="$3"
	status POST "$base/Users/AuthenticateByName" "" "$(credentials_json "$user" "$password")"
}

reset_plugin_config() {
	set_plugin_config "$JF_TOKEN" \
		".MigrationMode = \"MoveAfterFirstLogin\" | .AccountAccess = \"CopyEmbyRemoteAccess\" | .MigrationTarget = \"$DEFAULT_PROVIDER\" | .PasswordSetTarget = \"\""
}

set_plugin_config() {
	local token="$1" filter="$2"
	local config
	config="$(api GET "$JELLYFIN/Plugins/$PLUGIN_ID/Configuration" "$token" | jq -c "$filter")"
	api POST "$JELLYFIN/Plugins/$PLUGIN_ID/Configuration" "$token" "$config" >/dev/null
}
```
`70-invalid-settings.bats`'s four cases each call `set_plugin_config "$JF_TOKEN" '.EmbyServerUrl = "..."'` (or `.EmbyApiKey = "..."`) after a `reset_plugin_config`, exactly this jq-filter pattern.

**Inline Jellyfin-log read to extract into a shared helper** (`e2e/90-jellyfin-log.bats:9`):
```bash
logs="$(docker compose -f "$COMPOSE_FILE" logs jellyfin 2>&1)"
```
Per the discretion decision already settled in `04-CONTEXT.md` ("The Jellyfin-log reader that D-07 needs becomes a shared helper in `e2e/helpers.bash`"), add a `jellyfin_log_contains PATTERN` function to `e2e/helpers.bash` modeled on this line plus a `grep` check, so `70-invalid-settings.bats`'s four cases call one helper instead of four inline `docker compose logs` calls.

**bats fd-3-safe backgrounding, required for `60-concurrent-logins.bats`'s burst** (no in-repo analog exists for a backgrounded burst — every existing `.bats` file runs its assertions serially with `run`). The plan must write this from the external sources already cited in `04-CONTEXT.md`/RESEARCH.md (bats-core docs on fd 3; bats-core#419), not from an in-repo pattern:
```bash
( login_status "$JELLYFIN" burst1 burst1-emby-pass 3>&- ) >"$BATS_TEST_TMPDIR/result-$i" &
pids+=($!)
...
for pid in "${pids[@]}"; do wait "$pid"; done
```
Avoid `run` on the backgrounded call (it captures through fd 3); redirect to a file instead, close fd 3 in the subshell, and `wait` every PID before the test function returns.

---

### `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs` (rewritten for the new store)

**Analog:** itself, current full file (140+ lines), especially the concurrency test.

**Store construction convention to preserve** (`EmbyVerifiedPasswordsTests.cs:24-33`):
```csharp
private readonly string _filePath = Path.Combine(Path.GetTempPath(), $"emby-auth-tests-{Guid.NewGuid():N}.json");

public void Dispose()
{
    File.Delete(_filePath);
    File.Delete(_filePath + ".tmp");
    ...
}

private EmbyVerifiedPasswords CreateStore(ILogger<EmbyVerifiedPasswords>? logger = null) =>
    new(_filePath, logger ?? NullLogger<EmbyVerifiedPasswords>.Instance);
```
This `CreateStore(logger?)` factory-helper convention, and a per-test unique temp path cleaned up in `Dispose`, carries over directly — swap the `.json` temp path for a `.db` temp path (or an in-memory SQLite connection string, per the addendum's still-open item on how the new store is faked; a per-test temp-file database is the closer analog to the current test's own "survives a restart" tests, since an in-memory database cannot prove durability across a new store instance).

**The concurrency test that must survive the store change, exact excerpt** (`EmbyVerifiedPasswordsTests.cs:169-178`):
```csharp
[Fact]
public async Task ConcurrentRecords_AreAllKept()
{
    var store = CreateStore();
    var userIds = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToArray();

    await Task.WhenAll(userIds.Select(id => Task.Run(() => store.Record(id, HashA))));

    var reloaded = CreateStore();
    Assert.All(userIds, id => Assert.True(reloaded.Matches(id, HashA)));
}
```
This test's shape (50 concurrent `Record()` calls via `Task.Run`/`Task.WhenAll`, then a **new store instance** proving durability) transfers unchanged in intent to the SQLite store — it is the direct regression proof that concurrent writes do not corrupt or drop records under the new engine, and RESEARCH.md's own (now-superseded) load-test plan explicitly proposed extending this exact test with a `Stopwatch` for a microbenchmark that is no longer in scope; keep the test as a correctness check only.

**Tests that assert the specific failure modes D-14 removes** (`EmbyVerifiedPasswordsTests.cs:100-138`, `MissingFile_...`, `UnreadableFile_...`) do not have a direct SQLite equivalent — a missing/corrupt `.db` file and a locked database are different failure shapes than a missing/malformed JSON file. Keep the *pattern* (one test per failure mode, asserting on `CapturingLogger` entries via `entry.StartsWith("Error:", ...)`) but write new failure-mode tests specific to SQLite (e.g., a locked file, a schema mismatch) rather than porting the JSON-specific ones verbatim.

**`CapturingLogger<T>` convention, unchanged** — used throughout for asserting the reworded log messages; no new test double needed for this class beyond the store itself.

---

### `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyClientTests.cs` (theory split, edited)

**Analog:** itself, current shape.

**The theory that must split, exact excerpt** (`EmbyClientTests.cs:179-192`):
```csharp
[Theory]
[InlineData("{}")]
[InlineData("""{"User":{"Name":""},"AccessToken":"t"}""")]
[InlineData("not json")]
public async Task Login_ReturnsNull_WhenEmbyResponseHasNoUserName(string json)
{
    var handler = new StubHttpMessageHandler().Then(() => Json(HttpStatusCode.OK, json));

    var login = await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", Password, CancellationToken.None);

    Assert.Null(login);
    Assert.Single(handler.Requests);
    AssertNoSecretsLogged();
}
```
Per D-01/D-02, the three rows stop agreeing after the reorder: `{}` and `"not json"` carry no `AccessToken` and must keep `Assert.Single(handler.Requests)` (no sign-out possible, D-02's documented limit). The `{"User":{"Name":""},"AccessToken":"t"}` row carries a token and must move to a new `[Fact]` asserting **two** requests (login, then sign-out) — this is the row the fix is for.

**`StubHttpMessageHandler` fixture convention to reuse for the new fact** (`EmbyClientTests.cs:194-206`, the existing charset test, same shape):
```csharp
[Fact]
public async Task Login_ReturnsNull_WhenEmbyResponseHasAnInvalidCharset()
{
    var handler = new StubHttpMessageHandler().Then(() =>
    {
        var response = Json(HttpStatusCode.OK, """{"User":{"Name":"alice"},"AccessToken":"t"}""");
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=bogus");
        return response;
    });

    var login = await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", Password, CancellationToken.None);

    Assert.Null(login);
}
```
The new fact for the token-bearing no-name row follows this single-`[Fact]`-with-a-`StubHttpMessageHandler`-fixture pattern, asserting `Assert.Equal(2, handler.Requests.Count)` (or the repo's existing multi-request assertion idiom — check `AliceUserList`/`AliceAuthenticateResponse`-style two-step handlers elsewhere in the file for the exact idiom) instead of `Assert.Single`.

## Shared Patterns

### `[LoggerMessage]` partial methods, Error level, never a secret
**Source:** `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs:142-146`, `EmbyUserDirectory.cs:85-86`
**Apply to:** every new/edited log call in this phase — the rewritten store's read/write-failure messages, the reworded `LogCreateAccountFailed` (D-04), and `LogSettingsInvalid` (D-07/D-08).
```csharp
[LoggerMessage(Level = LogLevel.Error, Message = "...")]
private static partial void LogSomething(ILogger logger, Exception exception, string context);
```
Rule: parameters carry names/paths/counts only, never a password, hash, URL with credentials, or API key (`.claude/rules/plugin.md` §Settings: "Settings messages never repeat the configured values").

### Hand-written test doubles, no mocking library
**Source:** `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs` (`FakeUserManager:101`, `CapturingLogger<T>`, `SqliteJellyfinDbContextFactory:329`)
**Apply to:** any new seam the SQLite-store tests need. Per Phase 1 D-09 (cited in `04-CONTEXT.md` canonical refs) and the addendum's own open item, the new store's test seam is a hand-written temp-file (or in-memory) construction inside `EmbyVerifiedPasswordsTests.cs` itself, following `CreateStore()`'s existing shape — not a new entry in `TestDoubles.cs`, since the store is concrete and constructible directly, the same way `EmbyVerifiedPasswordsTests.cs` already does today.

### `bats` `NN-topic.bats` file shape
**Source:** `e2e/40-emby-outage.bats`, `e2e/90-jellyfin-log.bats`
**Apply to:** both new e2e files — `setup_file` (`load helpers`; `reset_plugin_config`; any file-owned setup), `setup()` (`load helpers`), optional `teardown_file`, `@test` blocks using `run` for simple calls and the fd-3-safe backgrounding pattern only for the concurrent burst.

### Jellyfin-core lock/exception facts (background, not code to copy)
**Source:** `.claude/rules/plugin.md` §Jellyfin login flow — "All logins for unknown names share one lock key, `Guid.Empty`"; "Jellyfin catches only `AuthenticationException` from a login method... any other exception escapes and becomes HTTP 500."
**Apply to:** D-03's unit test (`FakeUserManager.CreateUserThrows = new ArgumentException(...)`, already present at `EmbyAuthenticationProviderTests.cs:196-207`) and D-06's e2e invariant assertions (every response is 200 or 401, never 500).

## No Analog Found

| File/Change | Role | Data Flow | Reason |
|---|---|---|---|
| Single-flight guard in `EmbyUserDirectory.cs` (D-18) | service, coordination primitive | event-driven (collapse concurrent refreshes to one) | No `SemaphoreSlim`, `Interlocked`, or double-checked-lock pattern exists anywhere else in `src/`. Build from the external source already cited in `04-CONTEXT.md` (`SemaphoreSlim.WaitAsync` + explicit double-check after acquiring), not from an in-repo analog. |
| SQLite failure-mode unit tests for the new store (locked file, schema mismatch) | test (unit) | CRUD, error handling | The current `MissingFile_...`/`UnreadableFile_...` tests are JSON-specific (`JsonException`, a truncated JSON string); SQLite's failure shapes differ (`SqliteException` for a locked/corrupt file). Keep the one-test-per-failure-mode pattern; write new bodies. |

## Metadata

**Analog search scope:** `src/Jellyfin.Plugin.EmbyAuth/`, `tests/Jellyfin.Plugin.EmbyAuth.Tests/`, `e2e/`, project root config files (`.csproj`, `.claude/rules/*.md`).
**Files scanned:** `EmbyVerifiedPasswords.cs`, `EmbyUserDirectory.cs`, `EmbyClient.cs`, `EmbyAuthenticationProvider.cs`, `PluginServiceRegistrator.cs`, `Jellyfin.Plugin.EmbyAuth.csproj`, `TestDoubles.cs`, `EmbyVerifiedPasswordsTests.cs`, `EmbyClientTests.cs`, `EmbyAuthenticationProviderTests.cs`, `e2e/40-emby-outage.bats`, `e2e/90-jellyfin-log.bats`, `e2e/setup_suite.bash`, `e2e/helpers.bash`.
**Pattern extraction date:** 2026-09-20
**Scope note:** k6/toxiproxy/load-test/account-pool/`docs/performance.md` files described in the bulk of `04-RESEARCH.md` are out of scope per the 2026-09-20 planning-session scope cut (see `04-CONTEXT.md` "SUPERSEDED" notice on D-10 through D-13) and are excluded from this pattern map entirely.
