# Roadmap: Jellyfin Emby Auth

## Overview

This roadmap takes the working plugin to a public first release. The security fixes come first: account creation that leaves no open account, and logins that only Emby decides. Next, the fingerprint file and the settings page stop losing data when a read or a request fails. Then the migration status, the Emby traffic, and the load test get their fixes and tests. The last two phases make the repository safe to publish, then publish one manifest and version 1.0.0.0 so that any Jellyfin 12.1 administrator can install and update the plugin from the catalog.

## Phases

**Phase Numbering:**

- Integer phases (1, 2, 3): Planned work
- Decimal phases (2.1, 2.2): Urgent insertions (marked with INSERTED)

Decimal phases appear between their surrounding integers in numeric order.

- [ ] **Phase 1: Account Creation and Login Security** - Account creation leaves no open account, and only Emby decides a login on the Emby login method
- [ ] **Phase 2: Safe Failures for the Fingerprint File and Settings** - An unreadable fingerprint file keeps its records, and the settings page reports load and save failures
- [ ] **Phase 3: Migration Status** - The Migration section shows the real task state and fingerprint file problems, and the migration code has unit tests
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

**Plans**: 2/4 plans executed

Plans:
**Wave 1**

- [x] 01-01-PLAN.md — Unit-test seam for `EmbyAuthenticationProvider`, and every AUTH-04 failure path closed behind it

**Wave 2** *(blocked on Wave 1 completion)*

- [x] 01-02-PLAN.md — Full TEST-01 coverage, the AUTH-03 save-directly-after-create ordering, and the account-creation window documented

**Wave 3** *(blocked on Wave 2 completion)*

- [x] 01-03-PLAN.md — The saved-password migration behavior removed from the code, the settings page, and the tests, with both e2e tests rewritten

**Wave 4** *(blocked on Wave 3 completion)*

- [ ] 01-04-PLAN.md — The same removal in the documentation, the `CHANGELOG.md` settings-loss notice, and a refreshed settings-page screenshot

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

**Plans**: TBD
**UI hint**: yes

### Phase 3: Migration Status

**Goal**: The Migration section of the settings page shows what the migration task and the fingerprint file actually do, and the migration code has unit tests against an SQLite in-memory `JellyfinDbContext`. The database test seam and the `ITaskManager` fake come before the fixes.
**Depends on**: Phase 2
**Requirements**: FPRT-01, FPRT-03, UI-03, TEST-02, TEST-03, DOCS-01
**Success Criteria** (what must be TRUE):

  1. When Jellyfin cannot read the fingerprint file, the Migration section tells the administrator and points to the Jellyfin log.
  2. After **Run migration now**, the migration list shows that the migration runs and updates when the task finishes, also for a run that takes longer than 3 seconds. One change to the `GET /EmbyAuth/Migration` response carries both the read failure and the task state.
  3. When Jellyfin cannot write the fingerprint file, the log message, the XML doc, and `docs/how-it-works.md` say that the record stays in memory until Jellyfin restarts, and a unit test covers the write failure.
  4. `docs/how-it-works.md` points to the shutdown step in `docs/migration.md` that finds users who went back to the Emby login method.
  5. `dotnet test` runs unit tests for `MoveToDefaultLoginMethod`, `DefaultLoginMethod`, `EmbyLoginMethodUsers`, and `MoveEmbyUsersToDefaultTask` against an SQLite in-memory `JellyfinDbContext`, and for `EmbyAuthController`: the migration status, the run request, and the task state.

**Plans**: TBD
**UI hint**: yes

### Phase 4: Emby Traffic Under Load and Failure

**Goal**: The plugin behaves predictably toward Emby when Emby is slow, when logins arrive at the same time, and when the settings are invalid, and each known bottleneck has measured numbers. The load test measures the final account creation code from Phase 1 and the final fingerprint code from Phases 2 and 3.
**Depends on**: Phase 3
**Requirements**: AUTH-05, TEST-05, TEST-06, PERF-01, PERF-02
**Success Criteria** (what must be TRUE):

  1. Whenever Emby returns an access token, the plugin sends `POST /Sessions/Logout`, also for a login response that has no user name. A unit test shows the sign-out request for that response.
  2. A test sends concurrent first logins through Jellyfin. Each Emby user gets exactly one account, and no login returns HTTP 500.
  3. An e2e test saves invalid settings on a running server. Logins on the Emby login method are then refused, and the Jellyfin log names the problem at Error level.
  4. A mise task runs the load test against the Docker Compose stack with a separate pool of test accounts, and reports numbers for logins with a slow Emby server, concurrent first logins, user list cache expiry, and fingerprint file writes.
  5. Each of the three bottlenecks (Emby calls inside the login lock, duplicate user list requests at cache expiry, fingerprint writes under the lock) is fixed, or the docs accept it with the numbers from criterion 4.

**Plans**: TBD

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
| 1. Account Creation and Login Security | 2/4 | In Progress|  |
| 2. Safe Failures for the Fingerprint File and Settings | 0/TBD | Not started | - |
| 3. Migration Status | 0/TBD | Not started | - |
| 4. Emby Traffic Under Load and Failure | 0/TBD | Not started | - |
| 5. Public Repository | 0/TBD | Not started | - |
| 6. Catalog Install and First Public Release | 0/TBD | Not started | - |
