# Requirements: Jellyfin Emby Auth

**Defined:** 2026-09-17
**Core Value:** A user moves from Emby to Jellyfin without a password reset, and no password that Emby did not verify ever opens an account.

## v1 Requirements

Requirements for the public v1.0.0 release. Each maps to one roadmap phase. Sources: `.planning/codebase/CONCERNS.md` (open issues 1-15), `.planning/research/SUMMARY.md`, and the maintainer's decisions on 2026-09-17.

### Password and Account Security

- [x] **AUTH-01**: While a user is on the Emby login method, Emby checks every login, and the saved Jellyfin hash plays no part in accepting it. After each login that Emby accepts, the saved hash is the hash of that password.
- [x] **AUTH-02**: The `JellyfinPasswordFirst` migration behavior ("Check the saved Jellyfin password first") no longer exists in the settings, the settings page, the docs, or the tests.
- [x] **AUTH-03**: When the plugin creates an account, it saves the Emby-verified hash and the Emby login method in the call right after `CreateUserAsync`, the same pattern as Jellyfin's own user creation, and `docs/how-it-works.md` describes the brief moment before that save.
- [x] **AUTH-04**: No failure while creating or saving an account (failed save, failed cleanup delete, or an unexpected exception type) returns HTTP 500 or leaves an enabled account on the Default login method without a password.
- [x] **AUTH-05**: The plugin ends the Emby session whenever it can read an access token in Emby's response, including a login response that has no user name. A success response whose body the plugin cannot read hides its token; the docs state that limit.
- [x] **AUTH-06**: An account on the Emby login method may have no saved password, and the plugin does not invent one. The saved hash plays no part in an Emby login method login (AUTH-01), so a missing password costs the user nothing while Emby checks the login. The settings page names every account on the Emby login method that has no saved password, warns that Jellyfin's Default login method opens such an account with a blank password, and recommends setting a password before the account moves. The plugin does not refuse the move and does not write a password the user cannot type.

### Verified Password Records

- [x] **FPRT-01**: When a fingerprint record cannot be written, it is not saved, and the log message says the user must log in through Emby again; the log message, the XML doc, and `docs/how-it-works.md` say so, and a unit test covers the write failure. *(Phase 3 met this against the JSON file store, where an unwritable file kept the record in memory until Jellyfin restarted. FPRT-04 replaced that store with SQLite, where each record commits durably as it is written and no record is ever held in memory, so Phase 4 restated the requirement to the guarantee the code now makes.)*
- [x] **FPRT-02**: When the fingerprint file cannot be read, a later record does not replace the file and erase the records in it. *(Describes the JSON file store, where one write rewrote every record. FPRT-04 removes the whole-file rewrite, so this failure mode stops being reachable rather than being handled.)*
- [x] **FPRT-03**: When the fingerprint records cannot be read, the Migration section of the settings page tells the administrator and points to the Jellyfin log.
- [x] **FPRT-04**: The fingerprint records live in a plugin-owned SQLite database in the plugin data folder, not a JSON file. A read takes no lock of the plugin's own. A record commits durably as it is written, so no accepted login depends on a later write to survive a restart. On first start the plugin imports an existing fingerprint JSON file once, and a unit test covers the import. The plugin references the same `Microsoft.Data.Sqlite` version the target Jellyfin image ships and binds to Jellyfin's copy rather than shipping its own.

### Settings Page

- [x] **UI-01**: When loading the plugin settings fails, the settings page shows a message, and a Save after that failed load does not write empty fields over the saved settings.
- [x] **UI-02**: When saving the plugin settings fails, the settings page shows a message.
- [x] **UI-03**: After **Run migration now**, the migration list shows that the migration runs and updates when the task finishes, instead of reloading once after a fixed 3 seconds.

### Migration Target

- [x] **MIGR-01**: The login method that the plugin moves a user to is a setting, not a fixed value. Two settings carry it: **Migration target** governs the move after a login and the migration task, and **Password-set target** governs the move after a password that an administrator sets in Jellyfin. Password-set target offers the same choices plus "Same as the migration target", which is its default. Both offer "Remain on Emby Login", which means no path moves anyone. The settings page offers only the login methods that Jellyfin reports as enabled, and the plugin refuses a value that is not one of them. Every path that moves a user off the Emby login method uses the setting that governs it.
- [x] **MIGR-02**: `EmbyAuthenticationProvider` stays `internal`. A public class enters the `GetExports<IAuthenticationProvider>()` scan that another plugin can run, which can send a password to Emby for a user that this plugin does not serve. A unit test fails if the class becomes public.

### Documentation

- [x] **DOCS-01**: `docs/how-it-works.md` points to the shutdown step in `docs/migration.md` that finds users who went back to the Emby login method.
- [ ] **DOCS-02**: `README.md` gives the manifest URL and the steps to install and update the plugin from the Jellyfin catalog.
- [ ] **DOCS-03**: `README.md` states the tested Jellyfin and Emby versions, and that `targetAbi` sets only the minimum Jellyfin version.
- [ ] **DOCS-04**: The Jellyfin version bump rule in `CLAUDE.md` names every pin, including the test project's `Jellyfin.Controller` reference and the `targetAbi` values in `tests/scripts/package.bats`.
- [x] **DOCS-05**: `docs/how-it-works.md` states that Jellyfin's Default login method accepts a blank password for an account that has no saved password, and names the three places this shapes the plugin: the account that a failed password save leaves behind is deleted (AUTH-04), the settings page warns before a move (AUTH-06), and the plugin is an interim tool whose end state is users on a login method that checks a password the user chose.

### Test Coverage

- [x] **TEST-01**: Unit tests cover `EmbyAuthenticationProvider`: account checks, the Emby login, account creation and update, and every failure path in AUTH-04.
- [x] **TEST-02**: Unit tests cover `MoveAfterLogin`, `LoginMethodMove`, `EmbyLoginMethodUsers`, and `EmbyMigrationTask` against an SQLite in-memory `JellyfinDbContext`.
- [x] **TEST-03**: Unit tests cover `EmbyAuthController`: the migration status, the run request, and the task state.
- [x] **TEST-04**: Automated tests run the settings page JavaScript: load, save, their error messages, the migration list, and **Run migration now**.
- [x] **TEST-05**: A test covers concurrent first logins through Jellyfin.
- [x] **TEST-06**: An e2e test covers invalid settings on a running server: logins on the Emby login method are refused, and the Jellyfin log names the problem at Error level.

### Performance

- [x] **PERF-01**: Concurrent logins that find an expired user list snapshot send one Emby user list request between them, not one each. A unit test drives concurrent readers against an expired snapshot and counts the outgoing requests.
- [x] **PERF-02**: Each known cost that this version does not remove is named in `docs/how-it-works.md` with the reason it stays: the Emby calls that run inside Jellyfin's login lock, and the Jellyfin account save that every accepted login performs. Neither has a fix the plugin can apply, so each is documented rather than measured.

### Release and Tooling

- [x] **REL-01**: The release workflow publishes a release only when the tagged commit has a passing `ci-success` check run.
- [x] **REL-02**: Each `zizmor --persona=pedantic` finding is fixed or suppressed with a written reason.
- [x] **REL-03**: gitleaks runs in `mise run lint`, so the pre-commit hook and CI check for secrets.
- [ ] **REL-04**: Each release has an entry in `CHANGELOG.md`, and the manifest entry for that version carries the same changelog.

### Public Install

- [ ] **PUB-01**: Before the repository goes public, gitleaks scans the full git history, and the Actions logs and artifacts, issue and PR text, and release notes are reviewed; every finding is removed or rotated.
- [ ] **PUB-02**: One `manifest.json` on GitHub Pages lists every released version with a plain hex MD5 checksum, and a separate workflow publishes it when it changes.
- [ ] **PUB-03**: The repository is public, after PUB-01 and REL-01 are complete and the maintainer approves the change at that time.
- [ ] **PUB-04**: A Jellyfin 12.1 administrator adds the manifest URL, installs the plugin from the catalog, and receives an update to a newer version.
- [ ] **PUB-05**: v1.0.0 is tagged and published through the release workflow.

## v2 Requirements

Deferred to a future release. Tracked but not in the current roadmap.

### Distribution

- **DIST-01**: The manifest URL is listed on jellyfin.org's community repositories or awesome-jellyfin, after v1.0.0 runs on a real install that is not the maintainer's (`.planning/research/FEATURES.md:171`).

## Out of Scope

| Feature | Reason |
|---------|--------|
| `JellyfinPasswordFirst` migration behavior | Removed by maintainer decision: while a user is on the Emby login method, only Emby decides a login |
| Account creation under a temporary name, then rename | Maintainer chose Jellyfin's own create-then-save pattern (`UserController.cs:517-533`, tag `v12.1`) |
| Re-running lint and e2e inside `release.yml` | Maintainer chose to check the CI result of the tagged commit |
| Manifest as a file on a branch, or a latest-only manifest | Maintainer chose one multi-version manifest on GitHub Pages |
| Signed or attested releases | Jellyfin's install path checks only the MD5 checksum, so nothing can check a signature (`FEATURES.md:92`) |
| Automated version bump and changelog tooling | Out of proportion for a single maintainer's release cadence (`FEATURES.md:93`) |
| Submission to the official Jellyfin plugin repository | The chosen route is a self-hosted manifest |
| Moving the maintainer's own server off Emby | Not a completion criterion for this milestone |
| Logins with an Emby Connect email address | Documented limit (`docs/how-it-works.md:36`); no change requested |

## Traceability

Which phases cover which requirements. Updated during roadmap creation.

| Requirement | Phase | Status |
|-------------|-------|--------|
| AUTH-01 | Phase 1 | Complete |
| AUTH-02 | Phase 1 | Complete |
| AUTH-03 | Phase 1 | Complete |
| AUTH-04 | Phase 1 | Complete |
| AUTH-05 | Phase 4 | Complete |
| AUTH-06 | Phase 3 | Complete |
| FPRT-01 | Phase 3 | Complete |
| FPRT-02 | Phase 2 | Complete |
| FPRT-03 | Phase 3 | Complete |
| FPRT-04 | Phase 4 | Complete |
| UI-01 | Phase 2 | Complete |
| UI-02 | Phase 2 | Complete |
| UI-03 | Phase 3 | Complete |
| MIGR-01 | Phase 3 | Complete |
| MIGR-02 | Phase 3 | Complete |
| DOCS-01 | Phase 3 | Complete |
| DOCS-02 | Phase 6 | Pending |
| DOCS-03 | Phase 5 | Pending |
| DOCS-04 | Phase 6 | Pending |
| DOCS-05 | Phase 3 | Complete |
| TEST-01 | Phase 1 | Complete |
| TEST-02 | Phase 3 | Complete |
| TEST-03 | Phase 3 | Complete |
| TEST-04 | Phase 2 | Complete |
| TEST-05 | Phase 4 | Complete |
| TEST-06 | Phase 4 | Complete |
| PERF-01 | Phase 4 | Complete |
| PERF-02 | Phase 4 | Complete |
| REL-01 | Phase 5 | Complete |
| REL-02 | Phase 5 | Complete |
| REL-03 | Phase 5 | Complete |
| REL-04 | Phase 6 | Pending |
| PUB-01 | Phase 5 | Pending |
| PUB-02 | Phase 6 | Pending |
| PUB-03 | Phase 5 | Pending |
| PUB-04 | Phase 6 | Pending |
| PUB-05 | Phase 6 | Pending |

**Coverage:**

- v1 requirements: 37 total
- Mapped to phases: 37
- Unmapped: 0

---
*Requirements defined: 2026-09-17*
*Last updated: 2026-09-20 — FPRT-04 replaces the fingerprint JSON file with a plugin-owned SQLite database, and PERF-01 and PERF-02 drop the load test. The two costs the load test would have measured inside the plugin are now fixed outright: the user list stampede gains a single-flight guard (PERF-01), and the fingerprint write stops holding a lock across disk I/O (FPRT-04). The two it would have measured outside the plugin — the Emby calls inside Jellyfin's login lock, and Jellyfin's own per-login account save — have no fix the plugin can apply, so PERF-02 documents them instead. Building a k6 and toxiproxy rig to measure costs whose fixes were already decided was more work than the fixes (Phase 4 planning)*
