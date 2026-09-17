# Codebase Structure

**Analysis Date:** 2026-09-16

**Scope:** This document describes commit `ecee1ed` on `main`.

## Directory Layout

```
jellyfin-plugin-emby-auth/
├── src/Jellyfin.Plugin.EmbyAuth/               # Plugin project
│   ├── Api/
│   │   └── EmbyAuthController.cs               # Admin-only migration API; MigrationStatus and MigrationUser records
│   ├── Configuration/
│   │   ├── PluginConfiguration.cs              # Settings model, MigrationMode and AccountAccess enums
│   │   └── configPage.html                     # Settings page with the Migration section (embedded resource)
│   ├── AccountAccessPolicy.cs                  # Applies the AccountAccess setting to accounts
│   ├── DefaultLoginMethod.cs                   # Single-column move to the Default login method
│   ├── EmbyAuthenticationProvider.cs           # The Emby login method
│   ├── EmbyAuthPlugin.cs                       # Plugin ID, name, Instance, settings page
│   ├── EmbyAuthSettings.cs                     # Settings validation
│   ├── EmbyClient.cs                           # All requests to Emby; EmbyLogin and EmbyUser records
│   ├── EmbyLoginMethodUsers.cs                 # Users on the Emby login method with readiness; EmbyLoginMethodUser record
│   ├── EmbyUserDirectory.cs                    # Cached Emby user list; EmbyUserStatus enum
│   ├── EmbyVerifiedPasswords.cs                # Fingerprint file of hashes that Emby verified
│   ├── Jellyfin.Plugin.EmbyAuth.csproj         # Package pins, AnalysisMode, InternalsVisibleTo, embedded page
│   ├── LoginDecision.cs                        # Pure account decision; JellyfinAccount and LoginAction
│   ├── MoveEmbyUsersToDefaultTask.cs           # Migration scheduled task
│   ├── MoveToDefaultLoginMethod.cs             # Event consumer: move after login
│   └── PluginServiceRegistrator.cs             # DI registration
│
├── tests/
│   ├── Jellyfin.Plugin.EmbyAuth.Tests/         # Unit tests (xUnit v3)
│   │   ├── AccountAccessPolicyTests.cs         # AccountAccess on new and existing accounts
│   │   ├── EmbyAuthSettingsTests.cs            # Settings validation and defaults
│   │   ├── EmbyClientTests.cs                  # Login, sign-out, user list, timeouts, charset, no secrets in logs
│   │   ├── EmbyUserDirectoryTests.cs           # Cache duration, settings change, retry delay
│   │   ├── EmbyVerifiedPasswordsTests.cs       # Records, restart, missing/unreadable file, concurrency
│   │   ├── Jellyfin.Plugin.EmbyAuth.Tests.csproj # OutputType Exe, xunit.v3 and Jellyfin.Controller pins
│   │   ├── LoginDecisionTests.cs               # LoginDecision.Decide
│   │   └── TestDoubles.cs                      # StubHttpMessageHandler, StubHttpClientFactory, ManualTimeProvider, CapturingLogger<T>
│   └── scripts/
│       └── package.bats                        # bats tests for scripts/package.sh
│
├── e2e/                                        # End-to-end tests (bats, Docker)
│   ├── setup_suite.bash                        # Publishes the plugin, starts containers, configures plugin, creates Emby users
│   ├── helpers.bash                            # Ports, API, readiness, user, plugin-config, and proxy-log helpers
│   ├── 10-login-checks.bats                    # Settings page, first login, password checks, user list checks, sign-out
│   ├── 20-accounts.bats                        # Pre-created accounts, remote access, administrators, Quick Connect, password set/reset
│   ├── 30-migration-modes.bats                 # KeepEmbyInCharge, JellyfinPasswordFirst, migration task, NoLibraries, JellyfinDefaults, migration API
│   ├── 40-emby-outage.bats                     # Logins while Emby is unreachable
│   ├── 90-jellyfin-log.bats                    # Runs last: no test password or API key in the Jellyfin log
│   ├── compose.yaml                            # Emby, emby-proxy (nginx), Jellyfin; host ports from EMBY_PORT and JELLYFIN_PORT
│   └── emby-proxy.conf                         # nginx config that logs each request body sent to Emby
│
├── scripts/
│   ├── dev-env.sh                              # Local demo environment: up | status | down
│   └── package.sh                              # Release zip and manifest: build | check-tag TAG
│
├── docs/                                       # User and developer documentation
│   ├── how-it-works.md                         # Login steps, password changes, security notes, limits
│   ├── settings.md                             # Settings, settings API, migration behavior, account access
│   ├── migration.md                            # Migration procedure, migration API, Emby shutdown
│   ├── development.md                          # Commands, e2e tests, demo, releases
│   └── images/
│       └── settings-page.png                   # README screenshot of the settings page
│
├── .github/workflows/
│   ├── ci.yml                                  # Jobs lint, test, e2e (one mise task each) and ci-success
│   └── release.yml                             # On a v* tag: check tag, test, package, create GitHub release
│
├── .claude/rules/
│   ├── plugin.md                               # Jellyfin and Emby behavior the plugin depends on (src/, tests/)
│   └── e2e.md                                  # e2e layout, conventions, server facts (e2e/)
│
├── .planning/codebase/                         # These codebase map documents
├── artifacts/                                  # Build output (gitignored)
├── .actrc                                      # act runner image for local CI runs
├── .gitignore                                  # bin/, obj/, artifacts/, TestResults/
├── .mise.toml                                  # Pinned tools, env, and the lint, test, package, e2e tasks
├── .pre-commit-config.yaml                     # Hooks that run mise run lint and mise run test
├── CLAUDE.md                                   # Commands, CI and release, layout, rules
├── Directory.Build.props                       # Version, net10.0, Nullable, TreatWarningsAsErrors
├── global.json                                 # Test runner: Microsoft.Testing.Platform
├── Jellyfin.Plugin.EmbyAuth.slnx               # Solution (XML): plugin and test projects
├── LICENSE                                     # GPL-3.0
└── README.md                                   # Short introduction: install, configure, migrate, links to docs/
```

## Directory Purposes

**src/Jellyfin.Plugin.EmbyAuth/**
- Purpose: The plugin assembly
- Contains: Namespace `Jellyfin.Plugin.EmbyAuth` for all files in the project root, `Jellyfin.Plugin.EmbyAuth.Configuration` for the settings model, and `Jellyfin.Plugin.EmbyAuth.Api` for the controller (`Api/EmbyAuthController.cs:14`)
- Key files: `EmbyAuthenticationProvider.cs` (login method), `EmbyClient.cs` (only Emby contact), `EmbyLoginMethodUsers.cs` (list shared by the task and the API), `PluginServiceRegistrator.cs` (DI)

**src/Jellyfin.Plugin.EmbyAuth/Api/**
- Purpose: The plugin's HTTP API
- Contains: `EmbyAuthController` with `GET EmbyAuth/Migration` and `POST EmbyAuth/Migration/Run`, both restricted to administrators by `[Authorize(Policy = Policies.RequiresElevation)]` (`EmbyAuthController.cs:23-24, 37, 53`)

**src/Jellyfin.Plugin.EmbyAuth/Configuration/**
- Purpose: Settings model and settings page
- Contains: `PluginConfiguration.cs` and `configPage.html`, embedded by `Jellyfin.Plugin.EmbyAuth.csproj:22-25` and served by `EmbyAuthPlugin.GetPages` (`EmbyAuthPlugin.cs:43-53`). The page has the settings form and a Migration section that calls the migration API (`configPage.html:47-55, 63-105`).
- Persistence: Jellyfin saves `PluginConfiguration` with `XmlSerializer` (`PluginConfiguration.cs:57`)

**tests/Jellyfin.Plugin.EmbyAuth.Tests/**
- Purpose: Unit tests of the plugin classes that do not need a Jellyfin server
- Contains: One `{ClassName}Tests.cs` file per tested class, and `TestDoubles.cs`
- No unit test file exists for `EmbyAuthenticationProvider`, `DefaultLoginMethod`, `MoveToDefaultLoginMethod`, `MoveEmbyUsersToDefaultTask`, `EmbyLoginMethodUsers`, or `EmbyAuthController`. The e2e tests cover their behavior, including the migration API (`e2e/30-migration-modes.bats:95`, `e2e/30-migration-modes.bats:112`).

**tests/scripts/**
- Purpose: bats tests for `scripts/package.sh`: usage errors, zip contents, `meta.json`, `manifest.json`, and `check-tag` (`tests/scripts/package.bats:20-76`)
- Run by: `mise run test`, after the unit tests (`.mise.toml:28-33`)

**e2e/**
- Purpose: End-to-end tests against Emby and Jellyfin containers
- Contains: Independent `NN-topic.bats` files, `setup_suite.bash`, `helpers.bash`, `compose.yaml`, `emby-proxy.conf`
- Flow: `setup_suite` starts the servers and creates every Emby user once. Files `10` to `40` create their own Jellyfin accounts in `setup_file` and reset the plugin settings; `90-jellyfin-log.bats` runs last. `teardown_suite` removes the containers unless `KEEP_E2E=1` (`e2e/setup_suite.bash:38-44`, `.claude/rules/e2e.md:10-14`).
- Ports: `EMBY_PORT` and `JELLYFIN_PORT`, default 18096 and 28096, set the host ports (`e2e/helpers.bash:7-10`, `.claude/rules/e2e.md:24`).

**scripts/**
- Purpose:
  - `dev-env.sh` starts a local demo from `e2e/compose.yaml` under the Compose project name `emby-auth-dev`, on default ports 18196 and 28196, and sources `e2e/helpers.bash` (`scripts/dev-env.sh:4-19, 22`). It requires the action `up`, `status`, or `down` (`scripts/dev-env.sh:75-91`).
  - `package.sh` builds `jellyfin-plugin-emby-auth_<version>.zip` and `manifest.json` in `artifacts/release/`, or checks that a tag is `v<version>` (`scripts/package.sh:4-13`). It requires the action `build` or `check-tag TAG` (`scripts/package.sh:107-124`).
- Referenced by: `CLAUDE.md:11, 14, 38`, `.claude/rules/e2e.md:13`, `docs/development.md:14-15`, `.mise.toml:37`, `.github/workflows/release.yml:35`

**docs/**
- Purpose: User and developer documentation. `README.md` stays short and links here (`README.md:54-61`, `CLAUDE.md:3, 50`).
- `images/settings-page.png` is the README screenshot, taken from the demo (`CLAUDE.md:39`).

**.github/workflows/**
- Purpose: CI and releases. `ci.yml` runs on pull requests and on push to `main`, with jobs `lint`, `test`, and `e2e`, each one mise task, and `ci-success`, which requires every job (`ci.yml:5-9, 19-81`). `release.yml` runs on a pushed `v*` tag (`release.yml:6-9, 32-48`).
- Local runs: `act pull_request -j <job>`, with the runner image in `.actrc` (`CLAUDE.md:13`).

**.claude/rules/**
- Purpose: Path-scoped instructions: `plugin.md` for `src/` and `tests/`, `e2e.md` for `e2e/`

**.planning/codebase/**
- Purpose: Codebase map documents written by `/gsd-map-codebase`

**artifacts/**
- Purpose: Build output (`.gitignore:3`)
  - `plugin/` receives `dotnet publish` output for the e2e tests and the demo (`e2e/setup_suite.bash:12`, `scripts/dev-env.sh:29`) and is mounted into the Jellyfin container (`e2e/compose.yaml:25`)
  - `release/` receives the zip and `manifest.json` (`scripts/package.sh:21`)
  - `package-stage/` holds the publish output and zip contents during `package.sh build` (`scripts/package.sh:22`)

## Key File Locations

**Entry Points:**
- `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs:60` — `Authenticate(string, string, User?)`, called for each login that the plugin handles
- `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs:125` — `ChangePassword`, called for password changes of users on the Emby login method
- `src/Jellyfin.Plugin.EmbyAuth/MoveToDefaultLoginMethod.cs:31` — `OnEvent`, called after a login
- `src/Jellyfin.Plugin.EmbyAuth/MoveEmbyUsersToDefaultTask.cs:57` — `ExecuteAsync`, called when the migration task runs
- `src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs:39` — `GetMigrationStatus`, `GET /EmbyAuth/Migration`
- `src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs:55` — `RunMigration`, `POST /EmbyAuth/Migration/Run`
- `src/Jellyfin.Plugin.EmbyAuth/PluginServiceRegistrator.cs:26` — `RegisterServices`, called by Jellyfin at startup

**Configuration:**
- `src/Jellyfin.Plugin.EmbyAuth/Configuration/PluginConfiguration.cs` — settings model and enums
- `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html` — settings page and Migration section
- `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthSettings.cs` — validation
- `Directory.Build.props`, `global.json`, `.mise.toml`, `.pre-commit-config.yaml` — build and version, test runner, tools and tasks, hooks

**Core Logic:**
- `src/Jellyfin.Plugin.EmbyAuth/LoginDecision.cs` — account decision
- `src/Jellyfin.Plugin.EmbyAuth/AccountAccessPolicy.cs` — account access

**External Communication:**
- `src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs` — every Emby request
- `src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs` — user list cache

**State & Migration:**
- `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs` — fingerprint file
- `src/Jellyfin.Plugin.EmbyAuth/DefaultLoginMethod.cs` — single-column move
- `src/Jellyfin.Plugin.EmbyAuth/MoveToDefaultLoginMethod.cs` — move after login
- `src/Jellyfin.Plugin.EmbyAuth/EmbyLoginMethodUsers.cs` — users on the Emby login method and the readiness rule (line 51)
- `src/Jellyfin.Plugin.EmbyAuth/MoveEmbyUsersToDefaultTask.cs` — migration task
- `src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs` — migration API

**Testing:**
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs` — HTTP stub, manual clock, capturing logger
- `e2e/helpers.bash` — shared bats helpers; `COMPOSE_FILE` points to `e2e/compose.yaml` (`e2e/helpers.bash:14`)
- `tests/scripts/package.bats` — packaging script tests

**Build, CI, and release:**
- `.mise.toml` — the `lint`, `test`, `package`, and `e2e` tasks that CI runs (`.mise.toml:16-41`)
- `.github/workflows/ci.yml`, `.github/workflows/release.yml`
- `scripts/package.sh`

## Naming Conventions

**Files:**
- C# files are named after their main type (`EmbyClient.cs` for `EmbyClient`). Small related types share the file: `EmbyLogin` and `EmbyUser` in `EmbyClient.cs`, `EmbyUserStatus` in `EmbyUserDirectory.cs`, `JellyfinAccount` and `LoginAction` in `LoginDecision.cs`, `EmbyLoginMethodUser` in `EmbyLoginMethodUsers.cs`, `MigrationStatus` and `MigrationUser` in `Api/EmbyAuthController.cs`, the two enums in `PluginConfiguration.cs`.
- Unit test files: `{ClassName}Tests.cs`
- bats suite files: `setup_suite.bash`, `helpers.bash`
- bats e2e test files: `NN-topic.bats`, with a two-digit order prefix and a kebab-case topic (`10-login-checks.bats`, `90-jellyfin-log.bats`)
- bats script test files: named after the script (`tests/scripts/package.bats` for `scripts/package.sh`)
- Scripts: lowercase `.sh`, kebab-case for more than one word (`scripts/dev-env.sh`, `scripts/package.sh`)
- Documentation: lowercase kebab-case `.md` in `docs/` (`how-it-works.md`)

**Directories:**
- Project directories use the dotted project name (`Jellyfin.Plugin.EmbyAuth`, `Jellyfin.Plugin.EmbyAuth.Tests`); namespace subdirectories are PascalCase (`Api/`, `Configuration/`)
- Top-level directories are lowercase (`src/`, `tests/`, `e2e/`, `scripts/`, `docs/`, `artifacts/`)

**Types:**
- PascalCase for classes, records, and enums
- Public: `EmbyAuthPlugin`, `PluginServiceRegistrator`, `MoveEmbyUsersToDefaultTask`, `EmbyVerifiedPasswords`, `PluginConfiguration`, `MigrationMode`, `AccountAccess`, `EmbyAuthController`, `MigrationStatus`, `MigrationUser`. All other types are `internal`.

**Members:**
- Methods: PascalCase; async methods that the plugin defines end in `Async` (`GetStatusAsync`, `MoveAsync`, `ListAsync`, `CreateAccountAsync`). Jellyfin interface methods keep their names (`Authenticate`, `OnEvent`). Controller actions are named for the action (`GetMigrationStatus`, `RunMigration`).
- Log methods: `private static partial void Log{Event}` with `[LoggerMessage]` (`LogLoginRejected`, `LogSettingsInvalid`)
- Private fields: `_camelCase` (`_snapshot`, `_lock`, `_verifiedPasswords`)
- Parameters and locals: camelCase (`username`, `passwordHash`, `embyLogin`)
- Unit test methods: PascalCase phrases with underscores (`Login_ReturnsNull_WhenEmbyIsUnreachable`, `Rejects_UnknownMigrationMode`)
- bats tests: plain-sentence names (`@test "the plugin ends its Emby session after each login"`)
- Settings page element IDs: PascalCase, prefixed `EmbyAuth` for the Migration section (`EmbyAuthMigrationSummary`, `EmbyAuthRunMigration` in `configPage.html:49-51`)

## Where to Add New Code

**Change to the login flow:**
- Primary code: `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs`
- Account decision: `src/Jellyfin.Plugin.EmbyAuth/LoginDecision.cs`
- Unit tests: `tests/Jellyfin.Plugin.EmbyAuth.Tests/`
- End-to-end test: an existing or new `e2e/NN-topic.bats`. `CLAUDE.md:44` requires an end-to-end test for a change that depends on Jellyfin or Emby behavior.

**Change to the migration:**
- Who is listed and who is ready: `src/Jellyfin.Plugin.EmbyAuth/EmbyLoginMethodUsers.cs`, which both the task and the API use
- The move itself: `src/Jellyfin.Plugin.EmbyAuth/MoveEmbyUsersToDefaultTask.cs` and `src/Jellyfin.Plugin.EmbyAuth/DefaultLoginMethod.cs`
- End-to-end test: `e2e/30-migration-modes.bats`

**New API endpoint:**
- Implementation: `src/Jellyfin.Plugin.EmbyAuth/Api/`, with `[Authorize(Policy = Policies.RequiresElevation)]` and an e2e test that a regular user gets 403 (`.claude/rules/plugin.md:48`)
- Settings page call: `ApiClient.getJSON(ApiClient.getUrl(...))` or `ApiClient.ajax(...)`; responses have PascalCase names (`.claude/rules/plugin.md:49`)
- Docs: the Migration API table in `docs/migration.md` (`docs/migration.md:27-34`)

**New class:**
- Implementation: `src/Jellyfin.Plugin.EmbyAuth/{ClassName}.cs`, `internal` unless Jellyfin must discover it
- Registration: `PluginServiceRegistrator.RegisterServices` (`PluginServiceRegistrator.cs:26-36`) if it needs DI. A scheduled task is discovered by type and must be public, with public constructor parameter types (`.claude/rules/plugin.md:22`).
- Tests: `tests/Jellyfin.Plugin.EmbyAuth.Tests/{ClassName}Tests.cs`

**New setting:**
- Property: `PluginConfiguration` in `src/Jellyfin.Plugin.EmbyAuth/Configuration/PluginConfiguration.cs` (keep URLs as strings)
- Validation: `EmbyAuthSettings.FindProblem` (`EmbyAuthSettings.cs:36`) and the `EmbyAuthSettings` record
- UI: `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html`; take a new screenshot for `docs/images/settings-page.png` when the page changes (`CLAUDE.md:50`)
- Tests: `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthSettingsTests.cs`
- Docs: `docs/settings.md`

**New script:**
- Implementation: `scripts/{name}.sh` with an explicit action argument, as `scripts/dev-env.sh:75-91` and `scripts/package.sh:107-124` do
- Tests: `tests/scripts/{name}.bats`, run by `mise run test` (`.mise.toml:32`)
- Lint: `mise run lint` runs `shellcheck -x` and `shfmt -d` on `scripts/` and `tests/scripts/` (`.mise.toml:22-23`)

**Helpers:**
- Plugin: no shared utility folder; helpers live in the class that uses them
- Unit tests: `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs`
- e2e: `e2e/helpers.bash`. `scripts/dev-env.sh` also uses it, so run `scripts/dev-env.sh up` and `down` after a change (`.claude/rules/e2e.md:13`).

**Test data:**
- Unit tests: inline in the test class
- e2e: create Emby users only in `e2e/setup_suite.bash`, because the plugin caches the Emby user list for 60 seconds; create Jellyfin accounts in the `setup_file` of the `.bats` file that uses them (`.claude/rules/e2e.md:11, 18`)

## Special Directories

**artifacts/:**
- Purpose: Build output (`plugin/`, `release/`, `package-stage/`)
- Generated: Yes
- Committed: No (`.gitignore:3`)

**bin/, obj/:**
- Purpose: Per-project build output
- Generated: Yes (by `dotnet build`)
- Committed: No (`.gitignore:1-2`)

**TestResults/:**
- Purpose: Test run output
- Generated: Yes
- Committed: No (`.gitignore:4`)

**.planning/codebase/:**
- Purpose: Codebase map documents
- Generated: Yes (by `/gsd-map-codebase`)

## Dependency Resolution

`Jellyfin.Plugin.EmbyAuth.slnx` lists two projects:
- `src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj` — references `Jellyfin.Controller` and `Jellyfin.Model` 12.1.0 with `ExcludeAssets` `runtime` (`Jellyfin.Plugin.EmbyAuth.csproj:9-16`)
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/Jellyfin.Plugin.EmbyAuth.Tests.csproj` — references `Jellyfin.Controller` 12.1.0, `xunit.v3` 4.0.1, and the plugin project

The plugin project exposes internal types to the test project with `InternalsVisibleTo` (`Jellyfin.Plugin.EmbyAuth.csproj:19`). `Directory.Build.props` sets the version and the target framework `net10.0` for both projects. Tool versions are pinned in `.mise.toml:1-10`.

## Navigating to Common Tasks

| Task | Navigate To |
|------|-------------|
| Change a login check | `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs:60` |
| Change which account a login applies to | `src/Jellyfin.Plugin.EmbyAuth/LoginDecision.cs:40` |
| Change account access | `src/Jellyfin.Plugin.EmbyAuth/AccountAccessPolicy.cs` |
| Change an Emby request | `src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs` |
| Change the saved-password check | `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs:80-86` and `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs` |
| Change cache duration or retry delay | `src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs:39, 44` |
| Change when users move to Default | `src/Jellyfin.Plugin.EmbyAuth/MoveToDefaultLoginMethod.cs`, `src/Jellyfin.Plugin.EmbyAuth/MoveEmbyUsersToDefaultTask.cs`, `src/Jellyfin.Plugin.EmbyAuth/DefaultLoginMethod.cs` |
| Change who counts as ready to move | `src/Jellyfin.Plugin.EmbyAuth/EmbyLoginMethodUsers.cs:51` |
| Change the migration API or the Migration section | `src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs`, `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html:47-55, 63-105` |
| Add a setting | `src/Jellyfin.Plugin.EmbyAuth/Configuration/PluginConfiguration.cs`, `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthSettings.cs:36`, `Configuration/configPage.html`, `docs/settings.md` |
| Write a unit test | `tests/Jellyfin.Plugin.EmbyAuth.Tests/{ClassName}Tests.cs` |
| Write an e2e test | `e2e/NN-topic.bats`, with helpers from `e2e/helpers.bash` |
| Change packaging or the release | `scripts/package.sh`, `tests/scripts/package.bats`, `.github/workflows/release.yml` |
| Change a CI step | The mise task in `.mise.toml`, not only `.github/workflows/ci.yml` (`CLAUDE.md:16`) |

---

*Structure analysis: 2026-09-16*
