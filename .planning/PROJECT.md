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
- ✓ Migration behaviors `MoveAfterFirstLogin`, `KeepEmbyInCharge`, and `JellyfinPasswordFirst` — existing
- ✓ Account access choices `CopyEmbyRemoteAccess`, `NoLibraries`, and `JellyfinDefaults` — existing
- ✓ Password set in Jellyfin moves the user to Default; a password reset keeps the user on the Emby login method — existing
- ✓ Migration task `EmbyAuthMoveUsersToDefault` moves each user whose saved hash Emby verified — existing
- ✓ Admin-only migration API (`GET /EmbyAuth/Migration`, `POST /EmbyAuth/Migration/Run`) and the Migration section of the settings page — existing
- ✓ No password or API key in any log message, checked by unit tests and the e2e log test — existing
- ✓ CI runs lint, unit tests, script tests, and e2e tests on pull requests and on pushes to `main`; a `v*` tag builds a release zip and `manifest.json` — existing

### Active

Each item comes from `.planning/codebase/CONCERNS.md` (item numbers match the list reviewed on 2026-09-17), except the public install items.

**Password rule (applies to every item below):**
- [ ] A login on the Emby login method succeeds only with a password that Emby verified. An account with no saved Jellyfin password refuses every login until Emby accepts the password.

**Bugs and weak spots:**
- [ ] (1) A failed write of the fingerprint file is described correctly: the log message, the XML doc, and `docs/how-it-works.md` state that the record stays in memory until Jellyfin restarts, and a test covers the write failure.
- [ ] (2) `docs/how-it-works.md` points to the shutdown step that finds users who went back to the Emby login method.
- [ ] (3) An account that the plugin creates never accepts a blank password, including between `CreateUserAsync` and the save of the hash. A failed save or a failed cleanup does not leave an open account and does not return HTTP 500.
- [ ] (4) A fingerprint file that cannot be read does not lose the records in it, and the administrator can find the cause.
- [ ] (5) The settings page shows a message when loading or saving the settings fails, and the migration list shows the result of a finished run.
- [ ] (6) The plugin ends the Emby session whenever Emby returns an access token, including when the login response has no user name.

**Release and tooling:**
- [ ] (7) A release is published only from a commit that passed lint, unit tests, script tests, and e2e tests.
- [ ] (8) The Jellyfin version bump rule names every pin, including the test project reference and the `targetAbi` values in `tests/scripts/package.bats`.
- [ ] (9) Each `zizmor --persona=pedantic` finding is fixed or suppressed with a written reason.

**Test gaps:**
- [ ] (10) The login method, the migration task, the move to Default, the migration API, and the list of users on the Emby login method have unit tests.
- [ ] (11) The settings page JavaScript has automated tests.
- [ ] (12) Concurrent logins and invalid settings on a running server have tests.

**Performance:**
- [ ] (13-15) A load test measures logins with a slow Emby server, concurrent first logins, user list cache expiry, and fingerprint writes. Each of the three bottlenecks is fixed or documented with the measured numbers.

**Public install:**
- [ ] The full git history has no secrets, and the repository is public.
- [ ] One `manifest.json` at a stable URL lists every released version, and the zips stay as GitHub release files.
- [ ] A Jellyfin administrator adds the manifest URL, installs the plugin from the catalog, and receives updates.
- [ ] v1.0.0 is tagged and published through the release workflow.

### Out of Scope

- Moving the maintainer's own server off Emby — not a completion criterion for this milestone; the chosen criteria are a tagged release and a public install.
- Submission to the official Jellyfin plugin repository — the chosen route is a self-hosted manifest at a stable URL.
- A private repository with a separate public release repository — rejected in favor of a public repository with a stable manifest.
- A manifest that lists only the latest version — rejected, because administrators need a stable URL that keeps every version.
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
- **Security**: Every move to Default and every saved-password login checks `EmbyVerifiedPasswords.Matches` — otherwise a password that an administrator set on a pre-created account would work (`.claude/rules/plugin.md`).
- **Security**: Never leave or move a user to Default without a password — the Default login method accepts a blank password for such an account.
- **Quality**: Test first, then break the code once and watch the test fail. A change that depends on Jellyfin or Emby behavior needs an e2e test. Warnings are errors (`CLAUDE.md`).
- **Versions**: Pin exact versions, and look up the current stable version before a bump (`CLAUDE.md`).
- **License**: GPL-3.0, because the Jellyfin packages are GPL-3.0-only (`LICENSE`).
- **Publishing**: Making the repository public cannot be undone for anyone who copied it — the history scan comes first, and the switch needs the maintainer's explicit approval at that time.

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Fix all four issue groups (bugs, release and tooling, test gaps, performance) in one milestone | Maintainer chose all groups for v1.0.0 | — Pending |
| Done means a tagged v1.0.0 and a public install | Maintainer's completion criteria | — Pending |
| Only an Emby-verified password logs in; no saved password means refuse until Emby accepts | Maintainer's rule; it is the core value | — Pending |
| A failed fingerprint write keeps the in-memory record; fix the docs and log, add a test | Emby did verify the password, so the rule holds, and a disk error does not block the migration. Confirm at the requirements review. | — Pending |
| Public repository with one stable manifest that lists every version | Usual setup for third-party Jellyfin plugins; gives administrators a URL that does not change | — Pending |
| Performance work starts with a load test | None of the three bottlenecks is measured | — Pending |

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
*Last updated: 2026-09-17 after initialization*
