---
last_mapped_commit: fb0fd999638d88fabb37bd9d449c23226a5b2f8b
---

# Codebase Structure

**Analysis Date:** 2026-09-20

**Scope:** Full repository at commit fb0fd99 (branch: gsd/phase-04-emby-traffic-under-load-and-failure)

## Directory Layout

```
jellyfin-plugin-emby-auth/
├── src/Jellyfin.Plugin.EmbyAuth/               # Plugin project (.NET 10 C#)
│   ├── Api/
│   │   └── EmbyAuthController.cs               # Admin-only migration API (GET/POST)
│   ├── Configuration/
│   │   ├── PluginConfiguration.cs              # Settings model; MigrationMode, AccountAccess enums
│   │   └── configPage.html                     # Settings page (embedded resource)
│   ├── AccountAccessPolicy.cs                  # Applies remote access and library access settings
│   ├── EmbyAuthenticationProvider.cs           # IAuthenticationProvider (the login method)
│   ├── EmbyAuthPlugin.cs                       # BasePlugin<PluginConfiguration>; plugin ID, name, lifecycle
│   ├── EmbyAuthSettings.cs                     # Validates PluginConfiguration into a typed record
│   ├── EmbyClient.cs                           # Only code that contacts Emby; EmbyLogin, EmbyUser records
│   ├── EmbyLoginMethodUsers.cs                 # Lists users on Emby method with migration state
│   ├── EmbyMigrationTask.cs                    # IScheduledTask (batch migration)
│   ├── EmbyUserDirectory.cs                    # Cached Emby user list; EmbyUserStatus enum
│   ├── EmbyVerifiedPasswords.cs                # Persistent fingerprint file of verified password hashes
│   ├── LoginDecision.cs                        # Pure function deciding Deny/UseAccount/CreateAccount
│   ├── LoginMethodMove.cs                      # Conditional single-column database move; MoveTarget enum
│   ├── MigrationTargetValidation.cs            # Validates move targets against enabled providers
│   ├── MoveAfterLogin.cs                       # IEventConsumer (move after login event)
│   ├── PluginServiceRegistrator.cs             # Wires DI container
│   └── Jellyfin.Plugin.EmbyAuth.csproj         # Package pins, AnalysisMode, embedded page
│
├── tests/
│   ├── Jellyfin.Plugin.EmbyAuth.Tests/         # Unit tests (xUnit v3, .NET 10)
│   │   ├── AccountAccessPolicyTests.cs
│   │   ├── EmbyAuthControllerTests.cs          # Migration API (GET status, POST run)
│   │   ├── EmbyAuthenticationProviderTests.cs  # Login method behavior
│   │   ├── EmbyAuthPluginTests.cs              # Plugin lifecycle
│   │   ├── EmbyAuthSettingsTests.cs            # Settings validation
│   │   ├── EmbyClientTests.cs                  # Emby HTTP calls
│   │   ├── EmbyLoginMethodUsersTests.cs        # User listing with readiness
│   │   ├── EmbyMigrationTaskTests.cs           # Migration task execution
│   │   ├── EmbyUserDirectoryTests.cs           # Caching behavior
│   │   ├── EmbyVerifiedPasswordsTests.cs       # Fingerprint persistence
│   │   ├── Jellyfin.Plugin.EmbyAuth.Tests.csproj
│   │   ├── LoginDecisionTests.cs               # Account decision logic
│   │   ├── LoginMethodMoveTests.cs             # Database move operations
│   │   ├── MigrationTargetValidationTests.cs   # Target validation
│   │   ├── MoveAfterLoginTests.cs              # Event consumer
│   │   ├── TestDoubles.cs                      # HTTP stub, time provider, logger stub
│   │   └── TypeVisibilityTests.cs              # Enforces internal types
│   ├── js/                                     # JavaScript tests (node:test + jsdom)
│   │   ├── configPage.test.js                  # Settings page tests
│   │   ├── testHelpers.js                      # Dashboard/ApiClient stubs, DOM helpers
│   │   └── package.json
│   └── scripts/
│       └── package.bats                        # Tests for scripts/package.sh
│
├── e2e/                                        # End-to-end tests (bats + Docker)
│   ├── compose.yaml                            # Emby + Jellyfin containers
│   ├── helpers.bash                            # Test utilities (API, readiness, proxy)
│   ├── setup_suite.bash                        # Build, publish, start containers, configure
│   ├── 10-login-checks.bats                    # Basic login flows
│   ├── 20-accounts.bats                        # Account behavior
│   ├── 30-migration-modes.bats                 # Both migration modes
│   └── proxy/                                  # Logging proxy for Emby requests
│
├── docs/                                       # Documentation
│   ├── how-it-works.md                         # Login flow, migration, verified passwords
│   ├── settings.md                             # All settings and their effects
│   ├── migration.md                            # Migration behavior and states
│   ├── development.md                          # Jellyfin setup, build, test, release
│   └── project-management.md                   # Issue triage, release process
│
├── scripts/
│   ├── package.sh                              # Build, test, create zip, manifest
│   └── dev-env.sh                              # Demo environment (Emby + Jellyfin)
│
├── README.md                                   # Installation, configuration, quick start
├── Directory.Build.props                       # Package version, target framework
├── .github/workflows/
│   ├── ci.yml                                  # Build, lint, test, e2e, release
│   └── ...
├── .pre-commit-config.yaml                     # Linting hooks (eslint, prettier, shfmt, etc.)
└── .mise.toml                                  # Tool versions (dotnet, node, bats, etc.)
```

## Directory Purposes

**`src/Jellyfin.Plugin.EmbyAuth/`:**
- Purpose: Plugin source code
- Contains: C# classes, settings page HTML, project configuration
- Key files: Entry points (`EmbyAuthenticationProvider`, `MoveAfterLogin`, `EmbyMigrationTask`), core logic (`LoginDecision`, `AccountAccessPolicy`), state (`EmbyUserDirectory`, `EmbyVerifiedPasswords`), external integration (`EmbyClient`, `LoginMethodMove`)

**`tests/Jellyfin.Plugin.EmbyAuth.Tests/`:**
- Purpose: Unit tests
- Contains: xUnit v3 tests, test doubles (HTTP stub, time provider, logger)
- Key files: `TestDoubles.cs` (shared test infrastructure), individual component tests
- Run: `mise run test`

**`tests/js/`:**
- Purpose: Settings page JavaScript tests
- Contains: node:test tests with jsdom, test helpers for Dashboard/ApiClient stubs
- Key files: `configPage.test.js`, `testHelpers.js`
- Run: `mise run test` (runs JavaScript tests via node:test)

**`tests/scripts/`:**
- Purpose: Shell script tests
- Contains: bats tests for `scripts/package.sh`
- Run: `mise run test`

**`e2e/`:**
- Purpose: End-to-end integration tests
- Contains: bats tests, Docker Compose file, Emby proxy for logging
- Key files: `setup_suite.bash` (fixture setup), `helpers.bash` (API utilities), test files
- Run: `mise run e2e` (requires Docker)

**`docs/`:**
- Purpose: User and developer documentation
- Contains: How-it-works guide, settings reference, migration guide, development setup
- Key files: `how-it-works.md` (architecture and flow), `settings.md` (all settings), `development.md` (build/test/release)

**`scripts/`:**
- Purpose: Build and demo automation
- Contains: `package.sh` (build, test, zip release), `dev-env.sh` (demo environment)

## Key File Locations

**Entry Points:**
- `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs` — Login method
- `src/Jellyfin.Plugin.EmbyAuth/MoveAfterLogin.cs` — Event consumer
- `src/Jellyfin.Plugin.EmbyAuth/EmbyMigrationTask.cs` — Scheduled task
- `src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs` — API controller
- `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html` — Settings page

**Configuration:**
- `src/Jellyfin.Plugin.EmbyAuth/Configuration/PluginConfiguration.cs` — Settings model
- `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthSettings.cs` — Settings validation
- `src/Jellyfin.Plugin.EmbyAuth/MigrationTargetValidation.cs` — Target validation

**Core Logic:**
- `src/Jellyfin.Plugin.EmbyAuth/LoginDecision.cs` — Account decision (pure function)
- `src/Jellyfin.Plugin.EmbyAuth/AccountAccessPolicy.cs` — Permission application
- `src/Jellyfin.Plugin.EmbyAuth/LoginMethodMove.cs` — Database move operation

**State Management:**
- `src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs` — Cached Emby user list
- `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs` — Verified password fingerprints

**External Integration:**
- `src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs` — Emby HTTP calls
- `src/Jellyfin.Plugin.EmbyAuth/EmbyLoginMethodUsers.cs` — Jellyfin user list query

**Test Infrastructure:**
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs` — HTTP stub, time provider, logger
- `tests/js/testHelpers.js` — Dashboard/ApiClient stubs

## Naming Conventions

**Files:**
- `{Component}.cs` — Single class or record (e.g., `LoginDecision.cs`)
- `{Component}Tests.cs` — Unit tests for component (e.g., `LoginDecisionTests.cs`)
- `{Feature}.bats` — End-to-end test suite (e.g., `10-login-checks.bats`)
- `{word}Page.html` — Web page resource (e.g., `configPage.html`)

**Directories:**
- `src/` — Source code
- `tests/` — Test code (all types)
- `e2e/` — End-to-end tests
- `docs/` — Documentation
- `scripts/` — Automation scripts
- `Api/` — REST API controllers and DTOs
- `Configuration/` — Settings and UI

**Classes:**
- `{Feature}{Responsibility}` — e.g., `EmbyAuthenticationProvider`, `LoginMethodMove`
- `{Feature}Tests` — Unit test class
- `{Adjective}{Noun}` for enums — e.g., `EmbyUserStatus`, `MigrationUserState`

**Methods:**
- `camelCase` — All methods and properties
- `Async` suffix — Async methods (e.g., `AuthenticateAsync`)
- `Try{Action}` — Methods that return bool and out param (e.g., `TryCreate`)

## Where to Add New Code

**New Feature (e.g., email notification on move):**
- Implementation: `src/Jellyfin.Plugin.EmbyAuth/{Feature}.cs` (new file)
- Tests: `tests/Jellyfin.Plugin.EmbyAuth.Tests/{Feature}Tests.cs` (new file)
- E2E: Add test case to relevant `.bats` file in `e2e/`
- Docs: Update `docs/how-it-works.md` or create new doc

**New Component/Module (e.g., cache invalidation, new validation rule):**
- Implementation: `src/Jellyfin.Plugin.EmbyAuth/{Component}.cs` (new file if significant, else add to related file)
- Tests: `tests/Jellyfin.Plugin.EmbyAuth.Tests/{Component}Tests.cs`
- DI registration: Add to `PluginServiceRegistrator.RegisterServices()` if it needs injection

**Settings/Configuration:**
- Settings property: `src/Jellyfin.Plugin.EmbyAuth/Configuration/PluginConfiguration.cs`
- Validation: `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthSettings.cs` (for basic structure) or `MigrationTargetValidation.cs` (for target validation)
- UI field: `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html`
- Tests: `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthSettingsTests.cs`

**Utilities/Helpers:**
- Shared by multiple components: `src/Jellyfin.Plugin.EmbyAuth/{Utility}.cs`
- Test helpers: `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs`
- JavaScript helpers: `tests/js/testHelpers.js`

**Database/ORM:**
- Query/update logic: `src/Jellyfin.Plugin.EmbyAuth/LoginMethodMove.cs` (or new file for complex queries)
- Must use `ExecuteUpdateAsync` for moves to avoid overwriting concurrent changes

**External API (Emby):**
- All Emby calls: `src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs` (this is the only file that contacts Emby)
- New endpoint: Add method to `EmbyClient`, then call from appropriate component

## Special Directories

**`artifacts/`:**
- Purpose: Build outputs
- Generated: Yes (by `scripts/package.sh`)
- Committed: No (.gitignore)
- Contents: Release zip, manifest, plugin DLL

**`TestResults/`:**
- Purpose: Test execution results
- Generated: Yes (by test runners)
- Committed: No (.gitignore)
- Contents: Coverage reports, xUnit XML results

**`.github/workflows/`:**
- Purpose: CI/CD pipeline
- Generated: No (hand-written)
- Committed: Yes
- Key file: `ci.yml` (build, lint, test, e2e, release)

**`.planning/codebase/`:**
- Purpose: Architecture and codebase documentation
- Generated: Yes (by `/gsd-map-codebase` command)
- Committed: Yes
- Contents: ARCHITECTURE.md, STRUCTURE.md, CONVENTIONS.md, etc.

---

*Structure analysis: 2026-09-20*
