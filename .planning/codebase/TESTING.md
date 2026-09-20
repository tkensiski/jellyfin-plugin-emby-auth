---
last_mapped_commit: fb0fd999638d88fabb37bd9d449c23226a5b2f8b
---

# Testing Patterns

<!-- refreshed: 2026-09-20 -->

**Analysis Date:** 2026-09-20

**Scope:** commit `fb0fd99` on `gsd/phase-04-emby-traffic-under-load-and-failure`.

## Test Framework Overview

Three independent test suites cover the plugin:
1. **Unit tests** (`tests/Jellyfin.Plugin.EmbyAuth.Tests/`): xUnit v3 with test doubles, ~3,350 lines
2. **Settings page tests** (`tests/js/`): Node.js `node:test` with jsdom, ~1,425 lines
3. **End-to-end tests** (`e2e/`): bats with Docker containers, ~745 lines

**Run Commands:**
```bash
mise run test                                          # All tests: build, unit tests, settings-page tests
mise run e2e                                           # E2E tests only (needs Docker)
bats e2e/NN-topic.bats                                 # One e2e file
dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx   # Unit tests only
prek run                                               # Pre-commit hooks
```

## Unit Tests (xUnit v3)

**Runner:** Microsoft.Testing.Platform (selected in `global.json:3`)
- **Framework:** xUnit v3 (`xunit.v3` `4.0.1`)
- **Command:** `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx`
- **Configuration:** Executable test project (`.csproj:OutputType` `Exe`, `IsPackable` `false`). References `Jellyfin.Controller` `12.1.0` and `Microsoft.EntityFrameworkCore.Sqlite` `10.0.11`.
- **Parallelism:** Default xUnit v3 parallelism (runs tests in parallel by default). No `[Collection]` attributes found; each test class runs independently.
- **Skip markers:** None — no tests carry `[Skip = "..."]`.

**Test doubles in `TestDoubles.cs`:**

| Double | Type | Responsibility |
|--------|------|---|
| `StubHttpMessageHandler` | HttpMessageHandler | Records HTTP requests sent by the plugin. Enqueues stub responses via `.Then(Func<HttpResponseMessage>)` and returns them in order. Exposes `Requests` list of `RecordedRequest` (method, URI, headers, body, content-length). |
| `StubHttpClientFactory` | IHttpClientFactory | Creates a single `HttpClient` over the stub handler. |
| `ManualTimeProvider` | TimeProvider | Provides controllable time. Start value: 2026-01-01. Method `Advance(TimeSpan)` moves time forward. Used by tests that exercise cache expiration. |
| `CapturingLogger<T>` | ILogger<T> | Thread-safe queue of log entries. Format: `"{LogLevel}: {message} {exception}"`. All log levels enabled (`IsEnabled` always true). Exposes `Entries` enumerable. |
| `FakeUserManager` | IUserManager | Implements only `CreateUserAsync`, `UpdateUserAsync`, `DeleteUserAsync`, `GetAuthenticationProviders`. Each can throw a configured exception or succeed. Tracks call order in `Calls` list, and records last values created/updated/deleted. Throws `NotImplementedException` for all other members. |
| `FakeCryptoProvider` | ICryptoProvider | Implements only `CreatePasswordHash`: returns a deterministic hash (method `"fake"`, bytes of password). Throws `NotImplementedException` for all other members. |
| `FakeJellyfinDatabaseProvider` | IJellyfinDatabaseProvider | No-op implementation for all methods. Used to wire in the real SQLite factory. |
| `SqliteJellyfinDbContextFactory` | IDbContextFactory<JellyfinDbContext> | Real SQLite in-memory database backed by a held-open connection. Creates a fresh `JellyfinDbContext` per call. Schema created on construction. **Important:** instantiate one per test class; never share across parallel test classes. Dispose to close the connection and destroy the database. |
| `FakeTaskManager` | ITaskManager | Implements only `ScheduledTasks` property and `QueueIfNotRunning<T>()`. Exposes `Tasks` list and `QueuedTypes` list (in call order). Throws `NotImplementedException` for all other members. |
| `FakeScheduledTaskWorker` | IScheduledTaskWorker | Wraps an `IScheduledTask` constructor-supplied task. Allows setting `State`, `CurrentProgress`, and `LastExecutionResult`. |

**Test File Organization:**

Files are flat in `tests/Jellyfin.Plugin.EmbyAuth.Tests/`, one per tested type (e.g., `EmbyClientTests.cs`):

| Test file | Tested type | Tests |
|-----------|-------------|-------|
| `AccountAccessPolicyTests.cs` | `AccountAccessPolicy` | 14 test cases (3 `[Fact]`, 4 `[Theory]` with 11 data rows) |
| `EmbyAuthenticationProviderTests.cs` | `EmbyAuthenticationProvider` | 40+ test cases (mix of `[Fact]` and `[Theory]`) |
| `EmbyAuthControllerTests.cs` | `EmbyAuthController` | API endpoint authorization and migration status, migration run |
| `EmbyAuthPluginTests.cs` | `EmbyAuthPlugin` | Plugin metadata |
| `EmbyAuthSettingsTests.cs` | `EmbyAuthSettings`, `PluginConfiguration` | 17 test cases (6 `[Fact]`, 4 `[Theory]` with 11 data rows) |
| `EmbyClientTests.cs` | `EmbyClient` | 25 test cases (12 `[Fact]`, 5 `[Theory]` with 13 data rows) — logs, secrets, request format, redirects, sign-out |
| `EmbyLoginMethodUsersTests.cs` | `EmbyLoginMethodUsers` | User readiness conditions |
| `EmbyMigrationTaskTests.cs` | `EmbyMigrationTask` | Task name and description, runs the migration |
| `EmbyUserDirectoryTests.cs` | `EmbyUserDirectory` | 15 test cases (9 `[Fact]`, 2 `[Theory]` with 6 data rows) — caching, Emby list cache, unavailability |
| `EmbyVerifiedPasswordsTests.cs` | `EmbyVerifiedPasswords` | 11 test cases (8 `[Fact]`, 1 `[Theory]` with 3 data rows) — file I/O, fingerprints |
| `LoginDecisionTests.cs` | `LoginDecision` | 8 test cases (5 `[Fact]`, 1 `[Theory]` with 3 data rows) — account rules |
| `LoginMethodMoveTests.cs` | `LoginMethodMove` | Move validation, migration target resolution |
| `MigrationTargetValidationTests.cs` | `MigrationTargetValidation` | Configured target is a real login method |
| `MoveAfterLoginTests.cs` | `MoveAfterLogin` | Moves the user after login in the right mode |
| `TestDoublesTests.cs` | Test doubles themselves | Ensures the doubles work as expected |
| `TypeVisibilityTests.cs` | Type visibility | Public types discoverable by Jellyfin (`Assembly.GetExportedTypes()`) |

**Test Patterns:**

```csharp
// Arrange: build doubles and provider
var handler = new StubHttpMessageHandler()
    .Then(() => LoginAccepted(name: "Alice"))
    .Then(() => Status(HttpStatusCode.NoContent));
var provider = CreateProvider(handler, userManager, out var logger);

// Act
var result = await provider.Authenticate("alice", "password", null);

// Assert
Assert.Equal("alice", result.Name);
Assert.Empty(handler.Requests);  // Requests stub
Assert.Contains("emby", _logger.Entries);  // Logging stub
```

**Secrets checking:** `EmbyClientTests.cs:36-43` asserts that passwords and API keys never appear in log entries.

## Settings Page Tests (Node.js)

**Runner:** Node.js `node:test` (built-in) with jsdom
- **Framework:** `node:test` with `node:assert/strict`
- **Configuration:** CommonJS, no build step. Only dependency: `jsdom` `28.1.0` (dev).
- **Command:** `node --test` (discovers and runs `*.test.js` files; invoked by `.mise.toml:35`)
- **Test file:** `tests/js/configPage.test.js` (973 lines)

**Test helpers in `testHelpers.js`** (the counterpart to `TestDoubles.cs`):

| Helper | Exports |
|--------|---------|
| `buildDom(options)` | Constructs a jsdom window over `Configuration/configPage.html`, with `ApiClient` and `Dashboard` stubs injected before script parse. Returns object with `window`, `document`, `api` stub, `dashboard` stub, `interval` (fake timer controller), and `close()` function (dispatches `pagehide` for cleanup). |
| `stubApiClient(options)` | Builds a fake `ApiClient` matching the members the page calls. Configurable: initial config, users list, task state, failure flags (by call number), hanging status reads. Each flag can be toggled at runtime. Tracks all calls: `getConfigCalls`, `updateCalls`, `migrationStatusCalls`, `runMigrationCalls`. |
| `stubDashboard()` | Builds a fake `Dashboard`. Tracks `showLoadingMsg()` and `hideLoadingMsg()` calls, and `processPluginConfigurationUpdateResult()` results. |
| `installFakeInterval(window)` | Overrides `window.setInterval` and `clearInterval` with a controllable implementation. Returns `{tick(ms)}` to synchronously run registered intervals. jsdom's default timer chains to Node's `setTimeout`, so this override intercepts the page's polling loop. |
| `listItemTexts(document)` | Reads migration list items as text. Returns array of text content from `<li>` elements under `#EmbyAuthMigrationUsers`. |
| `settingsStatus(document)` | Returns the `#EmbyAuthSettingsStatus` element (displays error/success messages). |
| `messageChildren(element)` | Describes an element's child structure: tag name, sorted class list, `aria-hidden`, text content. Useful for asserting on composed message structure. |
| `pageStyleRules(document)` | Reads CSS rules from the page's inline `<style>` elements. Returns array of `CSSRule` objects. |
| `selectOptions(select)` | Reads a `<select>`'s options as array of `{value, text, disabled, selected}`. |
| `firePageshow(document, window)` | Fires `pageshow` event on the page element (starts loading the settings). |
| `fireSubmit(document, window)` | Fires `submit` event on the settings form (tests form validation). |
| `clickSave(document)` | Clicks the Save button through its own DOM `click()` method (respects disabled state). |
| `clickRunMigration(document, window)` | Clicks the "Run migration now" button. |
| `tickPoll(interval, times)` | Advances the fake poll interval by `times` × 2-second ticks and awaits macrotask boundaries for promise settlement. Default: 1 tick. |
| `flush()` | Awaits three macrotask boundaries so async operations settle before assertions. |

**Test structure:**
```javascript
test('name', async () => {
  const { document, window, api, interval } = buildDom({
    config: { EmbyServerUrl: 'http://emby', EmbyApiKey: 'key' },
    users: [{ Name: 'alice', ... }],
  });
  
  firePageshow(document, window);
  await flush();
  
  assert.equal(settingsStatus(document).textContent, 'expected message');
  assert.deepEqual(api.updateCalls, []);
  
  clickRunMigration(document, window);
  await tickPoll(interval, 3);  // 3 poll cycles
  
  dom.close();
});
```

**Coverage:** 973 lines testing the settings page behavior: load/save settings, show/hide migration UI, poll for migration status, enable/disable buttons, error messages, form validation.

## End-to-End Tests (bats)

**Framework:** bats 1.14.0 — shell script testing
- **Command:** `bats e2e` (all files) or `bats e2e/NN-topic.bats` (one file)
- **Infrastructure:** Docker Compose with Emby, Jellyfin, and an Emby logging proxy (nginx)
- **Lifecycle:** 
  - `setup_suite.bash`: Runs once per test run. Starts containers, completes setup wizards, creates Emby users, configures plugin, exports `$EMBY_TOKEN`, `$JF_TOKEN`, `$EMBY_API_KEY`.
  - `setup_file`: Runs once per `.bats` file. File-specific setup (create test accounts, reset plugin config).
  - `setup`: Runs before each test.
  - `teardown_suite.bash`: Runs once at the end. Stops containers (unless `KEEP_E2E=1`).

**Test files and coverage:**

| File | Tests | Coverage |
|------|-------|----------|
| `10-login-checks.bats` | 12 @ tests | First login creates Default-method account; cached password accepted; blank password rejected; disabled Emby users; typos; extra spaces; wrong password |
| `20-accounts.bats` | 7 @ tests | Admin rejection; existing accounts on Default stay Default; password conflicts between methods; pre-created on Emby method |
| `30-migration-modes.bats` | 10 @ tests | Migration mode behavior: `KeepEmbyInCharge`, `MoveAfterFirstLogin`, `TrustedEmbyServer`; password move; new password moves |
| `40-emby-outage.bats` | 4 @ tests | Login when Emby is unreachable; login when Emby comes back; user list unavailable |
| `50-migration-target.bats` | 5 @ tests | Migration target validation; move to a real login method; target changes; task picks the right target |
| `90-jellyfin-log.bats` | 1 @ test | Scans the whole Jellyfin Debug log; asserts no passwords or API keys logged |

**Test data:** `setup_suite.bash` creates Emby users: `alice`, `bob`, `carol`, `dave`, `erin`, `frank` (no password), `gina` (remote access disabled), `henry`, `ivy` (disabled), and more (26 users total). Each user's password follows the pattern `NAME-emby-pass` to make log scanning safe.

**Server facts tested:**
- Jellyfin 12.1 answers `/health` with `"Healthy"` at ready; Emby has no `/health`.
- `POST /Users/AuthenticateByName` with MediaBrowser auth header (`Authorization: MediaBrowser Client=..., Token=...`).
- Quick Connect: `POST /QuickConnect/Initiate` (anon), `POST /QuickConnect/Authorize` (admin), then `POST /Users/AuthenticateWithQuickConnect` (secret).
- Migration task key: `EmbyAuthMigration`; run with `POST /ScheduledTasks/Running/{id}`.

**Test assertions:**
- Login status with `status POST "$JELLYFIN/Users/AuthenticateByName"` (returns HTTP status code).
- User policy field with `policy_field NAME FIELD` (reads `User.Policy.FIELD` from Jellyfin).
- Emby proxy request count with `emby_login_requests NAME` (reads nginx log; includes a marker request to sync).
- Jellyfin log assertions in `90-jellyfin-log.bats`: no passwords/API keys, no exception traces for expected failures.

**Helper functions in `helpers.bash`:**
- `api METHOD URL TOKEN [JSON]`: HTTP call with MediaBrowser auth.
- `status METHOD URL TOKEN [JSON]`: HTTP status code only.
- `login_token BASE USER PASS`: Get admin/user token.
- `create_user BASE TOKEN NAME`: Create user, return ID.
- `set_password BASE TOKEN USER_ID PASSWORD`: Set password.
- `policy_field NAME FIELD`: Read Jellyfin user policy field.
- `precreate_on_emby_method NAME`: Create Jellyfin account on Emby login method.
- `emby_login_requests NAME`: Count successful logins from Emby proxy log.
- `reset_plugin_config`: Reset plugin settings to defaults (in test file's `setup_file`).

**Environment variables:**
```bash
EMBY_PORT=18096           # Host port for Emby
JELLYFIN_PORT=28096       # Host port for Jellyfin
EMBY="http://127.0.0.1:$EMBY_PORT"      # Emby base URL
JELLYFIN="http://127.0.0.1:$JELLYFIN_PORT"  # Jellyfin base URL
EMBY_PROVIDER=Jellyfin.Plugin.EmbyAuth.EmbyAuthenticationProvider
DEFAULT_PROVIDER=Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider
EMBY_TOKEN, JF_TOKEN, EMBY_API_KEY    # Set in setup_suite
KEEP_E2E=1                # Keep containers running after tests
```

**Logging proxy (`e2e/emby-proxy.conf`):** nginx logs request body to access log so tests can assert on login attempts and Emby API key usage patterns.

---

*Testing analysis: 2026-09-20*
