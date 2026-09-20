---
last_mapped_commit: fb0fd999638d88fabb37bd9d449c23226a5b2f8b
---

# External Integrations

**Analysis Date:** 2026-09-20

**Scope:** commit `fb0fd99` on `gsd/phase-04-emby-traffic-under-load-and-failure`.

## APIs & External Services

**Emby Server:**
- `EmbyClient` is the only code that sends requests to Emby (`src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs:30-40`).
- HTTP client: `IHttpClientFactory.CreateClient(NamedClient.Default)` from Jellyfin, with a 5-second timeout on every request (`src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs:40`, `EmbyClient.cs:158-163`). A new client is created for each method call (`EmbyClient.cs:54`, `EmbyClient.cs:109`).
- Base URL: the `EmbyServerUrl` setting, with a trailing slash added if missing (`src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs:155-156`).

**Emby endpoints the plugin calls:**

1. **`GET /Users`** — reads the user list. Header `X-Emby-Token` carries the API key (`src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs:114-115`). The plugin reads only `Name` and `Policy.IsDisabled`, and drops entries without a name (`EmbyClient.cs:141-145`, `EmbyClient.cs:217-223`). Called by `EmbyUserDirectory` to cache the list for 60 seconds (`src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs:62`).

2. **`POST /Users/AuthenticateByName`** — checks the typed name and password. Header `Authorization: MediaBrowser Client="Jellyfin Emby Auth", Device="Jellyfin", DeviceId="jellyfin-plugin-emby-auth", Version="1.0.0"` without a token (`EmbyClient.cs:37-38`, `EmbyClient.cs:59-60`). JSON body fields `Username` and `Pw` (`EmbyClient.cs:209-211`), sent as buffered `StringContent` with a `Content-Length`, because Emby answers HTTP 400 to a chunked body (`EmbyClient.cs:62-66`, `.claude/rules/plugin.md:39`). The plugin reads `User.Name`, `User.Policy.EnableRemoteAccess`, and `AccessToken` (`EmbyClient.cs:213-223`); a missing policy means no remote access (`EmbyClient.cs:95`).

3. **`POST /Sessions/Logout`** — ends the Emby session that the login opened. The `Authorization` header adds `Token="<AccessToken>"` (`EmbyClient.cs:174-176`). A failed sign-out logs a warning and the login still succeeds (`EmbyClient.cs:177-185`, `EmbyClient.cs:203-207`).

**Emby failure handling:**
- A non-success status, `HttpRequestException`, or a timeout (`TaskCanceledException` without caller cancellation) returns `null`, and the login is refused (`src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs:68-72`, `EmbyClient.cs:76-80`, `EmbyClient.cs:148-149`).
- `JsonException` or `InvalidOperationException` (for example an unsupported charset) returns `null` and logs the exception (`EmbyClient.cs:81-85`, `EmbyClient.cs:151-153`).
- A login response without a user name returns `null` (`EmbyClient.cs:87-92`).

**Emby endpoints used only by test and demo scripts:**
- `e2e/helpers.bash` calls `/System/Info/Public`, `/Startup/*`, `/Users/New`, `/Users/{id}/Password`, `/Users/{id}/Policy`, and `/Auth/Keys` on Emby to set up users and create the API key (`e2e/helpers.bash:63-66`, `e2e/helpers.bash:72-113`, `e2e/helpers.bash:187-191`). The plugin does not call these.

**GitHub:**
- `.github/workflows/release.yml:43-48` creates a GitHub release with `gh release create "$TAG" artifacts/release/*.zip artifacts/release/manifest.json --verify-tag --title "$TAG" --generate-notes`, authenticated with the workflow token (`GH_TOKEN: ${{ github.token }}`).
- The release manifest points each version at `https://github.com/tkensiski/jellyfin-plugin-emby-auth/releases/download/v<version>/jellyfin-plugin-emby-auth_<version>.zip` by default; `RELEASE_URL_BASE` overrides the base (`scripts/package.sh:12`, `scripts/package.sh:23`, `scripts/package.sh:86`).
- The repository is private, so a release download needs access. Jellyfin fetches a repository manifest and the zip without GitHub credentials, so `manifest.json` works as a repository URL only when the release files are public (`README.md:23`, `README.md:35`).

## Data Storage

**Databases:**
- The plugin owns no database. It reads and updates Jellyfin's user table through `IDbContextFactory<JellyfinDbContext>` (`src/Jellyfin.Plugin.EmbyAuth/LoginMethodMove.cs:33-43`, `src/Jellyfin.Plugin.EmbyAuth/EmbyMigrationTask.cs:1-60`, `src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs:30-60`) and through `IUserManager` (`src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs:69-113`).
- `EmbyLoginMethodUsers.ListAsync` reads the ID, name, and saved hash of every user on the Emby login method, sorted by name, and marks each user `ReadyToMove` when the hash matches a fingerprint. The migration task and the migration API share it (`src/Jellyfin.Plugin.EmbyAuth/EmbyLoginMethodUsers.cs:20-53`).
- `LoginMethodMove.MoveAsync` changes only `AuthenticationProviderId` with `ExecuteUpdateAsync`, and only while the user is on the Emby login method and still has the verified hash (`src/Jellyfin.Plugin.EmbyAuth/LoginMethodMove.cs:33-43`, `.claude/rules/plugin.md:21`).

**File Storage:**
- Verified password fingerprints: `Jellyfin.Plugin.EmbyAuth.VerifiedPasswords.json` in Jellyfin's `PluginConfigurationsPath` (`src/Jellyfin.Plugin.EmbyAuth/PluginServiceRegistrator.cs:25`, `PluginServiceRegistrator.cs:33-35`).
- Contents: a JSON map of Jellyfin user ID (GUID) to the hex SHA-256 fingerprint of the saved password hash, never the hash (`src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs:23`, `EmbyVerifiedPasswords.cs:87-88`, `.claude/rules/plugin.md:27`).
- Writes go to `<file>.tmp`, then `File.Move` with overwrite, inside a lock (`EmbyVerifiedPasswords.cs:45-64`). The file is read once and kept in memory (`EmbyVerifiedPasswords.cs:90-113`).
- Plugin settings: Jellyfin saves them with `XmlSerializer`. `EmbyServerUrl` is a `string` because `XmlSerializer` cannot serialize `System.Uri` (`src/Jellyfin.Plugin.EmbyAuth/Configuration/PluginConfiguration.cs:50-53`, `.claude/rules/plugin.md:55`).

**Caching:**
- Emby user list: held in memory by `EmbyUserDirectory` for 60 seconds (`src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs:39`).
- After a failed read, the plugin stores no list and refuses Emby logins for 30 seconds, then reads again (`EmbyUserDirectory.cs:44`, `EmbyUserDirectory.cs:63-73`).
- A change to the server URL or API key reads the list again at once (`EmbyUserDirectory.cs:60`).

## Authentication & Identity

**Auth Provider:**
- `EmbyAuthenticationProvider` implements `IAuthenticationProvider` and `IRequiresResolvedUser`, with the login method name `Emby` and the ID `Jellyfin.Plugin.EmbyAuth.EmbyAuthenticationProvider` (`src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs:35-50`). Jellyfin calls the overload with the resolved user; the other overload throws `NotSupportedException` (`EmbyAuthenticationProvider.cs:65-66`).
- Flow (`EmbyAuthenticationProvider.cs:69-113`):
  1. Refuse a blank password, a disabled account, or an administrator, without contacting Emby (`EmbyAuthenticationProvider.cs:71-79`).
  2. In `JellyfinPasswordFirst` mode, accept the saved password if Emby verified that hash earlier (`EmbyAuthenticationProvider.cs:81-86`).
  3. Refuse unless the Emby user list has an enabled user with the exact name, ignoring case (`EmbyAuthenticationProvider.cs:88-93`, `src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs:76-82`).
  4. Send the credentials to Emby (`EmbyAuthenticationProvider.cs:95-96`).
  5. Apply `LoginDecision.Decide`: deny, use the account, or create an account (`EmbyAuthenticationProvider.cs:98-103`, `src/Jellyfin.Plugin.EmbyAuth/LoginDecision.cs:40-55`).
  6. Hash the password with Jellyfin's `ICryptoProvider`, save it through `IUserManager`, and record the fingerprint (`EmbyAuthenticationProvider.cs:105-111`).
- Every refusal throws `AuthenticationException`, because Jellyfin catches only that type from a login method (`.claude/rules/plugin.md:15`).
- The move to the configured migration target happens later, in `MoveAfterLogin` or the migration task. See Jellyfin Integration Details.

**Migration modes** (`src/Jellyfin.Plugin.EmbyAuth/Configuration/PluginConfiguration.cs:9-21`, `docs/settings.md:27-33`):
- `MoveAfterFirstLogin` — Emby checks the first login, then the user moves to the configured migration target.
- `KeepEmbyInCharge` — Emby checks every login. Users stay on the Emby login method until an administrator runs the migration.
- `JellyfinPasswordFirst` — Jellyfin checks the saved password first, and Emby checks only if no verified saved password matches. Users stay until the migration runs.

**Account access** (`src/Jellyfin.Plugin.EmbyAuth/Configuration/PluginConfiguration.cs:25-42`, `src/Jellyfin.Plugin.EmbyAuth/AccountAccessPolicy.cs:20-49`, `docs/settings.md:35-45`):
- `CopyEmbyRemoteAccess` — new accounts get Jellyfin defaults and copy Emby's remote access setting. Existing accounts lose remote access when Emby does not allow it.
- `NoLibraries` — like `CopyEmbyRemoteAccess`, and new accounts get no library access.
- `JellyfinDefaults` — the plugin ignores Emby's remote access setting.

**Password changes in Jellyfin** (`src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs:115-130`):
- A new password is hashed and the user moves to the configured password-set target (or falls back to migration target) at once.
- A reset (empty password) removes the saved password, and the user stays on the Emby login method.

**Migration targets** (`src/Jellyfin.Plugin.EmbyAuth/LoginMethodMove.cs:45-81`, `src/Jellyfin.Plugin.EmbyAuth/Configuration/PluginConfiguration.cs:70-82`):
- `MigrationTarget` — where the move after a login and the migration task move a user. Defaults to `DefaultProviderId` for backward compatibility.
- `PasswordSetTarget` — where a move happens when an administrator sets a password in Jellyfin. Defaults to empty, which means `MigrationTarget` governs that move too.
- Both can be `RemainOnEmbyLoginMethod` to skip the move.
- `LoginMethodMove.ResolveMigrationTarget` and `LoginMethodMove.ResolvePasswordSetTarget` resolve these values to a `MoveTarget` (`src/Jellyfin.Plugin.EmbyAuth/LoginMethodMove.cs:51-67`).
- `MigrationTargetValidation.FindProblem` checks that the targets are valid Jellyfin login methods before settings save (`src/Jellyfin.Plugin.EmbyAuth/MigrationTargetValidation.cs:25-35`).

**Migration API authorization:**
- `EmbyAuthController` has `[Authorize(Policy = Policies.RequiresElevation)]`, so only administrators can call it (`src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs:27`, `.claude/rules/plugin.md:48`). `e2e/30-migration-modes.bats:104-108` checks that a non-administrator gets 403 on both endpoints.

## Monitoring & Observability

**Error Tracking:**
- None. Errors go to the Jellyfin log.

**Logs:**
- `Microsoft.Extensions.Logging` with source-generated `[LoggerMessage]` methods in each class, for example `src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs:188-207` and `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs:227-258`.
- The e2e Jellyfin container logs at Debug level (`e2e/compose.yaml:19-21`).
- `e2e/90-jellyfin-log.bats:8-25` checks the whole Jellyfin log for the test password patterns and the Emby API key (`.claude/rules/e2e.md:20`, `.claude/rules/e2e.md:23`).

**Emby request logging (e2e and demo only):**
- The `emby-proxy` container (`nginx:1.30.5-alpine`) sits between Jellyfin and Emby and logs each request as `$request_method $uri $request_body`, JSON-escaped, to stdout (`e2e/compose.yaml:9-15`, `e2e/emby-proxy.conf:4-14`). Request bodies, including passwords, appear in this log unmasked.
- The plugin in the e2e and demo environments points at `http://emby-proxy:8096` (`e2e/setup_suite.bash:25-26`, `scripts/dev-env.sh:43-44`).
- `emby_login_requests NAME` counts `POST /Users/AuthenticateByName` entries for a name, after it sends a marker request and waits for it in the log (`e2e/helpers.bash:193-214`).

## CI/CD & Deployment

**Hosting:**
- Install: unzip `jellyfin-plugin-emby-auth_<version>.zip` into `<jellyfin config>/plugins/EmbyAuth_<version>/`, then restart Jellyfin. The zip holds the plugin DLL and `meta.json` (`README.md:20-33`).
- Not published to a plugin repository (`README.md:12`).

**Release packaging (`scripts/package.sh`):**
- Actions: `build` and `check-tag TAG`; any other argument prints usage and exits 2 (`scripts/package.sh:6-8`, `scripts/package.sh:107-124`).
- `check-tag` fails unless the tag is `v<Version>` from `Directory.Build.props` (`scripts/package.sh:40-47`).
- `build` publishes the plugin into `artifacts/package-stage`, writes `meta.json` (guid, name, version, `targetAbi`, timestamp, `status: Active`, `autoUpdate: false`, the assembly), zips the DLL and `meta.json` into `artifacts/release/jellyfin-plugin-emby-auth_<version>.zip`, and writes a one-version repository `manifest.json` with the MD5 checksum and download URL (`scripts/package.sh:49-105`).
- Environment: `PACKAGE_OUTPUT_DIR`, `RELEASE_URL_BASE`, `RELEASE_TIMESTAMP` (`scripts/package.sh:10-13`).
- `tests/scripts/package.bats` checks usage, the zip entries, `meta.json`, the manifest checksum and URL, and both `check-tag` outcomes (`tests/scripts/package.bats:20-76`).

**CI Pipeline (`.github/workflows/ci.yml`):**
- Triggers: every pull request and every push to `main` (lines 5-9).
- Jobs `lint`, `test`, `e2e`, each `mise run <task>` after `actions/checkout` (SHA-pinned v7.0.1, `persist-credentials: false`) and `jdx/mise-action` (SHA-pinned v4.3.0) (lines 19-62). Timeouts 15, 15, and 30 minutes.
- `ci-success` runs `if: always()`, needs all three jobs, and fails unless each result is `success` (lines 64-81).
- Local reproduction: `act pull_request -j <job>`; the `e2e` job needs `--bind --container-options "--network host"` (`docs/development.md:12-13`, `.actrc:1`).
- Local pre-commit hooks run `mise run lint` and `mise run test` (`.pre-commit-config.yaml:4-15`).

**Release Pipeline (`.github/workflows/release.yml`):**
- Trigger: a pushed `v*` tag (lines 6-9).
- Steps: checkout, `mise-action` with `cache: false`, `scripts/package.sh check-tag "$TAG"`, `mise run test`, `mise run package`, `gh release create` (lines 21-48). Only this job has `contents: write` (lines 18-19).
- Procedure: set the three version properties in `Directory.Build.props`, merge, tag the merge commit `v<version>`, push the tag (`docs/development.md:27-31`).

**E2E containers** (`e2e/compose.yaml`):
- `emby`: `emby/embyserver:4.10.0.40`, published on `127.0.0.1:${EMBY_PORT:-18096}` (`e2e/compose.yaml:4-7`).
- `emby-proxy`: `nginx:1.30.5-alpine`, no published port (`e2e/compose.yaml:10-15`).
- `jellyfin`: `jellyfin/jellyfin:12.1.20260915-010956`, published on `127.0.0.1:${JELLYFIN_PORT:-28096}`, with `../artifacts/plugin` mounted as `/config/plugins/EmbyAuth_1.0.0.0` (`e2e/compose.yaml:17-25`).
- `e2e/setup_suite.bash` runs `dotnet publish`, then `docker compose down --volumes` and `up -d`, waits for Emby `/System/Info/Public` and Jellyfin `/health`, completes both startup wizards, creates the Emby API key, configures the plugin, and creates every Emby user (`e2e/setup_suite.bash:8-36`).

**Local demo environment (`scripts/dev-env.sh`):**
- Uses `e2e/compose.yaml` and `e2e/helpers.bash` with the project name `emby-auth-dev` and default host ports 18196 (Emby) and 28196 (Jellyfin), so it can run next to the e2e containers (`scripts/dev-env.sh:4-5`, `scripts/dev-env.sh:12`, `scripts/dev-env.sh:17-22`).
- Actions: `up` builds the plugin, starts fresh containers, configures both servers, and creates demo users; `status` shows the containers; `down` removes the containers and their data (`scripts/dev-env.sh:7-10`, `scripts/dev-env.sh:75-91`). Any other argument prints usage and exits 2.

## Environment Configuration

**Plugin settings** (Dashboard > Plugins > Emby Auth, or `POST /Plugins/e973e09a-e8b4-40c1-9be2-8e51342de1f9/Configuration` with an administrator token; `docs/settings.md:5-25`):
- `EmbyServerUrl` — required. Must be an absolute `http` or `https` URL without a user name or password (`src/Jellyfin.Plugin.EmbyAuth/EmbyAuthSettings.cs:44-58`).
- `EmbyApiKey` — required, not blank (`EmbyAuthSettings.cs:60-63`). Used only for `GET /Users`.
- `MigrationMode` — default `MoveAfterFirstLogin` (`src/Jellyfin.Plugin.EmbyAuth/Configuration/PluginConfiguration.cs:63`).
- `AccountAccess` — default `CopyEmbyRemoteAccess` (`PluginConfiguration.cs:68`).
- `MigrationTarget` — default `DefaultProviderId` (Jellyfin's Default login method) (`PluginConfiguration.cs:75`).
- `PasswordSetTarget` — default empty string, which means falls back to `MigrationTarget` (`PluginConfiguration.cs:82`).
- Invalid settings make the plugin refuse every login it handles and log the problem at Error level, without repeating the configured values (`src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs:144-153`, `EmbyAuthenticationProvider.cs:251-252`, `EmbyAuthSettings.cs:21`, `docs/settings.md:12`).

**Settings page** (`src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html`, embedded resource served through `IHasWebPages`: `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthPlugin.cs:43-53`, `src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj:22-25`):
- Loads and saves the settings with `ApiClient.getPluginConfiguration` and `ApiClient.updatePluginConfiguration` (`configPage.html:82-94`, `configPage.html:107-122`).
- The Migration section (`configPage.html:47-55`) loads `GET EmbyAuth/Migration` with `ApiClient.getJSON(ApiClient.getUrl(...))` and lists each user with `textContent` (`configPage.html:63-80`). **Run migration now** sends `POST EmbyAuth/Migration/Run` with `ApiClient.ajax`, then reloads the list after 3 seconds (`configPage.html:96-105`). See `.claude/rules/plugin.md:49-50`.
- Tested with `tests/js/configPage.test.js`, which uses `node:test` and jsdom to simulate loading, saving, and running migrations in the browser environment.

**Environment variables:**
- The plugin reads none.
- E2E: `EMBY_PORT` and `JELLYFIN_PORT` (default 18096 and 28096) set the host ports that the helpers use and that `e2e/compose.yaml` publishes (`e2e/helpers.bash:6-10`, `e2e/compose.yaml:7`, `e2e/compose.yaml:23`, `.claude/rules/e2e.md:24`); `KEEP_E2E=1` keeps the containers after the run (`e2e/setup_suite.bash:41`). The `jellyfin` container sets `JELLYFIN_Serilog__MinimumLevel__Default: Debug` (`e2e/compose.yaml:21`).
- Demo: `EMBY_PORT` and `JELLYFIN_PORT` default to 18196 and 28196 (`scripts/dev-env.sh:18-19`).
- Packaging: `PACKAGE_OUTPUT_DIR`, `RELEASE_URL_BASE`, `RELEASE_TIMESTAMP` (`scripts/package.sh:10-13`).
- Tooling: `DOTNET_CLI_TELEMETRY_OPTOUT=1`, `DOTNET_NOLOGO=1` (`.mise.toml:13-14`).

**Secrets location:**
- The Emby API key is a plugin setting that Jellyfin stores. Jellyfin administrators can read it through the plugin settings API (`docs/how-it-works.md:29`). The settings page loads it into a `type="password"` input (`src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html:19`, `configPage.html:87`).
- The e2e suite and the demo script create their API key at run time on the Emby container (`e2e/setup_suite.bash:22`, `scripts/dev-env.sh:42`).
- The release job uses only the workflow token `github.token` (`.github/workflows/release.yml:45`). No repository secret is referenced in either workflow.

## Jellyfin Integration Details

**Service registration** (`src/Jellyfin.Plugin.EmbyAuth/PluginServiceRegistrator.cs:26-46`, `IPluginServiceRegistrator`):
- Singletons: `TimeProvider.System` (only if none is registered), `EmbyClient`, `EmbyUserDirectory`, `EmbyVerifiedPasswords`, and `EmbyAuthenticationProvider` as `IAuthenticationProvider`.
- Scoped: `MoveAfterLogin` as `IEventConsumer<AuthenticationResultEventArgs>`.
- `IUserManager` depends on all login methods, so the provider resolves it from `IServiceProvider` at login time (`src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs:21`, `EmbyAuthenticationProvider.cs:106`, `.claude/rules/plugin.md:16`).
- `PluginServiceRegistrator` does not register `EmbyAuthController`. The repository does not document how Jellyfin discovers the controller; the e2e tests call its routes (`e2e/30-migration-modes.bats:95-120`).

**Plugin type and visibility:**
- `EmbyAuthPlugin` extends `BasePlugin<PluginConfiguration>` and implements `IHasWebPages`, with ID `e973e09a-e8b4-40c1-9be2-8e51342de1f9` (`src/Jellyfin.Plugin.EmbyAuth/EmbyAuthPlugin.cs:15`, `EmbyAuthPlugin.cs:37`). The static `Instance` gives other classes the configuration (`EmbyAuthPlugin.cs:25`, `EmbyAuthPlugin.cs:31`).
- Jellyfin discovers scheduled tasks with `Assembly.GetExportedTypes()`, so `EmbyMigrationTask` and every type in its constructor must be public; other plugin types stay internal (`.claude/rules/plugin.md:22`).
- Public types: `EmbyAuthPlugin`, `PluginServiceRegistrator`, `EmbyMigrationTask`, `EmbyVerifiedPasswords`, `PluginConfiguration`, `MigrationMode`, `AccountAccess`, `EmbyAuthController`, `MigrationStatus`, and `MigrationUser`. `EmbyAuthenticationProvider`, `EmbyClient`, `EmbyUserDirectory`, `MoveAfterLogin`, `LoginMethodMove`, `MigrationTargetValidation`, `LoginDecision`, `AccountAccessPolicy`, `EmbyLoginMethodUsers`, and `EmbyLoginMethodUser` are internal.

**Login event:**
- After a successful login, Jellyfin sets the user's login method to the method that accepted the login, so the provider cannot move the user itself. `SessionManager` then publishes `AuthenticationResultEventArgs` (`.claude/rules/plugin.md:18`).
- `MoveAfterLogin.OnEvent` acts only in `MoveAfterFirstLogin` mode, and only if the user is on the Emby login method and `EmbyVerifiedPasswords` matches the saved hash (`src/Jellyfin.Plugin.EmbyAuth/MoveAfterLogin.cs:33-73`).
- Quick Connect publishes the same event without a password check; the fingerprint check stops it from moving a user (`.claude/rules/plugin.md:19`).
- `EventManager` logs and ignores an exception from a consumer, so a failed move does not fail the login (`.claude/rules/plugin.md:20`).

**Migration task:**
- `EmbyMigrationTask` implements `IScheduledTask` with key `EmbyAuthMigration`, category `Emby Auth`, and no default triggers (`src/Jellyfin.Plugin.EmbyAuth/EmbyMigrationTask.cs:20-59`).
- It gets the candidates from `EmbyLoginMethodUsers.ListAsync`, moves each `ReadyToMove` user with `LoginMethodMove.MoveAsync` to the configured migration target, and logs each user that stays (`EmbyMigrationTask.cs:62-90`).
- Admins start it with **Run migration now** on the settings page, from Dashboard > Advanced > Scheduled Tasks, or through the API (`EmbyMigrationTask.cs:17`, `docs/migration.md:25`, `.claude/rules/plugin.md:51`). The e2e helper `run_migration_task` starts it with `POST /ScheduledTasks/Running/{id}` (`e2e/helpers.bash:161-185`).

**Migration API (`src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs`):**
- ASP.NET Core `ControllerBase` with `[ApiController]`, route prefix `EmbyAuth`, JSON output, and `[Authorize(Policy = Policies.RequiresElevation)]` (`EmbyAuthController.cs:26-35`).
- `GET /EmbyAuth/Migration` returns `MigrationStatus { Users: [{ Name, ReadyToMove }] }` for the users on the Emby login method, sorted by name (`EmbyAuthController.cs:43-62`). Jellyfin serializes the properties in PascalCase (`.claude/rules/plugin.md:49`, `docs/migration.md:33`).
- `POST /EmbyAuth/Migration/Run` calls `ITaskManager.QueueIfNotRunning<EmbyMigrationTask>()` and returns 204 (`EmbyAuthController.cs:64-72`, `docs/migration.md:34`).
- E2E coverage: the ready state for a verified, a pre-created, and an administrator account; 403 for a non-administrator on both endpoints; and a 204 from `Migration/Run` that moves only ready users (`e2e/30-migration-modes.bats:95-120`).

**Concurrent access:**
- Jellyfin calls login methods inside a lock; all logins for unknown names share the key `Guid.Empty`, so Emby calls must stay short (`.claude/rules/plugin.md:14`).

---

*Integration audit: 2026-09-20*
