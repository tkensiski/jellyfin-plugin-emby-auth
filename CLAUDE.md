# Jellyfin Emby Auth

Jellyfin 12.1 authentication plugin in C# (.NET 10). It checks the first Jellyfin login of a user against Emby, saves a Jellyfin password hash, and moves the user to Jellyfin's Default login method. `README.md` describes the behavior and the admin procedures.

## Commands

- `mise install` — install the pinned tools (`.mise.toml`).
- `mise run lint` — check C# formatting, and run shellcheck, shfmt, actionlint, and zizmor.
- `mise run test` — build with warnings as errors, then run the unit tests.
- `mise run e2e` — run the end-to-end tests against Emby and Jellyfin containers. Needs Docker.
- `prek run` — run the pre-commit hooks, which call `mise run lint` and `mise run test`.
- `act pull_request -j <job>` — run a CI job from `.github/workflows/ci.yml` in a container. `.actrc` pins the runner image. The `e2e` job also needs `--bind --container-options "--network host"`.

CI (`.github/workflows/ci.yml`) runs one mise task per job, and `ci-success` requires every job. When a CI step changes, change the mise task, not only the workflow.

## Layout

- `src/Jellyfin.Plugin.EmbyAuth/`
  - `EmbyAuthenticationProvider.cs` — the login method. Connects the parts below to Jellyfin's `IUserManager`.
  - `LoginDecision.cs` — the account rules, as a pure function.
  - `EmbyAuthSettings.cs` — settings validation.
  - `EmbyClient.cs` — the only code that sends requests to Emby.
  - `EmbyUserDirectory.cs` — the cached Emby user list.
  - `VerifiedLogins.cs`, `MoveToDefaultLoginMethod.cs` — the move to Default after a login that Emby verified.
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/` — xUnit v3 unit tests. `TestDoubles.cs` has the HTTP stub, the manual clock, and the capturing logger.
- `e2e/` — bats tests, Docker Compose file, and the logging proxy for Emby.

## Rules

- Write the test first. Then break the code once and watch the test fail.
- A change that depends on Jellyfin or Emby behavior needs an end-to-end test. Run `mise run e2e` for every change to `src/`.
- Run `prek run` before each commit.
- Never put a password or the API key in a log or exception message.
- Warnings are errors, and the plugin project uses `AnalysisMode` `AllEnabledByDefault`. Fix a warning. Suppress it only with a `Justification`.
- Pin exact versions. Look up the current stable version before a bump.
- A Jellyfin version bump changes three pins together: `Jellyfin.Controller` and `Jellyfin.Model`, the `jellyfin/jellyfin` image tag in `e2e/compose.yaml`, and the target framework.
- Keep `README.md` accurate when behavior changes.

Path-scoped rules add details: `.claude/rules/plugin.md` for `src/` and `tests/`, and `.claude/rules/e2e.md` for `e2e/`.
