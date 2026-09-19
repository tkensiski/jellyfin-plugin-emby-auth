# Jellyfin Emby Auth

## What This Is

A Jellyfin 12.1 plugin that moves users from Emby to Jellyfin without a password reset. It adds a login method named **Emby** that checks each password against the Emby server, saves a Jellyfin hash of each password that Emby accepts, and moves the user to Jellyfin's Default login method, at once or when the administrator runs the migration. It is for Jellyfin administrators who are shutting down an Emby server.

This milestone makes the plugin ready for a public v1.0.0: every open issue from the codebase map is fixed and tested, and any Jellyfin administrator can install and update the plugin from a manifest URL.

## Core Value

A user moves from Emby to Jellyfin without a password reset, and no password that Emby did not verify ever opens an account.

## Requirements

### Validated

Inferred from the code at commit `ecee1ed` (`.planning/codebase/ARCHITECTURE.md`).

- ✓ Emby login method: refuses a blank password, a disabled account, and an administrator account without contacting Emby — existing
- ✓ Password goes to Emby only when an enabled Emby user has exactly the typed name, ignoring case; the user list is cached for 60 seconds, 30 seconds after a failed read — existing
- ✓ Emby login through `POST /Users/AuthenticateByName` with a 5-second timeout, then `POST /Sessions/Logout` — existing
- ✓ First login creates the Jellyfin account; the Emby name must match the typed name, and an existing account must use the Emby login method — existing
- ✓ Jellyfin password hash saved on the account, and a SHA-256 fingerprint of that hash recorded in `Jellyfin.Plugin.EmbyAuth.VerifiedPasswords.json` — existing
- ✓ Every move to Default, and every saved-password login without Emby, checks the fingerprint — existing
- ✓ Migration behaviors `MoveAfterFirstLogin` and `KeepEmbyInCharge` — existing (`JellyfinPasswordFirst` also exists today; this milestone removes it, AUTH-02)
- ✓ Account access choices `CopyEmbyRemoteAccess`, `NoLibraries`, and `JellyfinDefaults` — existing
- ✓ Password set in Jellyfin moves the user to Default; a password reset keeps the user on the Emby login method — existing
- ✓ Migration task `EmbyAuthMoveUsersToDefault` moves each user whose saved hash Emby verified — existing
- ✓ Admin-only migration API (`GET /EmbyAuth/Migration`, `POST /EmbyAuth/Migration/Run`) and the Migration section of the settings page — existing
- ✓ No password or API key in any log message, checked by unit tests and the e2e log test — existing
- ✓ CI runs lint, unit tests, script tests, and e2e tests on pull requests and on pushes to `main`; a `v*` tag builds a release zip and `manifest.json` — existing

### Active

The full list with IDs is in `.planning/REQUIREMENTS.md` (32 v1 requirements). Summary by group:

- [ ] **Password and account security (AUTH-01..05):** while a user is on the Emby login method, Emby checks every login and the saved hash plays no part; `JellyfinPasswordFirst` is removed; account creation follows Jellyfin's own create-then-save pattern with no failure path that returns HTTP 500 or leaves a passwordless Default account; the Emby session always ends when Emby returns a token.
- [ ] **Verified password records (FPRT-01..03):** a failed write keeps the in-memory record and the docs say so; a failed read never erases records and shows on the settings page.
- [ ] **Settings page (UI-01..03):** load and save failures show a message; the migration list follows the real task state.
- [ ] **Documentation (DOCS-01..04):** correct shutdown step reference; manifest URL and catalog steps; tested versions and the `targetAbi` minimum; complete version bump rule.
- [ ] **Test coverage (TEST-01..06):** unit tests for the untested classes, settings page JavaScript tests, concurrent first logins, invalid settings on a running server.
- [ ] **Performance (PERF-01..02):** a load test with a separate account pool; each bottleneck fixed or accepted with measured numbers.
- [ ] **Release and tooling (REL-01..04):** release only for a tagged commit with a passing `ci-success` run; pedantic zizmor findings resolved; gitleaks in lint; per-version changelog.
- [ ] **Public install (PUB-01..05):** full audit before going public; multi-version manifest on GitHub Pages; public repository with maintainer approval at that time; catalog install and update verified; v1.0.0 published.

### Out of Scope

- Moving the maintainer's own server off Emby — not a completion criterion for this milestone; the chosen criteria are a tagged release and a public install.
- Submission to the official Jellyfin plugin repository — the chosen route is a self-hosted manifest at a stable URL.
- A private repository with a separate public release repository — rejected in favor of a public repository with a stable manifest.
- A manifest that lists only the latest version — rejected, because administrators need a stable URL that keeps every version.
- A manifest file on a branch served from `raw.githubusercontent.com` — rejected in favor of GitHub Pages.
- The `JellyfinPasswordFirst` migration behavior — removed: while a user is on the Emby login method, only Emby decides a login.
- Account creation under a temporary name followed by a rename — rejected in favor of Jellyfin's own create-then-save pattern (`Jellyfin.Api/Controllers/UserController.cs:517-533`, tag `v12.1`).
- Re-running lint and e2e inside `release.yml` — rejected in favor of checking the CI result of the tagged commit.
- Signed or attested releases — Jellyfin's install path checks only the MD5 checksum (`.planning/research/FEATURES.md:92`).
- Automated version bump and changelog tooling — out of proportion for a single maintainer's release cadence (`.planning/research/FEATURES.md:93`).
- Logins with an Emby Connect email address — a documented limit (`docs/how-it-works.md:36`); no change was requested.

## Context

- Brownfield C# plugin, .NET 10, built against Jellyfin 12.1.0. Codebase map: `.planning/codebase/` (commit `ecee1ed`).
- Tested only in local containers with Jellyfin 12.1.0 and Emby 4.10.0.40. Not tested on a production server (`README.md:12`).
- Unit tests (90 cases), e2e tests (27, Docker), and script tests (8) run in CI. The e2e tests use an nginx proxy that logs Emby request bodies, so tests prove whether a password reached Emby.
- The repository is private today (`gh repo view`: `PRIVATE`). `scripts/package.sh:23` writes a manifest for one release with download URLs on this repository's GitHub releases.
- Third-party Jellyfin plugin repositories usually host one manifest that lists all versions on GitHub Pages or a raw branch file, with zips as release assets (jellyfin.org/docs/general/server/plugins; `Kevinjil/jellyfin-plugin-repo-action`; `LizardByte/jellyfin-plugin-repo`).
- Jellyfin behavior that the code depends on is recorded in `.claude/rules/plugin.md`; e2e harness rules are in `.claude/rules/e2e.md`.

## Constraints

- **Tech stack**: C# on .NET 10, Jellyfin 12.1 packages — a Jellyfin version bump changes the package pins, the e2e image tag, and the target framework together (`CLAUDE.md`).
- **Security**: Never put a password or the API key in a log or exception message — repository rule, checked by tests.
- **Security**: Every move to Default checks `EmbyVerifiedPasswords.Matches` — otherwise a password that an administrator set on a pre-created account would work (`.claude/rules/plugin.md`).
- **Security**: Never leave or move a user to Default without a password — the Default login method accepts a blank password for such an account (`DefaultAuthenticationProvider.cs:61-68`, tag `v12.1`). The accepted exception is the moment between `CreateUserAsync` and the plugin's next save, which Jellyfin's own user creation also has, because `CreateUserAsync(string name)` is the only create method (`IUserManager.cs:90`), and the case where that save and the cleanup delete both fail.
- **Quality**: Test first, then break the code once and watch the test fail. A change that depends on Jellyfin or Emby behavior needs an e2e test. Warnings are errors (`CLAUDE.md`).
- **Versions**: Pin exact versions, and look up the current stable version before a bump (`CLAUDE.md`).
- **License**: GPL-3.0, because the Jellyfin packages are GPL-3.0-only (`LICENSE`).
- **Publishing**: Making the repository public cannot be undone for anyone who copied it — the history scan comes first, and the switch needs the maintainer's explicit approval at that time.

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Fix all four issue groups (bugs, release and tooling, test gaps, performance) in one milestone | Maintainer chose all groups for v1.0.0 | — Pending |
| Done means a tagged v1.0.0 and a public install | Maintainer's completion criteria | — Pending |
| Only an Emby-verified password logs in; while a user is on the Emby login method, Emby checks every login and the saved hash is updated after each accepted login | Maintainer's rule; it is the core value | — Pending |
| Remove `JellyfinPasswordFirst` | It accepts a saved hash without asking Emby, which breaks the rule above; the plugin is not public, so no installed configuration depends on it | — Pending |
| Account creation keeps Jellyfin's create-then-save pattern; every failure is caught and never leaves a passwordless Default account | Jellyfin has no create-with-password call, and its own `POST /Users/New` uses the same two steps; maintainer chose parity over a temporary-name rename | — Pending |
| A failed fingerprint write keeps the in-memory record; fix the docs and log, add a test | Emby did verify the password, so the rule holds, and a disk error does not block the migration. Confirmed at the requirements review. | — Pending |
| Public repository with one multi-version manifest on GitHub Pages, published by a separate workflow | Usual setup for third-party Jellyfin plugins; a stable URL; a failed Pages deploy can be re-run without a new release | — Pending |
| Release checks that the tagged commit has a passing `ci-success` run | Proves the exact commit passed CI without re-running e2e; tags must be pushed on commits that CI ran on | — Pending |
| Performance work starts with a load test | None of the three bottlenecks is measured | — Pending |
| The login method that users move to becomes a setting, so a server can end on a login method other than Jellyfin's Default | Measured on 2026-09-18: a user whose account holds an Emby-verified hash logs in through JellyfinSecurity's `TwoFactorAuthProvider`, which checks the hash through Jellyfin's Default provider and sends nothing to Emby. An unenrolled user is not challenged. So Emby login method → another provider is a working end state | — Pending (MIGR-01, Phase 3) |
| `EmbyAuthenticationProvider` stays `internal` | `GetExports<IAuthenticationProvider>()` returns public types only (`ApplicationHost.cs:768`). A public class would join the scan that JellyfinSecurity uses to pick the provider it forwards passwords to, which could send a password to Emby for a user this plugin does not serve | — Pending (MIGR-02, Phase 3) |

## Evolution

This document evolves at phase transitions and milestone boundaries.

**After each phase transition** (via `/gsd-transition`):
1. Requirements invalidated? → Move to Out of Scope with reason
2. Requirements validated? → Move to Validated with phase reference
3. New requirements emerged? → Add to Active
4. Decisions to log? → Add to Key Decisions
5. "What This Is" still accurate? → Update if drifted

**After each milestone** (via `/gsd-complete-milestone`):
1. Full review of all sections
2. Core Value check — still the right priority?
3. Audit Out of Scope — reasons still valid?
4. Update Context with current state

---
*Last updated: 2026-09-17 after requirements definition*
