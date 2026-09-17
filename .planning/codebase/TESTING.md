# Testing Patterns

**Analysis Date:** 2026-09-16

**Scope:** commit `ecee1ed` on `main`.

## Test Framework

**Unit Test Runner:**
- Framework: xUnit v3, package `xunit.v3` `4.0.1` (`tests/Jellyfin.Plugin.EmbyAuth.Tests/Jellyfin.Plugin.EmbyAuth.Tests.csproj:10`).
- Runner: Microsoft.Testing.Platform, selected in `global.json:3`. Every command that runs the unit tests passes `--solution Jellyfin.Plugin.EmbyAuth.slnx` (`.mise.toml:31`).
- The test project is an executable (`OutputType` `Exe`, `IsPackable` `false`). It references `Jellyfin.Controller` `12.1.0` and the plugin project (`Jellyfin.Plugin.EmbyAuth.Tests.csproj:4-5`, `:9`, `:14`).
- `Directory.Build.props` sets `net10.0`, nullable reference types, and warnings as errors. The test project does not set `AnalysisMode`.
- No mocking library and no coverage package. The two package references are `Jellyfin.Controller` and `xunit.v3`.

**Script and E2E Test Runner:**
- bats 1.14.0, pinned in `.mise.toml:3`.

**Run Commands:**
```bash
mise run test                                          # Build (warnings are errors), run the unit tests, then bats tests/scripts
mise run e2e                                           # bats e2e: all e2e files (needs Docker)
bats e2e/10-login-checks.bats                          # Run one e2e file
dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx   # Only the unit tests
prek run                                               # Pre-commit hooks: mise run lint and mise run test
act pull_request -j test                               # Run a CI job in a local container
```
Sources: `.mise.toml:28-41`, `CLAUDE.md:9-13`, `docs/development.md:5-15`. The `act` e2e job also needs `--bind --container-options "--network host"` (`CLAUDE.md:13`, `docs/development.md:13`).

**Measured result:** on 2026-09-16 at `ecee1ed`, `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` reported total 90, succeeded 90, failed 0, skipped 0, duration 786 ms. The bats suites were not run for this analysis.

## Test File Organization

**Location:**
- Unit tests: `tests/Jellyfin.Plugin.EmbyAuth.Tests/`, flat, with no subdirectories.
- Script tests: `tests/scripts/package.bats`, for `scripts/package.sh`.
- E2E tests: `e2e/NN-topic.bats` (see End-to-End Tests).

**Unit test files** — one for each tested source type, named `<Type>Tests.cs`:

| Test file | Tested type | `[Fact]` | `[Theory]` (data rows) | Test cases |
|---|---|---|---|---|
| `AccountAccessPolicyTests.cs` | `AccountAccessPolicy` | 3 | 4 (11) | 14 |
| `EmbyAuthSettingsTests.cs` | `EmbyAuthSettings`, `PluginConfiguration` defaults | 6 | 4 (11) | 17 |
| `EmbyClientTests.cs` | `EmbyClient` | 12 | 5 (13) | 25 |
| `EmbyUserDirectoryTests.cs` | `EmbyUserDirectory` | 9 | 2 (6) | 15 |
| `EmbyVerifiedPasswordsTests.cs` | `EmbyVerifiedPasswords` | 8 | 1 (3) | 11 |
| `LoginDecisionTests.cs` | `LoginDecision` | 5 | 1 (3) | 8 |
| **Total** | | **43** | **17 (47)** | **90** |

- A `[Theory]` runs once for each `[InlineData]` row. So 60 test methods give 90 test cases.
- `TestDoubles.cs` holds the shared test doubles (see Mocking).
- `git diff --stat 0dc87e4 ecee1ed -- tests/Jellyfin.Plugin.EmbyAuth.Tests` is empty: the unit tests did not change when the migration API was added.

**Types without unit tests:**
- `EmbyAuthenticationProvider`, `DefaultLoginMethod`, `MoveToDefaultLoginMethod`, `MoveEmbyUsersToDefaultTask`, `EmbyLoginMethodUsers`, `Api/EmbyAuthController`, `EmbyAuthPlugin`, `PluginServiceRegistrator`.
- These types use `IUserManager`, `ICryptoProvider`, `JellyfinDbContext`, `ITaskManager`, or the Jellyfin plugin host. `TestDoubles.cs` has no double for any of these. The e2e tests cover this behavior.

**Naming:**
- Classes: `public class <Type>Tests` (`LoginDecisionTests.cs:5`). `EmbyVerifiedPasswordsTests` is `public sealed class` and implements `IDisposable` (`EmbyVerifiedPasswordsTests.cs:11`).
- A method name is a PascalCase sentence split by underscores: an optional subject, the outcome, then a `When...` condition.
  - `CreatesAccount_WhenNoJellyfinAccountExists` (`LoginDecisionTests.cs:11`)
  - `Denies_WhenTypedNameIsNotExactlyTheEmbyName` (`LoginDecisionTests.cs:42`)
  - `Login_ReturnsNull_WhenEmbyTimesOut` (`EmbyClientTests.cs:170`)
  - `NewAccount_NoLibraries_RemovesLibraryAccess` (`AccountAccessPolicyTests.cs:49`)
  - `Rejects_UrlWithCredentials_WithoutRepeatingThem` (`EmbyAuthSettingsTests.cs:89`)
- A name can give the reason: `Login_SendsBodyWithContentLength_BecauseEmbyRejectsChunkedBodies` (`EmbyClientTests.cs:89`).
- In `EmbyClientTests.cs`, the prefix `Login_` or `Users_` names the `EmbyClient` method under test.
- Bats tests (e2e and script) are named with a lowercase sentence that states the expected behavior: `"check-tag refuses a tag for another version"` (`tests/scripts/package.bats:72`), `"Run migration moves the users who are ready and keeps the others"` (`e2e/30-migration-modes.bats:112`).

**Namespace:**
- File-scoped `namespace Jellyfin.Plugin.EmbyAuth.Tests;`. Each file has an explicit `using Xunit;` (`LoginDecisionTests.cs:1`).

## Test Structure

**Fact (one case), from `LoginDecisionTests.cs:10-16`:**
```csharp
[Fact]
public void CreatesAccount_WhenNoJellyfinAccountExists()
{
    var action = LoginDecision.Decide("alice", typedAccount: null, embyUserName: "alice", Bridge);

    Assert.Equal(LoginAction.CreateAccount, action);
}
```

**Theory (one case per data row), from `LoginDecisionTests.cs:38-47`:**
```csharp
[Theory]
[InlineData("alice ")]
[InlineData(" alice")]
[InlineData("alice@example.com")]
public void Denies_WhenTypedNameIsNotExactlyTheEmbyName(string typedName)
{
    var action = LoginDecision.Decide(typedName, typedAccount: null, "alice", Bridge);

    Assert.Equal(LoginAction.Deny, action);
}
```

**Arrange, act, assert:**
- Blank lines separate the three blocks. No comments label them (`LoginDecisionTests.cs:19-26`).
- The act step is usually one line, assigned to `action`, `login`, `users`, or `status`.

**Setup and teardown:**
- xUnit creates a new test class instance for each test. Per-test state is in instance field initializers: `_logger` (`EmbyClientTests.cs:20`), `_clock` (`EmbyUserDirectoryTests.cs:18`), `_filePath` with a new GUID (`EmbyVerifiedPasswordsTests.cs:16`).
- Cleanup is in `Dispose`: `EmbyVerifiedPasswordsTests` deletes the temporary file and its `.tmp` file (`EmbyVerifiedPasswordsTests.cs:18-22`).
- No shared fixtures: `IClassFixture`, `ICollectionFixture`, and `IAsyncLifetime` are not used.

## Mocking

**Framework:**
- Hand-written test doubles in `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs`. No mocking library.

**StubHttpMessageHandler (`TestDoubles.cs:13-47`):**
- An `HttpMessageHandler` with a queue of response factories. `Then(Func<HttpResponseMessage>)` adds a factory and returns the handler, so calls chain.
- Records each request in `Requests` as a `RecordedRequest`: method, URI, `Authorization` header, `X-Emby-Token` header, body, and `Content-Length` (`TestDoubles.cs:11`, `:27-35`).
- Throws `InvalidOperationException` if a request arrives and no response is queued (`TestDoubles.cs:37-40`).
- A factory that throws simulates a network failure: `.Then(() => throw new HttpRequestException("Connection refused"))` (`EmbyClientTests.cs:161`), `.Then(() => throw new TaskCanceledException("Timed out"))` (`EmbyClientTests.cs:172`).

Request inspection, from `EmbyClientTests.cs:78-84`:
```csharp
var login = handler.Requests[0];
Assert.Equal(HttpMethod.Post, login.Method);
Assert.Equal(new Uri("http://emby:8096/Users/AuthenticateByName"), login.Uri);
Assert.StartsWith("MediaBrowser ", login.Authorization, StringComparison.Ordinal);
Assert.Contains("DeviceId=", login.Authorization, StringComparison.Ordinal);
using var body = JsonDocument.Parse(login.Body!);
Assert.Equal("alice", body.RootElement.GetProperty("Username").GetString());
```

**StubHttpClientFactory (`TestDoubles.cs:49-52`):**
- An `IHttpClientFactory` that returns `new HttpClient(handler, disposeHandler: false)`. `EmbyClient` disposes its `HttpClient` after each call (`EmbyClient.cs:54`, `:109`), and the handler stays usable for the next call and for the assertions.

**ManualTimeProvider (`TestDoubles.cs:54-61`):**
- A `TimeProvider` that starts at 2026-01-01 00:00 UTC and moves only when a test calls `Advance(TimeSpan)`.
- Tests move the clock to exactly, or one second before, `EmbyUserDirectory.CacheDuration` (60 s) or `RetryDelay` (30 s) (`EmbyUserDirectoryTests.cs:68`, `:81`, `:127`, `:143`).

**CapturingLogger<T> (`TestDoubles.cs:63-78`):**
- An `ILogger<T>` that stores each entry as `"{logLevel}: {message} {exception}"` in a `ConcurrentQueue<string>`, exposed as `Entries`. `IsEnabled` returns `true` for every level.
- Used to check that no secret is logged (`EmbyClientTests.cs:36-43`), that an Error entry exists (`entry.StartsWith("Error:", StringComparison.Ordinal)` at `EmbyVerifiedPasswordsTests.cs:107`), and that nothing is logged (`EmbyVerifiedPasswordsTests.cs:97`).
- A test that does not check the log uses `NullLogger<T>.Instance` (`EmbyUserDirectoryTests.cs:29`, `EmbyVerifiedPasswordsTests.cs:25`).

**What is replaced:**
- HTTP to Emby (`StubHttpMessageHandler`, `StubHttpClientFactory`).
- The clock (`ManualTimeProvider`).
- The logger, when a test checks log output (`CapturingLogger<T>`).
- In the script tests, the release timestamp, the download URL base, and the output folder, through environment variables that `scripts/package.sh` reads (`tests/scripts/package.bats:6-9`, `scripts/package.sh:10-13`).

**What is real:**
- JSON serialization and parsing in `EmbyClient`, including malformed bodies and an invalid charset (`EmbyClientTests.cs:179-207`, `:258-268`).
- The file system: `EmbyVerifiedPasswordsTests` writes a real file in `Path.GetTempPath()` (`EmbyVerifiedPasswordsTests.cs:16`).
- Jellyfin's `User` entity, with `AddDefaultPermissions` and `AddDefaultPreferences` (`AccountAccessPolicyTests.cs:12-18`).
- `EmbyClient` inside the `EmbyUserDirectory` tests: the directory gets a real `EmbyClient` over the stub handler (`EmbyUserDirectoryTests.cs:28-29`).
- Settings validation and the `PluginConfiguration` defaults (`EmbyAuthSettingsTests.cs`).
- In the script tests, a real `dotnet publish`, `zip`, `jq`, and `openssl` run (`tests/scripts/package.bats:11`, `scripts/package.sh:56`, `:80`, `:83`).

## Fixtures and Factories

**Config factory, from `EmbyAuthSettingsTests.cs:9-19`:**
```csharp
private static PluginConfiguration Config(
    string? url = "http://emby:8096",
    string? apiKey = "0123456789abcdef",
    MigrationMode mode = MigrationMode.MoveAfterFirstLogin,
    AccountAccess access = AccountAccess.CopyEmbyRemoteAccess) => new()
    {
        EmbyServerUrl = url!,
        EmbyApiKey = apiKey!,
        MigrationMode = mode,
        AccountAccess = access,
    };
```

**User factory, from `AccountAccessPolicyTests.cs:12-18`:**
```csharp
private static User JellyfinDefaultUser()
{
    var user = new User("alice", "provider", "reset-provider");
    user.AddDefaultPermissions();
    user.AddDefaultPreferences();
    return user;
}
```

**Response factories:**
- `Json(status, json)`, `LoginAccepted(name, token, remoteAccess)`, and `Status(status)` in `EmbyClientTests.cs:22-31`; `UserList()` in `EmbyUserDirectoryTests.cs:20-26`. They build `HttpResponseMessage` objects. Optional parameters with defaults let each test set only the values it checks.

**Constants:**
- Test data is in `const` or `static readonly` fields: `Password`, `ApiKey`, `EmbyUrl` (`EmbyClientTests.cs:16-18`); `Settings` (`EmbyUserDirectoryTests.cs:15-16`); `HashA`, `HashB` (`EmbyVerifiedPasswordsTests.cs:13-14`); `Bridge`, `DefaultProvider` (`LoginDecisionTests.cs:7-8`).

**Location:**
- Private static members of each test class. The only shared file is `TestDoubles.cs`.

## Coverage

**Requirements:**
- No coverage tool and no coverage threshold (measured: `rg -i 'coverlet|opencover|--collect'` finds nothing outside `.planning/`).
- The repo rules: write the test first, then break the code once and watch the test fail (`CLAUDE.md:43`). A change that depends on Jellyfin or Emby behavior needs an end-to-end test, and `mise run e2e` runs for every change to `src/` (`CLAUDE.md:44`). Every plugin API controller keeps an e2e test that a regular user gets 403 (`.claude/rules/plugin.md:48`).
- CI runs the unit tests, the script tests, and the e2e tests on every pull request and on push to `main`; `ci-success` fails unless all jobs succeed (`.github/workflows/ci.yml:5-9`, `:34-81`).

**What the unit tests cover:**
- Account rules: create, use, case-insensitive match, deny on a different Emby name or another login method (`LoginDecisionTests.cs`).
- Settings validation: missing, non-http, or credential-bearing URL; missing API key; unknown enum values; defaults (`EmbyAuthSettingsTests.cs`).
- Emby HTTP: request shape, body with `Content-Length`, base path, sign-out, rejected status, unreachable server, timeout, unreadable body, invalid charset, user list parsing, no secret in logs (`EmbyClientTests.cs`).
- User list cache: exact name match that ignores only case, disabled users, cache duration, refresh when the URL or API key changes, retry delay, no stale list after a failed refresh (`EmbyUserDirectoryTests.cs`).
- Fingerprint file: match, replace, restart, no hash in the file, missing file, unreadable file, 50 concurrent records (`EmbyVerifiedPasswordsTests.cs`).
- Account access: copy of Emby remote access, no libraries, existing accounts never gain remote access (`AccountAccessPolicyTests.cs`).

**What the script tests cover (`tests/scripts/package.bats`):**
- `package.sh` with no action or an unknown action prints the usage and exits 2 (`:20-30`).
- `build` prints the zip path; the zip holds only the plugin DLL and `meta.json` (`:32-40`).
- `meta.json` fields: GUID, name, version, `targetAbi` `12.1.0.0`, status, `autoUpdate`, timestamp, assemblies (`:42-52`).
- `manifest.json`: one plugin, one version, the MD5 checksum of the zip, and the download URL (`:54-65`).
- `check-tag` accepts `v<version>` and refuses another version (`:67-76`).

**What only the e2e tests cover:**
- The login flow in `EmbyAuthenticationProvider`, the move to Default, the migration task, Quick Connect, password set and reset in Jellyfin, and the check of the Jellyfin log.
- The migration API and `EmbyLoginMethodUsers`: the ready state per user, 403 for a non-administrator on `GET /EmbyAuth/Migration` and `POST /EmbyAuth/Migration/Run`, and a 204 run that moves only ready users (`e2e/30-migration-modes.bats:95-120`).
- The settings page JavaScript has no automated test. `e2e/10-login-checks.bats:15-18` checks only that Jellyfin serves the page (HTTP 200).

## Test Types

**Unit Tests:**
- Scope: one type, in process, with the doubles above. `EmbyUserDirectoryTests` also runs the real `EmbyClient`.
- The whole suite ran in 786 ms (see the measured result above).
- No separate integration test project.

**Script Tests:**
- Scope: `scripts/package.sh` as a black box. `setup_file` runs one real `build` into `$BATS_FILE_TMPDIR`, then each test checks the output (`tests/scripts/package.bats:2-12`).
- Framework: bats. `mise run test` runs them after the unit tests (`.mise.toml:32`), so the `test` hook, the CI `test` job, and the release workflow all run them (`.pre-commit-config.yaml:10-15`, `ci.yml:34-47`, `release.yml:37-38`).
- The build calls `dotnet publish`, `zip`, `jq`, and `openssl`, and the tests call `unzip` and `openssl` (`scripts/package.sh:56`, `:60`, `:80`, `:83`; `tests/scripts/package.bats:37`, `:55`). `zip`, `unzip`, and `openssl` are not pinned in `.mise.toml` (`.mise.toml:1-10`).

**End-to-End Tests:**
- Scope: the published plugin in a real Jellyfin container, against a real Emby container, through an nginx proxy that logs each request body (`e2e/compose.yaml:9-15`).
- Framework: bats. Needs Docker. CI runs them in the `e2e` job with a 30-minute timeout (`ci.yml:49-62`).
- Images: `emby/embyserver:4.10.0.40`, `nginx:1.30.5-alpine`, `jellyfin/jellyfin:12.1.20260915-010956` (`e2e/compose.yaml:5`, `:11`, `:18`).

## Common Patterns

**Async testing, from `EmbyUserDirectoryTests.cs:61-72`:**
```csharp
[Fact]
public async Task UsesCachedList_WithinCacheDuration()
{
    var handler = new StubHttpMessageHandler().Then(UserList);
    var directory = CreateDirectory(handler);

    await directory.GetStatusAsync(Settings, "alice", CancellationToken.None);
    _clock.Advance(EmbyUserDirectory.CacheDuration - TimeSpan.FromSeconds(1));
    await directory.GetStatusAsync(Settings, "ivy", CancellationToken.None);

    Assert.Single(handler.Requests);
}
```
- Test methods are `async Task`. Calls pass `CancellationToken.None`.
- A test counts the recorded HTTP requests to prove that the cache avoided a call.

**Error testing, from `EmbyClientTests.cs:143-156`:**
```csharp
[Theory]
[InlineData(HttpStatusCode.Unauthorized)]
[InlineData(HttpStatusCode.Forbidden)]
[InlineData(HttpStatusCode.InternalServerError)]
public async Task Login_ReturnsNull_WhenEmbyRejectsLogin(HttpStatusCode status)
{
    var handler = new StubHttpMessageHandler().Then(() => Status(status));

    var login = await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", Password, CancellationToken.None);

    Assert.Null(login);
    Assert.Single(handler.Requests);
    AssertNoSecretsLogged();
}
```
- The test checks the return value and the side effects: the requests sent and the log entries.

**Secret check, from `EmbyClientTests.cs:38-42`:**
```csharp
Assert.All(_logger.Entries, entry =>
{
    Assert.DoesNotContain(Password, entry, StringComparison.Ordinal);
    Assert.DoesNotContain(ApiKey, entry, StringComparison.Ordinal);
});
```

**Assertions:**
- `Assert.Single` returns the element: `var logout = Assert.Single(handler.Requests.Skip(1));` (`EmbyClientTests.cs:125`).
- `Assert.Equal` compares records and collections: `Assert.Equal([new EmbyUser("alice", IsDisabled: false), new EmbyUser("ivy", IsDisabled: true)], users);` (`EmbyClientTests.cs:231`).
- `Assert.Contains(collection, predicate)` (`EmbyVerifiedPasswordsTests.cs:107`).
- String assertions pass `StringComparison.Ordinal` (`EmbyClientTests.cs:81-82`, `EmbyAuthSettingsTests.cs:95`).

**Concurrency:**
- `ConcurrentRecords_AreAllKept` runs 50 `Record` calls with `Task.Run` and `Task.WhenAll`. Then a new store instance must match all 50 users (`EmbyVerifiedPasswordsTests.cs:110-120`).

**Bats assertions:**
- `run` captures a command, then `[ "$status" -eq N ]` and `[[ "$output" == *"text"* ]]` check it (`tests/scripts/package.bats:21-23`).
- JSON checks use `jq -r` with an equality test (`tests/scripts/package.bats:44-51`, `e2e/30-migration-modes.bats:16-18`).

## End-to-End Tests

**Files (`rg -c '^@test'`):**

| File | Tests | Topic |
|---|---|---|
| `e2e/10-login-checks.bats` | 10 | Default settings: settings page, first login, blank password, a name that is not an Emby user, extra spaces, disabled Emby user, an account on the Default login method, Emby session sign-out |
| `e2e/20-accounts.bats` | 6 | Accounts created before the first login, remote access copy, administrator refusal, Quick Connect, password set and reset in Jellyfin |
| `e2e/30-migration-modes.bats` | 7 | `KeepEmbyInCharge`, the migration task, `JellyfinPasswordFirst`, `NoLibraries`, `JellyfinDefaults`, the migration status API with its 403 check, and "Run migration" (`:29`, `:45`, `:60`, `:77`, `:87`, `:95`, `:112`) |
| `e2e/40-emby-outage.bats` | 3 | Logins while Emby and the proxy are stopped |
| `e2e/90-jellyfin-log.bats` | 1 | No test password and no API key in the Jellyfin log |

- Total: 27 e2e tests, plus 8 script tests in `tests/scripts/package.bats`.
- Support files: `e2e/setup_suite.bash`, `e2e/helpers.bash`, `e2e/compose.yaml`, `e2e/emby-proxy.conf`.

**Suite setup (`e2e/setup_suite.bash`, runs once for all files):**
- Publishes the plugin to `artifacts/plugin` with `dotnet publish -c Release` (`setup_suite.bash:12`). Compose mounts that folder as the Jellyfin plugin folder (`compose.yaml:25`).
- Removes old containers and volumes, starts new ones, and waits up to 180 seconds for each server (`setup_suite.bash:13-16`, `helpers.bash:49-61`).
- Completes both startup wizards, logs in both administrators, creates an Emby API key, and sets the plugin settings to the proxy URL `http://emby-proxy:8096` and that key (`setup_suite.bash:18-26`).
- Creates every Emby user: 21 users with passwords (`alice` to `wes`), and `frank` without a password (`setup_suite.bash:29-32`). Removes remote access from `gina` and `quinn`, and disables `ivy` (`setup_suite.bash:33-35`).
- `teardown_suite` removes the containers and volumes unless `KEEP_E2E=1` (`setup_suite.bash:38-44`).

**Per file:**
- `setup_file` loads the helpers, calls `reset_plugin_config`, and creates the Jellyfin accounts that the file needs (`10-login-checks.bats:4-9`, `20-accounts.bats:5-13`, `30-migration-modes.bats:4-10`).
- `setup` loads the helpers before each test (`10-login-checks.bats:11-13`).
- `teardown_file` restores shared state. `40-emby-outage.bats` starts Emby and the proxy again and resets the plugin settings (`40-emby-outage.bats:19-24`). `30-migration-modes.bats` resets the plugin settings (`30-migration-modes.bats:20-23`).
- A file can define its own small helpers: `wes_is_on_default` and `migration_ready_state` (`30-migration-modes.bats:12-18`). An asynchronous result is awaited with `wait_until` and such a predicate (`30-migration-modes.bats:116`).
- A file must not depend on another file (`.claude/rules/e2e.md:11`).

**Helpers (`e2e/helpers.bash`):**
- HTTP: `api` (prints the body, fails on HTTP 400 or higher, `:25-33`), `status` (prints only the status code, `:35-43`), `login_status`, `login_token`.
- Setup: `complete_startup_wizard`, `create_user`, `set_password`, `update_policy`, `set_login_method`, `precreate_on_emby_method`, `precreate_admin_on_emby_method`, `create_emby_api_key`, `reset_plugin_config`, `set_plugin_config`.
- Checks: `policy_field`, `user_by_name`, `emby_user_id`, `jellyfin_user_id`, `emby_login_requests`, `quick_connect_status`, `run_migration_task`.
- Waiting: `wait_until DESCRIPTION COMMAND...` retries every 2 seconds for at most 180 seconds (`:49-61`); `emby_ready` and `jellyfin_ready` (`:63-70`).
- `emby_login_requests NAME` counts the login requests for NAME in the `emby-proxy` log. It first sends a marker request and waits up to 20 seconds for it, so the count includes all earlier requests (`helpers.bash:193-214`, `.claude/rules/e2e.md:21`).
- Host ports: `EMBY_PORT` and `JELLYFIN_PORT`, default 18096 and 28096, set the ports in `helpers.bash` and `compose.yaml` (`helpers.bash:6-10`, `compose.yaml:7`, `:23`, `.claude/rules/e2e.md:24`).

Assertions after the first login of `alice`, from `e2e/10-login-checks.bats:24-28`:
```bash
# Also proves that the proxy log records the plugin's login requests.
[ "$(emby_login_requests alice)" = "1" ]
[ "$(policy_field alice AuthenticationProviderId)" = "$DEFAULT_PROVIDER" ]
[ "$(policy_field alice IsAdministrator)" = "false" ]
[ "$(policy_field alice EnableRemoteAccess)" = "true" ]
```

**Conventions (`.claude/rules/e2e.md`):**
- Create each Emby user in `setup_suite.bash`, never in a test file, because the plugin caches the Emby user list for 60 seconds (`e2e.md:18`).
- Each test file uses its own user names (`e2e.md:19`).
- Test passwords follow the patterns `NAME-emby-pass[-N]`, `NAME-jf-pass`, and `NAME-jf-random` (`e2e.md:20`). `90-jellyfin-log.bats` finds a leaked password with a regex over these patterns, which also covers the administrator passwords and one more test password form (`90-jellyfin-log.bats:16`).
- Use `emby_login_requests` to prove whether a login reached Emby. Do not use the Emby activity log, because Emby writes its entries late (`e2e.md:21-22`).
- Jellyfin runs at Debug level (`compose.yaml:21`). `90-jellyfin-log.bats` fails if the log has no `[DBG]` entries (`90-jellyfin-log.bats:10-13`).
- `scripts/dev-env.sh` also uses `compose.yaml` and `helpers.bash`. After a change to either file, run `scripts/dev-env.sh up` and `down` (`e2e.md:13`).
- Scripts must pass `shellcheck -x` and `shfmt -d`. In a test loop, use `if [[ ... ]]; then ...; return 1; fi` (`e2e.md:25`). `mise run lint` enforces both on `e2e/`, `scripts/`, and `tests/scripts/` (`.mise.toml:22-23`).

**Demo environment:**
- `scripts/dev-env.sh` is not a test. It reuses `e2e/compose.yaml` and `e2e/helpers.bash` with the compose project name `emby-auth-dev` and host ports 18196 and 28196, so it can run next to the e2e containers (`scripts/dev-env.sh:4-5`, `:17-22`; `docs/development.md:25`).

## Pre-Commit and CI Testing

**Hooks (`.pre-commit-config.yaml`, run with `prek run`):**
- `lint`: `mise run lint` — `dotnet format --verify-no-changes`, `shellcheck -x`, `shfmt -d`, `actionlint`, `zizmor --offline` — when a `.cs`, `.csproj`, `.slnx`, `.props`, `.sh`, `.bash`, `.bats`, `.yml`, or `.yaml` file, or `.mise.toml`, changes (`.pre-commit-config.yaml:4-9`, `.mise.toml:18-26`).
- `test`: `mise run test` — `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx`, then `bats tests/scripts` — when a `.cs`, `.csproj`, `.slnx`, `.props`, or `.html` file, `global.json`, `.mise.toml`, or anything under `scripts/` or `tests/scripts/` changes (`.pre-commit-config.yaml:10-15`, `.mise.toml:28-33`).
- No hook runs the e2e tests. The repo rules require `mise run e2e` for every change to `src/` (`CLAUDE.md:44`).

**CI (`.github/workflows/ci.yml`):**
- Triggers: every pull request, and push to `main` (`:5-9`).
- Jobs `lint`, `test`, and `e2e` each install the pinned tools with `jdx/mise-action` and run one mise task (`:19-62`). `ci-success` needs all three and fails if any result is not `success` (`:64-81`).
- A local run of `mise run lint`, `mise run test`, and `mise run e2e` matches CI (`docs/development.md:17`, `.mise.toml:16`).

**Release (`.github/workflows/release.yml`):**
- On a pushed `v*` tag: `scripts/package.sh check-tag`, `mise run test`, `mise run package`, then `gh release create` (`:6-9`, `:32-48`). The unit and script tests run again before the package is built.

---

*Testing analysis: 2026-09-16*
