# Roadmap: Jellyfin Emby Auth

## Overview

This roadmap takes the working plugin to a public first release. The security fixes come first: account creation that leaves no open account, and logins that only Emby decides. Next, the fingerprint file and the settings page stop losing data when a read or a request fails. Then the migration status and the migration target, the Emby traffic, and the load test get their fixes and tests. The last two phases make the repository safe to publish, then publish one manifest and version 1.0.0.0 so that any Jellyfin 12.1 administrator can install and update the plugin from the catalog.

## Phases

**Phase Numbering:**

- Integer phases (1, 2, 3): Planned work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [x] **Phase 1: Account Creation and Login Security** - Account creation leaves no open account, and only Emby decides a login on the Emby login method (completed 2026-09-17)
- [x] **Phase 2: Safe Failures for the Fingerprint File and Settings** - An unreadable fingerprint file keeps its records, and the settings page reports load and save failures (completed 2026-09-19)
- [x] **Phase 3: Migration Status and Target** - The Migration section shows the real task state and fingerprint file problems, the login method that users move to is a setting, and the migration code has unit tests (completed 2026-09-20)
- [ ] **Phase 4: Emby Traffic Under Load and Failure** - Emby sessions always end, concurrent and misconfigured logins have tests, and the load test measures each bottleneck
- [ ] **Phase 5: Public Repository** - Releases require a passing CI run, the history has no secrets, and the maintainer approves the switch to public
- [ ] **Phase 6: Catalog Install and First Public Release** - An administrator installs and updates the plugin from the manifest URL, and version 1.0.0.0 is published

## Phase Details

### Phase 1: Account Creation and Login Security

**Goal**: No password that Emby did not verify opens an account. Every failure during account creation refuses the login and leaves no open account, and while a user is on the Emby login method, only Emby decides the login. Work order: the `IServiceProvider`/`IUserManager` fake and the failing tests come first, then AUTH-04, AUTH-03, AUTH-01, and AUTH-02.
**Depends on**: Nothing (first phase)
**Requirements**: AUTH-04, AUTH-03, AUTH-01, AUTH-02, TEST-01
**Success Criteria** (what must be TRUE):

  1. Unit tests make the save after `CreateUserAsync` fail, make the cleanup delete fail as well, and throw an unexpected exception type. In each case the login is refused without HTTP 500, and the plugin attempts to delete the new account. When Jellyfin can neither save nor delete, the account stays on the Default login method without a password and the plugin logs the account name at Error.
  2. A new account gets the Emby-verified hash and the Emby login method in the `UpdateUserAsync` call directly after `CreateUserAsync`, and `docs/how-it-works.md` describes the brief moment before that save.
  3. An e2e test shows that a user on the Emby login method cannot log in with the saved Jellyfin password when Emby refuses that password, and that after each login Emby accepts, the saved hash is the hash of that password.
  4. A search of `src/`, `tests/`, `e2e/`, `docs/`, and `README.md` finds no `JellyfinPasswordFirst` and no "Check the saved Jellyfin password first" option.
  5. `dotnet test` runs unit tests for `EmbyAuthenticationProvider` that cover the account checks, the Emby login, account creation and update, and every failure path in criterion 1. Each fix has a test that failed before the fix.

**Plans**: 4/4 plans executed

Plans:
**Wave 1**

- [x] 01-01-PLAN.md — Unit-test seam for `EmbyAuthenticationProvider`, and every AUTH-04 failure path closed behind it

**Wave 2** *(blocked on Wave 1 completion)*

- [x] 01-02-PLAN.md — Full TEST-01 coverage, the AUTH-03 save-directly-after-create ordering, and the account-creation window documented

**Wave 3** *(blocked on Wave 2 completion)*

- [x] 01-03-PLAN.md — The saved-password migration behavior removed from the code, the settings page, and the tests, with both e2e tests rewritten

**Wave 4** *(blocked on Wave 3 completion)*

- [x] 01-04-PLAN.md — The same removal in the documentation, the `CHANGELOG.md` settings-loss notice, and a refreshed settings-page screenshot

**UI hint**: no

### Phase 2: Safe Failures for the Fingerprint File and Settings

**Goal**: A failed read or a failed request does not destroy saved data without a message. An unreadable fingerprint file keeps its records, and the settings page tells the administrator when a load or a save fails. The JavaScript test harness and the failing load and save tests come before the page changes.
**Depends on**: Phase 1
**Requirements**: FPRT-02, UI-01, UI-02, TEST-04
**Success Criteria** (what must be TRUE):

  1. When Jellyfin cannot read the fingerprint file, a later Emby login does not replace the file, and every record that was in the file is still in it after that login.
  2. When loading the plugin settings fails, the settings page shows a message, and a Save after that failed load does not write empty fields over the saved settings.
  3. When saving the plugin settings fails, the settings page shows a message.
  4. Automated tests run the settings page JavaScript for load, save, their error messages, the migration list, and **Run migration now**. CI runs these tests on each pull request, and a test fails when a load or save error handler is removed.

**Plans**: 3/3 plans executed

Plans:
**Wave 1**

- [x] 02-01-PLAN.md — The JavaScript test harness, end to end, and the UI-01 load-failure message with the Save button turned off

**Wave 2** *(blocked on Wave 1 completion)*

- [x] 02-02-PLAN.md — The UI-02 save-failure message, the rest of the TEST-04 coverage, and the documentation of both
- [x] 02-03-PLAN.md — FPRT-02: an unreadable fingerprint file keeps its records and is read again on the next login

**UI hint**: yes

### Phase 3: Migration Status and Target

**Goal**: The Migration section of the settings page shows what the migration task and the fingerprint file actually do, the login method that users move to becomes a setting instead of a fixed value, and the migration code has unit tests against an SQLite in-memory `JellyfinDbContext`. The database test seam and the `ITaskManager` fake come before the fixes.
**Depends on**: Phase 2
**Requirements**: FPRT-01, FPRT-03, UI-03, MIGR-01, MIGR-02, AUTH-06, TEST-02, TEST-03, DOCS-01, DOCS-05
**Success Criteria** (what must be TRUE):

  1. When Jellyfin cannot read the fingerprint file, the Migration section tells the administrator and points to the Jellyfin log.
  2. After **Run migration now**, the migration list shows that the migration runs and updates when the task finishes, also for a run that takes longer than 3 seconds. One change to the `GET /EmbyAuth/Migration` response carries both the read failure and the task state.
  3. When Jellyfin cannot write the fingerprint file, the log message, the XML doc, and `docs/how-it-works.md` say that the record stays in memory until Jellyfin restarts, and a unit test covers the write failure.
  4. `docs/how-it-works.md` points to the shutdown step in `docs/migration.md` that finds users who went back to the Emby login method.
  5. `dotnet test` runs unit tests for the move classes, `EmbyLoginMethodUsers`, and `EmbyMigrationTask` against an SQLite in-memory `JellyfinDbContext`, and for `EmbyAuthController`: the migration status, the run request, and the task state.
  6. An administrator chooses the login method that users move to, through two settings: **Migration target** for the move after a login and for the migration task, and **Password-set target** for the move after a password that an administrator sets in Jellyfin. Password-set target also offers "Same as the migration target", which is its default. Both offer "Remain on Emby Login", and while a setting holds that value no path moves anyone through it. The settings page offers only the login methods that Jellyfin reports as enabled, and a value that is not one of them is refused with a message. Every move uses the setting that governs it. The class names, the task name, and the task key no longer say "Default".
  7. An e2e test installs JellyfinSecurity v2.6.1 (the `-jf12` build, pinned and checksum-verified) beside this plugin in the shared e2e stack, moves a user to it, and that user then logs in with the password that Emby verified. The test stops at the hand-over.
  8. A unit test fails if `EmbyAuthenticationProvider` becomes public, and a comment on the class says why it stays internal.
  9. The migration list names every account on the Emby login method that has no saved password, and the Migration section warns that Jellyfin's Default login method, or any login method that hands its password check to Default, opens such an account with a blank password, and recommends setting one before the account moves. The plugin neither refuses the move nor writes a password the user cannot type. An e2e test shows that a user with no saved password still logs in through Emby, and that the migration list names that account.
  10. `docs/how-it-works.md` states that Jellyfin's Default login method accepts a blank password for an account with no saved password, and connects that behavior to the deleted account after a failed password save, to the settings page warning, and to the plugin's role as an interim migration tool.

**Plans**: 7/7 plans executed

Plans:
**Wave 1**

- [x] 03-01-PLAN.md — The unit-test seam for the database and the task manager, then one end-to-end path: an unreadable fingerprint file reaches the Migration section

**Wave 2** *(blocked on Wave 1 completion)*

- [x] 03-02-PLAN.md — FPRT-01's write-failure correction in the log, the XML doc, and `docs/how-it-works.md`; the MIGR-02 visibility guard; DOCS-05 and the DOCS-01 verification
- [x] 03-03-PLAN.md — The migration response completed: one state per account, the task state, and the login methods an administrator may pick

**Wave 3** *(blocked on Wave 2 completion)*

- [x] 03-04-PLAN.md — The move target becomes a setting: the two settings, the three renames, the new task key, and every move path

**Wave 4** *(blocked on Wave 3 completion)*

- [x] 03-05-PLAN.md — The settings page: the polling loop, the target dropdown above **Run migration now**, and the no-saved-password warning
- [x] 03-06-PLAN.md — The plugin refuses a target Jellyfin does not report as enabled, and a password set in Jellyfin follows the password-set target

**Wave 5** *(blocked on Wave 4 completion)*

- [x] 03-07-PLAN.md — JellyfinSecurity in the end-to-end stack, the hand-over test, and the documentation

**UI hint**: yes

### Phase 4: The Fingerprint Store, and Emby Traffic Under Failure

**Goal**: The fingerprint records move from a JSON file to a plugin-owned SQLite database, so a read takes no lock and a record survives a restart as soon as it is written. On that store, the plugin then behaves predictably toward Emby when logins arrive at the same time and when the settings are invalid, and the one remaining contention point inside the plugin — the user list refresh — sends one request where it used to send one per login.
**Depends on**: Phase 3
**Requirements**: FPRT-04, AUTH-05, TEST-05, TEST-06, PERF-01, PERF-02
**Success Criteria** (what must be TRUE):

  1. The fingerprint records live in a SQLite database in the plugin data folder. `Matches` and `RecordsAvailable` take no lock of the plugin's own, and a record commits durably as it is written. The plugin references the `Microsoft.Data.Sqlite` version the pinned Jellyfin image ships and binds to Jellyfin's copy instead of shipping its own.
  2. On first start with an existing fingerprint JSON file, the plugin imports every record once, and a later start does not import again. A unit test covers the import, and an e2e test shows a user who was ready to move before the upgrade still ready after it.
  3. Whenever the plugin can read an access token in Emby's response, it sends `POST /Sessions/Logout`, also for a login response that has no user name. A unit test shows the sign-out request for that response. A success response whose body the plugin cannot read hides its token, so one Emby session can stay open; `docs/how-it-works.md` states that limit.
  4. A test sends concurrent first logins through Jellyfin. Each Emby user gets exactly one account, and no login returns HTTP 500.
  5. An e2e test saves invalid settings on a running server. Logins on the Emby login method are then refused, and the Jellyfin log names the problem at Error level.
  6. Concurrent logins that find an expired user list snapshot send one Emby user list request between them. A unit test drives concurrent readers against an expired snapshot and counts the outgoing requests.
  7. `docs/how-it-works.md` names the two costs this version does not remove — the Emby calls that run inside Jellyfin's login lock, and Jellyfin's own account save on every accepted login — and says why neither has a fix the plugin can apply.

**Plans**: TBD

> **Scope change, 2026-09-20 (planning).** This phase previously promised a k6 and toxiproxy load test with a written pass-or-fail threshold per item (old criteria 4 and 5, PERF-01 and PERF-02). It was cut. Of the four costs it would have measured, the two inside the plugin now get fixed outright — the user list stampede by a single-flight guard, and the fingerprint write by leaving the lock entirely (criteria 1 and 6) — and building the rig to justify those fixes was more work than the fixes. The other two lie outside the plugin: the Emby calls inside Jellyfin's login lock have no fix while the plugin must call Emby, and the per-login account save is Jellyfin's own `UpdateUserAsync`. Both are documented instead (criterion 7). The store swap leads the phase so the concurrency and settings tests are written once, against SQLite, rather than written against the file store and then rewritten.

### Phase 5: Public Repository

**Goal**: The repository is safe to make public, and then it is public. Releases require a passing CI run, the workflows and the history have no open findings, and the maintainer approves the switch at the time of the change.
**Depends on**: Phase 4
**Requirements**: REL-01, REL-02, REL-03, DOCS-03, PUB-01, PUB-03
**Success Criteria** (what must be TRUE):

  1. A `v*` tag on a commit that has no passing `ci-success` check run stops the release workflow before it publishes a release. The first release in Phase 6 shows the passing path.
  2. Each `zizmor --persona=pedantic` finding is fixed or suppressed with a written reason, and `mise run lint` runs gitleaks, so the pre-commit hook and CI check for secrets.
  3. `README.md` states the tested Jellyfin and Emby versions, and that `targetAbi` sets only the minimum Jellyfin version.
  4. Before the visibility change, gitleaks scans the full git history, and the Actions logs and artifacts, the issue and PR text, and the release notes are reviewed. Every finding is removed or rotated.
  5. The repository is public. The maintainer gave explicit approval at the time of the change, after criteria 1 and 4 were complete.

**Plans**: TBD

### Phase 6: Catalog Install and First Public Release

**Goal**: Any Jellyfin 12.1 administrator installs the plugin from one stable manifest URL and receives updates, and version 1.0.0.0 is published through the release workflow. The second catalog version that the update check needs comes from a rehearsal release, version 0.9.0.0, published before version 1.0.0.0.
**Depends on**: Phase 5
**Requirements**: PUB-02, REL-04, DOCS-02, DOCS-04, PUB-04, PUB-05
**Success Criteria** (what must be TRUE):

  1. One `manifest.json` on GitHub Pages lists every released version with a plain hex MD5 checksum, and a separate workflow publishes it when it changes.
  2. Each release has an entry in `CHANGELOG.md`, and the manifest entry for that version carries the same changelog.
  3. `README.md` gives the manifest URL and the steps to install and update the plugin from the Jellyfin catalog. The Jellyfin version bump rule in `CLAUDE.md` names every pin, including the test project's `Jellyfin.Controller` reference and the `targetAbi` values in `tests/scripts/package.bats`.
  4. Version 0.9.0.0 (tag `v0.9.0.0`) goes through the release workflow as the rehearsal release. On a clean Jellyfin 12.1 server, an administrator adds the manifest URL and installs 0.9.0.0 from the catalog.
  5. After the Pages manifest exists, version 1.0.0.0 (tag `v1.0.0.0`, the `v<version>` form that `scripts/package.sh check-tag` requires) goes through the release workflow. The manifest then lists both versions, and the same server receives the update to 1.0.0.0 from the catalog.

**Plans**: TBD

## Progress

**Execution Order:**
Phases execute in numeric order: 1 → 2 → 3 → 4 → 5 → 6

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Account Creation and Login Security | 4/4 | Complete    | 2026-09-17 |
| 2. Safe Failures for the Fingerprint File and Settings | 3/3 | Complete    | 2026-09-19 |
| 3. Migration Status and Target | 7/7 | Complete    | 2026-09-20 |
| 4. Emby Traffic Under Load and Failure | 0/TBD | Not started | - |
| 5. Public Repository | 0/TBD | Not started | - |
| 6. Catalog Install and First Public Release | 0/TBD | Not started | - |
