---
phase: 06-catalog-install-and-first-public-release
plan: 01
subsystem: infra
tags: [jellyfin-plugin-repository, manifest, jq, bats, docker-compose, e2e]

# Dependency graph
requires: []
provides:
  - "scripts/package.sh reads a version's dated CHANGELOG.md section into meta.json and the manifest, and refuses to build without it"
  - "scripts/manifest.sh merge: a checksum-verified, numerically-sorted, multi-version PackageInfo[] document built from local package.sh-shaped release directories"
  - "e2e/85-catalog-install.bats: a hermetic proof that a clean Jellyfin 12.1 server installs and updates from that document"
affects: [06-02, 06-03, 06-04]

# Actuals (#2632)
actuals:
  tokens: 9411
  tasks: 3
  commits: 3

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "awk-based verbatim CHANGELOG.md section extraction, with a missing/duplicated/empty refusal gate ahead of dotnet publish"
    - "jq -s group_by(.guid) | sort_by numeric-component version merge, with a post-merge duplicate-version scan rather than jq's own error()"
    - "compose-profile-gated ephemeral e2e services (catalog-manifest, catalog-jellyfin), with per-@test re-authentication since bats runs each test in its own process"

key-files:
  created:
    - scripts/manifest.sh
    - tests/scripts/manifest.bats
    - e2e/85-catalog-install.bats
  modified:
    - scripts/package.sh
    - tests/scripts/package.bats
    - e2e/compose.yaml
    - e2e/helpers.bash
    - .claude/rules/e2e.md

key-decisions:
  - "Task 1 and Task 2 each combined their RED and GREEN phases into one commit, because prek's pre-commit `test` hook runs the full suite (mise run test) on any scripts/ or tests/scripts/ change and would block a commit that leaves a test failing; --no-verify is forbidden. See TDD Gate Compliance below."
  - "GET /Plugins's own Version field reflects the compiled DLL's AssemblyVersion (from Directory.Build.props), not the catalog meta.json version reached via PACKAGE_VERSION — verified live. Task 3's tests check that endpoint's Status and guid (loaded/Active), and read the true installed version from the container's on-disk meta.json instead."
  - "GET /Packages and GET /Plugins both report the plugin guid with its dashes stripped; meta.json on disk and PLUGIN_ID (helpers.bash) keep them. A plugin_id_nodash() helper normalizes for comparison."

requirements-completed: [REL-04, PUB-02, PUB-04]

coverage:
  - id: D1
    description: "scripts/package.sh build reads the dated CHANGELOG.md section for the version it packages, writes it verbatim into meta.json and the manifest, and refuses before dotnet publish when the section is missing, empty, or duplicated"
    requirement: "REL-04"
    verification:
      - kind: unit
        ref: "tests/scripts/package.bats (16 tests)"
        status: pass
    human_judgment: false
  - id: D2
    description: "scripts/manifest.sh merge produces one checksum-verified, numerically-sorted PackageInfo[] document from local release directories, refusing on a checksum mismatch, a duplicated version, or a malformed directory"
    requirement: "PUB-02"
    verification:
      - kind: unit
        ref: "tests/scripts/manifest.bats (15 tests)"
        status: pass
    human_judgment: false
  - id: D3
    description: "A clean Jellyfin 12.1 server with no plugin mounted adds the merged manifest as a repository, lists both versions, installs the lower one, tolerates a repeat install, and updates to the higher one Jellyfin itself resolves as newest"
    requirement: "PUB-04"
    verification:
      - kind: e2e
        ref: "e2e/85-catalog-install.bats (8 tests)"
        status: pass
    human_judgment: false

duration: 44min (measured from the first task commit to the last; does not include initial context-reading or the 1Password signing pause described below)
completed: 2026-09-22
status: complete
---

# Phase 6 Plan 1: Catalog Install and Update, Hermetically Proven Summary

**A version's `CHANGELOG.md` section reaches the zip it ships in via a new `changelog_entry()` in `scripts/package.sh`; a new `scripts/manifest.sh merge` folds real per-release directories into one checksum-verified multi-version manifest; and a clean, unmounted Jellyfin 12.1 server installs the lower of two genuinely built versions from that manifest over HTTP and then updates to the higher one, all proven by a new hermetic `e2e/85-catalog-install.bats` alongside the existing suite.**

## Performance

- **Duration:** 44 min (task-commit span; see note above)
- **Started:** 2026-09-21T21:38:39-07:00 (first task commit)
- **Completed:** 2026-09-21T22:22:59-07:00 (last task commit)
- **Tasks:** 3
- **Files modified:** 8 (3 created, 5 modified)

## Accomplishments

- `scripts/package.sh` gained `changelog_entry()`, `PACKAGE_VERSION`, and `CHANGELOG_PATH`: a version's dated `CHANGELOG.md` section reaches `meta.json` and the manifest verbatim (byte-identical, proven with a body carrying a quote and a backslash), and `build` refuses before `dotnet publish` when that section is missing, empty, or duplicated.
- `scripts/manifest.sh merge DIR [DIR...]` recomputes each directory's zip MD5 against its recorded `checksum` before trusting it, groups by `guid`, sorts each group's versions ascending by numeric component (not lexicographically), and refuses on a checksum mismatch, a duplicated version, or a malformed directory.
- `e2e/85-catalog-install.bats` builds two real versions, merges them with the real `manifest.sh`, serves the result from a profiled `catalog-manifest` nginx service, and drives a profiled `catalog-jellyfin` service (no plugin bind mount) through `POST /Repositories`, `GET /Packages`, install, idempotent reinstall, a restart, and an unversioned update — 8 tests, all green under `mise run e2e`.

## Task Commits

Each task was committed atomically (Task 1 and Task 2 as single combined commits — see TDD Gate Compliance):

1. **Task 1: One version's changelog reaches the zip it ships in, and the build refuses without it** - `4793ead` (feat)
2. **Task 2: One multi-version manifest merged from real per-release directories, with every checksum recomputed** - `3673d63` (feat)
3. **Task 3: A clean Jellyfin server installs from the merged manifest and then updates** - `27be3e5` (feat)

**Plan metadata:** _pending — recorded after this SUMMARY is committed._

## Files Created/Modified

- `scripts/package.sh` - `changelog_entry()`, `PACKAGE_VERSION`/`CHANGELOG_PATH` overrides, refusal wired into `build()` before `dotnet publish`
- `tests/scripts/package.bats` - 8 new tests (16 total): verbatim carry-through, three distinct refusals, position-independent extraction, both `PACKAGE_VERSION` states
- `scripts/manifest.sh` - new; `merge` action: per-directory validation, checksum re-verification, numeric-sort merge, duplicate-version refusal
- `tests/scripts/manifest.bats` - new; 15 tests over local fixture directories, no `gh` and no network
- `e2e/compose.yaml` - `catalog-manifest` and `catalog-jellyfin` services, both behind the new `catalog` profile; `catalog-jellyfin` has no `volumes:`
- `e2e/helpers.bash` - `CATALOG_JELLYFIN_PORT`, `CATALOG_MANIFEST_PORT`, `CATALOG_JELLYFIN`, `CATALOG_ARTIFACT_DIR` exports
- `e2e/85-catalog-install.bats` - new; 8 tests proving the whole catalog install/update path
- `.claude/rules/e2e.md` - the port-variable sentence now names all four ports; one new Layout line for `85-catalog-install.bats`'s self-managed services

## Decisions Made

- **TDD Gate Compliance:** see the dedicated section below.
- **`GET /Plugins`'s `Version` field is not a reliable signal for which catalog version installed** — it reflects the compiled DLL's `AssemblyVersion` (from `Directory.Build.props`, unaffected by `PACKAGE_VERSION`), confirmed by directly inspecting a live response. Tests 7 and 8 in `e2e/85-catalog-install.bats` instead check that endpoint's `Status` (`"Active"`) and `Id` (guid, dash-stripped) to confirm the plugin loaded, and read the actual installed version from the container's on-disk `meta.json` via a new `active_installed_version()` helper, which filters by `meta.json`'s own `status: "Active"` field (an update leaves the superseded version's folder and `meta.json` on disk with `status: "Superseded"` rather than deleting it).
- **Plugin GUID casing:** `GET /Packages`'s `.guid` and `GET /Plugins`'s `.Id` both report the plugin GUID with its dashes stripped (`e973e09ae8b440c19be28e51342de1f9`); `PLUGIN_ID` (helpers.bash) and on-disk `meta.json` both keep them (`e973e09a-e8b4-40c1-9be2-8e51342de1f9`). A `plugin_id_nodash()` helper normalizes for the two API comparisons.
- **Per-@test re-authentication:** bats runs `setup_file` and each `@test` in separate processes, so a token exported inside one test's body (as the restart helper does) is not visible to a sibling test. `setup()` now calls `catalog_login` before every test rather than relying on inherited state, which is what fixed an initial 401 cascade and a later `wait_until` timeout in this session.

### Required output per the plan's `<output>` section

- **Property-name casing observed, live, this session:**
  - `GET /Repositories`: PascalCase (`Name`, `Url`, `Enabled`).
  - `GET /Packages`: lowerCamelCase (`name`, `description`, `overview`, `owner`, `category`, `guid`, `versions[].version`, `.changelog`, `.targetAbi`, `.sourceUrl`, `.checksum`, `.timestamp`) — except `versions[].VersionNumber`, `.repositoryName`, and `.repositoryUrl`, three fields Jellyfin computes/fills in itself, in a mixed casing that does not follow either convention consistently.
  - `GET /Plugins`: PascalCase (`Name`, `Version`, `ConfigurationFileName`, `Description`, `Id`, `CanUninstall`, `HasImage`, `Status`).
- **Plugin folder name Jellyfin created:** `/config/plugins/Emby Auth_<version>/` — literally the package name (with its space) plus an underscore plus the version, e.g. `Emby Auth_0.0.1.0`. Confirmed via `docker compose exec`; this is why `installed_versions()` enumerates `/config/plugins/*/meta.json` rather than guessing the folder name.
- **Timing cost of the two extra `dotnet publish` runs added to `mise run test` and `mise run e2e`:** measured directly this session, warm (same job, after an initial `dotnet test`/`dotnet publish` had already primed the incremental build cache) — each additional `dotnet publish` invocation takes about 1 second. Full-suite times measured in this session: `mise run test` 33s total, `mise run e2e` 73s total. Neither run was timed before this plan for a strict before/after delta; the ~1s-per-publish figure is the number that matters, since a cold CI job pays the full compile cost once and every subsequent `dotnet publish` in that same job reuses that cache.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] `GET /Plugins`'s `Version` field cannot distinguish the two catalog versions**

- **Found during:** Task 3, first live run of `e2e/85-catalog-install.bats`
- **Issue:** The plan's task text specified asserting `GET /Plugins` reports the plugin guid "at version 0.0.1.0" / "at version 0.0.2.0" after each restart. Live testing showed `GET /Plugins`'s `Version` field always reports `1.0.0.0` (the compiled DLL's `AssemblyVersion` from `Directory.Build.props`) regardless of which `PACKAGE_VERSION`-labeled zip was installed — this is exactly the accepted cost D-17 already named ("the override could in principle mislabel a real release zip, because the DLL's own AssemblyVersion still comes from Directory.Build.props"), just not fully reconciled against that one literal assertion when the task was written.
- **Fix:** Tests 7 and 8 now assert `GET /Plugins`'s `Status` (`"Active"`) and `Id` (guid, dash-normalized) to prove the plugin loaded, and a new `active_installed_version()` helper reads the true installed version from the container's on-disk `meta.json` (filtering by that file's own `status: "Active"` field, since a superseded version's folder and `meta.json` persist on disk with `status: "Superseded"`).
- **Files modified:** e2e/85-catalog-install.bats
- **Verification:** `bats e2e/85-catalog-install.bats` and `mise run e2e`, both green (8/8 and 49/49).
- **Committed in:** `27be3e5` (Task 3 commit)

**2. [Rule 1 - Bug] `GET /Packages`'s and `GET /Plugins`'s guid is dash-stripped**

- **Found during:** Task 3, same live run
- **Issue:** The plan did not anticipate that Jellyfin's `PackageInfo`/plugin-list serialization strips dashes from the plugin GUID, while `PLUGIN_ID` (exported by `helpers.bash`) and on-disk `meta.json` both keep them. A literal `select(.guid == $PLUGIN_ID)` filter matched nothing.
- **Fix:** Added `plugin_id_nodash()`, used in the two API-response comparisons.
- **Files modified:** e2e/85-catalog-install.bats
- **Verification:** Test 4 ("the catalog lists both versions") and the guid checks in Tests 7/8 pass.
- **Committed in:** `27be3e5` (Task 3 commit)

**3. [Rule 1 - Bug] `CATALOG_TOKEN` did not survive across bats' per-test process boundary**

- **Found during:** Task 3, live debugging
- **Issue:** bats runs `setup_file` and each `@test` as separate processes. The initial design set `CATALOG_TOKEN` once in `setup_file` and again inside a restart helper called from within a test body, but never re-authenticated at the start of each `@test`; every test after the first restart got a 401 or, one test later, a `wait_until` timeout because the install request silently failed authentication.
- **Fix:** `setup()` now calls `catalog_login` (which re-authenticates and exports `CATALOG_TOKEN`) before every test.
- **Files modified:** e2e/85-catalog-install.bats
- **Verification:** Full `bats e2e/85-catalog-install.bats` run, 8/8 green, repeated twice for consistency.
- **Committed in:** `27be3e5` (Task 3 commit)

---

**Total deviations:** 3 auto-fixed (all Rule 1 — bugs found and fixed against live, verified Jellyfin behavior, not against the plan's design intent)
**Impact on plan:** All three fixes are corrections to test assertions against empirically-verified server behavior; none changed the production code's design or scope. The plan's core claims (checksum-verified merge, verbatim changelog carry-through, hermetic install-then-update) all hold as specified.

## TDD Gate Compliance

Tasks 1 and 2 each carry `tdd="true"` and specify a RED-then-GREEN commit pair. Both landed as a **single combined commit** instead:

- Task 1: `4793ead` — `feat(06-01): carry a version's changelog section into the packaged zip`
- Task 2: `3673d63` — `feat(06-01): add manifest.sh merge, checksum-verified multi-version manifest`

**Reason:** this repository's `.pre-commit-config.yaml` runs the `test` hook (`mise run test`, which includes `bats tests/scripts`) on any change under `scripts/` or `tests/scripts/`. A RED-only commit — new tests, `changelog_entry()`/`merge` not yet implemented — would leave `bats` failing and the hook would block the commit. `--no-verify` is forbidden by both this project's and the global development standards. RED was still genuinely proven before implementing, in the same shape the repository's convention already uses for this exact constraint (skip-marked tests, confirmed failing, then unskipped in the same commit as the implementation) — see the full working detail in each task's execution trace. **No RED-phase commit exists in the git log for either task; this is a deliberate deviation from the plan's literal `tdd="true"` commit-pattern instruction, driven by the pre-commit hook's scope, not an omission.**

Task 3 is `type="auto"` (not `tdd="true"`) and committed normally as a single `feat` commit once its 8 tests all passed live.

## Issues Encountered

- A `git commit` attempt for Task 1 failed twice with `error: 1Password: agent returned an error` (the SSH-signing agent's desktop approval prompt did not land while the maintainer was away from the keyboard). Per the two-attempts-then-stop rule, execution paused and handed back to the orchestrator with the full implementation staged and verified (tests green, lint clean) but uncommitted. On resume, the maintainer confirmed being at the keyboard and the same commit landed on the first retry with no code changes needed.

## User Setup Required

None - no external service configuration required. (The repository-visibility switch and both `git push origin v<version>` tag pushes remain reserved for the maintainer, per Phase 5's D-15 and this phase's D-13 — neither is due until later plans in this phase.)

## Next Phase Readiness

- `scripts/manifest.sh` has exactly the `merge` action; plan 06-02 adds `rebuild` (the GitHub Releases API enumeration) and `verify` to the same file, extending rather than replacing today's `usage()`/`main()` shape.
- The catalog install/update path is proven hermetically and runs on every `mise run e2e` / CI `e2e` job — a regression in `package.sh`'s or `manifest.sh`'s output will be caught before either script is ever exercised against the real public GitHub Pages URL.
- No blockers for 06-02. The three facts this plan's `<output>` section asked to carry forward (property-name casing, the plugin folder naming convention, and the `dotnet publish` timing cost) are recorded above for 06-02 and 06-04 to read.

---
*Phase: 06-catalog-install-and-first-public-release*
*Completed: 2026-09-22*

## Self-Check: PASSED

- All 8 created/modified files confirmed present on disk.
- All 3 task commits (`4793ead`, `3673d63`, `27be3e5`) confirmed in `git log`.
- Re-ran every task's `<verify>` block: `bats tests/scripts/package.bats` (16/16), `bats tests/scripts/manifest.bats` (15/15), `bats e2e/85-catalog-install.bats` (8/8), all three `rg`-based acceptance gates, `shellcheck -x`/`shfmt -d` for every changed script and bats file, and `git diff --exit-code .github/workflows/ci.yml` — all passed.
- Re-ran the plan-level `<verification>` block in full: `mise run test` (exit 0), `mise run e2e` (exit 0, 49/49 including `90-jellyfin-log.bats` last), `mise run lint` (exit 0), `git diff --exit-code .github/workflows/ci.yml` (exit 0).
- `scripts/dev-env.sh up` then `down` confirmed the demo still starts exactly the three services it started before this plan (`emby`, `emby-proxy`, `jellyfin`).
