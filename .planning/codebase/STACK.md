# Technology Stack

**Analysis Date:** 2026-09-16

**Scope:** commit `ecee1ed` on `main`.

## Languages

**Primary:**
- C# — plugin (`src/Jellyfin.Plugin.EmbyAuth/`) and unit tests (`tests/Jellyfin.Plugin.EmbyAuth.Tests/`)

**Secondary:**
- Bash — end-to-end tests (`e2e/*.bats`, `e2e/helpers.bash`, `e2e/setup_suite.bash`), the release packaging script (`scripts/package.sh`) and its tests (`tests/scripts/package.bats`), and the local demo (`scripts/dev-env.sh`)
- HTML/JavaScript — the plugin settings page, embedded as a resource (`src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html`, `src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj:22-25`)
- YAML — GitHub Actions workflows (`.github/workflows/ci.yml`, `.github/workflows/release.yml`), Docker Compose file (`e2e/compose.yaml`), pre-commit config (`.pre-commit-config.yaml`)
- TOML — tool pins and tasks (`.mise.toml`)
- nginx config — the logging proxy for Emby (`e2e/emby-proxy.conf`)

## Runtime

**Environment:**
- .NET SDK 10.0.401, pinned in `.mise.toml:2`
- Target framework `net10.0` for both projects (`Directory.Build.props:6`)
- The plugin runs inside the Jellyfin server process. It has no process of its own.
- `.mise.toml:12-14` sets `DOTNET_CLI_TELEMETRY_OPTOUT=1` and `DOTNET_NOLOGO=1`.

**Package Manager:**
- NuGet through the `dotnet` CLI. Versions are exact in each `*.csproj`. There is no lock file and no central package management file.

## Frameworks

**Core:**
- `Jellyfin.Controller` 12.1.0 — login method, user manager, events, scheduled tasks, plugin base types (`src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj:10-12`)
- `Jellyfin.Model` 12.1.0 (`Jellyfin.Plugin.EmbyAuth.csproj:13-15`)
- Both references use `<ExcludeAssets>runtime</ExcludeAssets>`, so the published plugin does not ship Jellyfin assemblies.
- ASP.NET Core MVC types (`ControllerBase`, `[ApiController]`, `[Authorize]`, `[Route]`) for the migration API, with no extra package reference (`src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs:9-11`, `EmbyAuthController.cs:22-30`).

**Testing:**
- `xunit.v3` 4.0.1 (`tests/Jellyfin.Plugin.EmbyAuth.Tests/Jellyfin.Plugin.EmbyAuth.Tests.csproj:10`). The test project is an executable (`OutputType` `Exe`, line 4) and also references `Jellyfin.Controller` 12.1.0 (line 9).
- Microsoft.Testing.Platform, selected in `global.json:3`. Every command that runs the unit tests passes `--solution Jellyfin.Plugin.EmbyAuth.slnx` (`.mise.toml:31`).
- bats — end-to-end tests (`e2e/`) and script tests (`tests/scripts/`) (`.mise.toml:32`, `.mise.toml:41`)

**Build/Dev tools (all pinned in `.mise.toml:1-10`):**
- dotnet 10.0.401
- bats 1.14.0
- jq 1.8.2 — JSON in the e2e helpers, `scripts/dev-env.sh`, and `scripts/package.sh`
- shellcheck 0.11.0
- shfmt 3.14.1
- actionlint 1.7.12 — GitHub workflow lint
- zizmor 1.30.1 — GitHub workflow security audit, run with `--offline` (`.mise.toml:25`)
- prek 0.5.3 — runs `.pre-commit-config.yaml`
- act 0.2.89 — runs CI jobs in a local container. `.actrc:1` maps `ubuntu-latest` to `catthehacker/ubuntu:act-24.04-20260815`.

`scripts/package.sh` also calls `zip` (line 80) and `openssl` (line 83), and `tests/scripts/package.bats` calls `unzip` (line 37). These are not in `.mise.toml`; they come from the host.

## Key Dependencies

**From Jellyfin.Controller (compile-time only):**
- `MediaBrowser.Controller.Authentication` — `IAuthenticationProvider`, `IRequiresResolvedUser`, `AuthenticationException` (`EmbyAuthenticationProvider.cs:9`)
- `MediaBrowser.Controller.Events` — `IEventConsumer<AuthenticationResultEventArgs>` (`MoveToDefaultLoginMethod.cs:7-8`)
- `MediaBrowser.Model.Tasks` — `IScheduledTask` (`MoveEmbyUsersToDefaultTask.cs:7`) and `ITaskManager` (`Api/EmbyAuthController.cs:8`, `EmbyAuthController.cs:29`)
- `MediaBrowser.Common.Api` — `Policies.RequiresElevation` (`Api/EmbyAuthController.cs:7`, `EmbyAuthController.cs:23`)
- `Jellyfin.Database.Implementations` — `JellyfinDbContext` and the `User` entity, used with EF Core queries and `ExecuteUpdateAsync` (`DefaultLoginMethod.cs:5-6`, `EmbyLoginMethodUsers.cs:6-7`)
- `MediaBrowser.Model.Cryptography` — `ICryptoProvider` and `PasswordHash` for the Jellyfin password hash (`EmbyAuthenticationProvider.cs:11`)
- `MediaBrowser.Common.Net.NamedClient` — the named `HttpClient` from `IHttpClientFactory` (`EmbyClient.cs:11`, `EmbyClient.cs:160`)

**From the .NET base library:**
- `System.Net.Http` and `System.Net.Http.Json` — Emby requests (`EmbyClient.cs:4-5`)
- `System.Text.Json` — Emby request/response bodies and the fingerprint file (`EmbyClient.cs:7`, `EmbyVerifiedPasswords.cs:6`)
- `System.Security.Cryptography.SHA256` — fingerprints of saved password hashes (`EmbyVerifiedPasswords.cs:88`)
- `System.Threading.Lock` — guards the fingerprint dictionary (`EmbyVerifiedPasswords.cs:22`)
- `TimeProvider` — the clock for the user list cache, registered as `TimeProvider.System` (`PluginServiceRegistrator.cs:28`)
- `Microsoft.Extensions.Logging` with source-generated `[LoggerMessage]` methods (for example `EmbyClient.cs:188-207`)

The plugin has no NuGet dependency other than the two Jellyfin packages.

## Configuration

**Build:**
- `Directory.Build.props` — version `1.0.0.0` (lines 3-5), `net10.0`, `Nullable` `enable`, `TreatWarningsAsErrors` `true`. The release version comes from `<Version>` (`scripts/package.sh:29-31`).
- `src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj` — `AnalysisMode` `AllEnabledByDefault` (line 6), `GenerateDocumentationFile` (line 5), `InternalsVisibleTo` the test project (line 19)
- `Jellyfin.Plugin.EmbyAuth.slnx` — XML solution file with the two projects
- `global.json` — test runner selection only. It does not pin the SDK; `.mise.toml` does.

**mise tasks (`.mise.toml:16-41`):** CI runs exactly these tasks (`.mise.toml:16`).
- `lint` — `dotnet format Jellyfin.Plugin.EmbyAuth.slnx --verify-no-changes`; `shellcheck -x` on `e2e/*.bash`, `e2e/*.bats`, `scripts/*.sh`, `tests/scripts/*.bats`; `shfmt -d e2e scripts tests/scripts`; `actionlint`; `zizmor --offline .github/workflows` (lines 18-26)
- `test` — `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx`, then `bats tests/scripts` (lines 28-33)
- `package` — `scripts/package.sh build` (lines 35-37)
- `e2e` — `bats e2e` (lines 39-41)

**Pre-commit hooks (`.pre-commit-config.yaml`):**
- `lint` runs `mise run lint` for `.cs`, `.csproj`, `.slnx`, `.props`, `.sh`, `.bash`, `.bats`, `.yml`/`.yaml` files and `.mise.toml` (lines 4-9)
- `test` runs `mise run test` for C# project files, `.html`, `global.json`, `.mise.toml`, `scripts/`, and `tests/scripts/` (lines 10-15)
- No hook runs `mise run e2e`.

**CI and release (GitHub Actions):**
- `.github/workflows/ci.yml` — on every pull request and on push to `main` (lines 5-9). Jobs `lint`, `test`, and `e2e` each run one mise task on `ubuntu-latest` (lines 19-62). `ci-success` fails unless every job succeeded (lines 64-81). Permissions `contents: read` (lines 11-12); in-progress runs for the same ref are cancelled (lines 14-16).
- `.github/workflows/release.yml` — on a pushed `v*` tag (lines 6-9). Steps: `scripts/package.sh check-tag`, `mise run test`, `mise run package`, then `gh release create` with the zip and `manifest.json` (lines 32-48). The job has `contents: write` (lines 18-19), and `mise-action` runs with `cache: false` (lines 28-30).
- Actions are pinned by commit SHA: `actions/checkout` v7.0.1 and `jdx/mise-action` v4.3.0 (`ci.yml:24`, `ci.yml:29`, `release.yml:22`, `release.yml:27`). Checkout uses `persist-credentials: false`.

**Plugin settings (runtime):**
- `PluginConfiguration` (`Configuration/PluginConfiguration.cs:52-74`): `EmbyServerUrl`, `EmbyApiKey`, `MigrationMode`, `AccountAccess`. Jellyfin saves it with `XmlSerializer`, so the URL is a `string` (`PluginConfiguration.cs:57`).
- Validation: `EmbyAuthSettings.TryCreate` (`EmbyAuthSettings.cs:23-76`)
- Verified-password fingerprints: `Jellyfin.Plugin.EmbyAuth.VerifiedPasswords.json` in Jellyfin's plugin configuration folder (`PluginServiceRegistrator.cs:23`, `PluginServiceRegistrator.cs:31-33`)

## Platform Requirements

**Development:**
- mise, to install the pinned tools (`mise install`, `docs/development.md:3`)
- Docker with Compose, for `mise run e2e` and `scripts/dev-env.sh` (`docs/development.md:9`, `docs/development.md:14`)
- `zip`, `openssl`, and `unzip` on the host for `mise run package` and `mise run test` (see Build/Dev tools)

**Production:**
- Jellyfin 12.1. The plugin is built against the 12.1.0 packages (`README.md:16`). The release `targetAbi` is the `Jellyfin.Controller` version with a fourth part, `12.1.0.0` (`scripts/package.sh:33-38`, `tests/scripts/package.bats:47`).
- An Emby server that Jellyfin reaches over HTTP or HTTPS, and an Emby API key (`README.md:17-18`)
- Tested only in local containers, with Jellyfin 12.1.0 and Emby 4.10.0.40. Not tested on a production server. Not published to a plugin repository (`README.md:12`).
- Install: unzip `jellyfin-plugin-emby-auth_<version>.zip` (from a GitHub release or `mise run package`) into `<jellyfin config>/plugins/EmbyAuth_<version>/`, then restart Jellyfin (`README.md:20-33`).

**E2E container images (`e2e/compose.yaml`):**
- `emby/embyserver:4.10.0.40` (line 5)
- `nginx:1.30.5-alpine` (line 11)
- `jellyfin/jellyfin:12.1.20260915-010956` (line 18)

**License:** GPL-3.0 (`LICENSE:1-2`, `README.md:63-65`).

---

*Stack analysis: 2026-09-16*
