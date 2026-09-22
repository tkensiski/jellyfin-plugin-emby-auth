# Jellyfin Emby Auth

Jellyfin 12.1 authentication plugin in C# (.NET 10). It checks Jellyfin logins against Emby, saves a Jellyfin password hash, and moves users to Jellyfin's Default login method, at once or when an administrator runs the migration task. `README.md` is the short introduction (install, configure, migrate). `docs/` has the details: `how-it-works.md`, `settings.md`, `migration.md`, and `development.md`.

## Commands

- `mise install` — install the pinned tools (`.mise.toml`).
- `mise run lint` — check C# formatting, and run shellcheck, shfmt, actionlint, and zizmor.
- `mise run test` — build with warnings as errors, then run the unit tests, the `scripts/package.sh` tests (`tests/scripts/`), and the settings-page tests (`tests/js/`).
- `mise run e2e` — run the end-to-end tests against Emby and Jellyfin containers. Needs Docker. `bats e2e/NN-topic.bats` runs one file.
- `mise run package` — build `artifacts/release/jellyfin-plugin-emby-auth_<version>.zip` and `manifest.json` with `scripts/package.sh build`.
- `prek run` — run the pre-commit hooks, which call `mise run lint` and `mise run test`.
- `act pull_request -j <job>` — run a CI job from `.github/workflows/ci.yml` in a container. `.actrc` pins the runner image. The `e2e` job also needs `--bind --container-options "--network host"`.
- `scripts/dev-env.sh up|status|down` — a demo with Emby and Jellyfin in Docker on ports 18196 and 28196, for manual checks in a browser.

CI (`.github/workflows/ci.yml`) runs one mise task per job, and `ci-success` requires every job. When a CI step changes, change the mise task, not only the workflow.

A pushed `v<version>` tag runs `.github/workflows/release.yml`: `scripts/package.sh check-tag`, `mise run test`, `mise run package`, then `gh release create`. The version comes from `Directory.Build.props`. Jellyfin reads the zip checksum as MD5.

## Layout

- `src/Jellyfin.Plugin.EmbyAuth/`
  - `EmbyAuthenticationProvider.cs` — the login method. Connects the parts below to Jellyfin's `IUserManager`.
  - `Configuration/PluginConfiguration.cs` — the settings, including the `MigrationMode` and `AccountAccess` enums.
  - `EmbyAuthSettings.cs` — settings validation.
  - `LoginDecision.cs` — the account rules, as a pure function.
  - `AccountAccessPolicy.cs` — applies `AccountAccess` to an account.
  - `EmbyClient.cs` — the only code that sends requests to Emby.
  - `EmbyUserDirectory.cs` — the cached Emby user list.
  - `EmbyVerifiedPasswords.cs` — the file of fingerprints of password hashes that Emby verified.
  - `LoginMethodMove.cs` — the single-column move, and resolving the configured migration target.
  - `MoveAfterLogin.cs` — the move after a login, in `MoveAfterFirstLogin` mode.
  - `EmbyMigrationTask.cs` — the migration task.
  - `EmbyLoginMethodUsers.cs` — the list of users on the Emby login method, with their readiness. The task and the API share it.
  - `Api/EmbyAuthController.cs` — the admin-only migration API (`GET /EmbyAuth/Migration`, `POST /EmbyAuth/Migration/Run`) that the settings page calls.
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/` — xUnit v3 unit tests. `TestDoubles.cs` has the HTTP stub, the manual clock, and the capturing logger.
- `tests/js/` — `node:test` tests for `configPage.html`, run with jsdom against the shipping file. `configPage.test.js` has the tests; `testHelpers.js` has the `ApiClient`/`Dashboard` stubs and DOM helpers, the one shared file of doubles, the same role `TestDoubles.cs` plays for the C# suite.
- `e2e/` — bats tests in independent `NN-topic.bats` files, `setup_suite.bash` (shared servers and Emby users), Docker Compose file, and the logging proxy for Emby.
- `scripts/dev-env.sh` — the demo. It uses `e2e/compose.yaml` and `e2e/helpers.bash`.
- `docs/` — user and developer documentation.

## Rules

- Write the test first. Then break the code once and watch the test fail.
- A change that depends on Jellyfin or Emby behavior needs an end-to-end test. Run `mise run e2e` for every change to `src/`.
- Run `prek run` before each commit.
- Never put a password or the API key in a log or exception message.
- Warnings are errors, and the plugin project uses `AnalysisMode` `AllEnabledByDefault`. Fix a warning. Suppress it only with a `Justification`.
- Pin exact versions. Look up the current stable version before a bump.
- A Jellyfin version bump changes six pins together: `Jellyfin.Controller` and `Jellyfin.Model` in `src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj`; `Microsoft.Data.Sqlite.Core` in the same project, whose version must equal the one in `/jellyfin/jellyfin.deps.json` inside the pinned image — a version above the host's stops the plugin loading; the `jellyfin/jellyfin` image tag in `e2e/compose.yaml`; the target framework in `Directory.Build.props`; `Jellyfin.Controller` again in `tests/Jellyfin.Plugin.EmbyAuth.Tests/Jellyfin.Plugin.EmbyAuth.Tests.csproj`, a second reference to the same package in a second file that can drift from the first; and the two hardcoded `targetAbi` assertions in `tests/scripts/package.bats`, which hold the value `scripts/package.sh` derives from the plugin project's `Jellyfin.Controller` version with a fourth part appended, so they go red on a bump that misses one of the other five pins.
- Keep `README.md` and `docs/` accurate when behavior changes. Keep the README short, and put details in `docs/`.

Path-scoped rules add details: `.claude/rules/plugin.md` for `src/` and `tests/`, and `.claude/rules/e2e.md` for `e2e/`.
