# Phase 6: Catalog Install and First Public Release - Context

**Gathered:** 2026-09-21
**Status:** Ready for planning

<domain>
## Phase Boundary

This phase gives any Jellyfin 12.1 administrator one stable manifest URL to add, install from, and receive updates from, and it publishes the first two versions through that path. It covers PUB-02 (one multi-version `manifest.json` on GitHub Pages, published by a separate workflow), REL-04 (each release has a `CHANGELOG.md` entry, and the manifest entry for that version carries the same changelog), DOCS-02 (the README gives the manifest URL and the catalog install and update steps), DOCS-04 (the Jellyfin version bump rule in `CLAUDE.md` names every pin), PUB-04 (an administrator adds the URL, installs, and receives an update), and PUB-05 (v1.0.0.0 is tagged and published through the release workflow).

In scope: a new `scripts/manifest.sh` with its mise task and bats coverage, a new Pages workflow, a changelog reader and a build-time refusal in `scripts/package.sh`, a `PACKAGE_VERSION` override on that script, restructured `CHANGELOG.md` version sections, a new catalog-install end-to-end bats file with its compose services, the README Install and update sections, the `CLAUDE.md` pin list, and two releases — `v0.9.0.0` as the rehearsal and `v1.0.0.0`.

Out of scope: any change to `src/` or to the plugin's login, migration, or settings behavior. Also out of scope: submission to the official Jellyfin plugin repository, signed or attested releases, and automated version bump or changelog tooling — all three recorded as rejected in `PROJECT.md` §Out of Scope.

**Execution is blocked until the repository is public.** Verified on 2026-09-21: `gh repo view` reports `PRIVATE`, there are no tags, no releases, and `gh api repos/:owner/:repo/pages` returns 404. Jellyfin fetches the manifest and the zip with no credentials (`InstallationManager.GetAsync`), and GitHub Pages on a private repository needs a paid plan — the same plan gate that returned HTTP 403 for branch protection during Phase 5. Phase 5's D-15 reserves the visibility change for the maintainer, and it is still pending. Planning may proceed; no task in this phase can run before that switch.

</domain>

<decisions>
## Implementation Decisions

### The Pages manifest (PUB-02)

- **D-01:** The manifest is **rebuilt from the GitHub Releases API** on every publish. It is not appended to, not committed to `main`, and not pushed to a `gh-pages` branch. No workflow gains repository write credentials, so `persist-credentials: false` stays true everywhere and the pedantic zizmor gate from Phase 5's D-08 is not put under pressure. The manifest cannot claim a version whose release does not exist, which is one of the two failure modes `.planning/research/FEATURES.md:43` names as silently breaking the catalog with no error shown to the administrator. A rebuild is idempotent, which is what `PROJECT.md`'s "a failed Pages deploy can be re-run without a new release" asks for.
  - Rejected: a tracked `manifest.json` on `main` appended by `release.yml` and pushed back (BlackDark/jellyfin-plugin-remoteauth, GeiserX/smart-covers) — it needs `contents: write` plus git credentials that `release.yml` currently refuses, lands a bot commit on `main` after every tag, and can drift from the actual releases with nothing to catch it.
  - Rejected: an orphan `gh-pages` branch the release workflow pushes to (jellyrock) — same credential cost, and jellyrock needed a GitHub App token because the default `GITHUB_TOKEN` could not push there. It also conflicts with deploying Pages from Actions, since a branch source and a workflow source are different Pages build types.
- **D-02:** Each version's entry comes from **that release's attached `manifest.json`**, and the rebuild then **downloads that release's zip and confirms the MD5 matches** before writing the entry. A mismatch fails the Pages build loudly. Jellyfin computes `Convert.ToHexString(MD5.HashData(stream))` and compares it case-insensitively with no algorithm-prefix parsing; a mismatch throws `InvalidDataException` server-side and the administrator sees only a failed install. `FEATURES.md:43` asks for this to be checked end to end, not only unit-tested. The zips are small, so re-downloading every one per Pages run costs nothing.
  - This settles the fate of the single-version `manifest.json` that `scripts/package.sh` already attaches to each release: it stops being redundant and becomes the rebuild's input.
- **D-03:** The Pages workflow triggers on **`workflow_run`** for the Release workflow with `types: [completed]`, guarded on `conclusion == 'success'`, plus **`workflow_dispatch`**.
  - **Verified during this discussion, against GitHub's own documentation (three pages, identical text — "Trigger a workflow", "GITHUB_TOKEN", "Triggering a workflow"):** "events triggered by the `GITHUB_TOKEN` will not create a new workflow run", with `workflow_dispatch` and `repository_dispatch` as the only unconditional exceptions. `release.yml:59-64` creates the release with `GH_TOKEN: ${{ github.token }}`, so **`on: release: published` would never fire.** The release run itself is started by the maintainer's tag push, so the `workflow_run` chain is unbroken. The planner must confirm this on the `v0.9.0.0` rehearsal before `v1.0.0.0` depends on it.
  - Rejected: `workflow_dispatch` alone — a forgotten dispatch leaves the catalog advertising the previous version, which is the stranded-catalog bug GeiserX's `pages.yml` comment records, with a human cause instead of a transient one.
  - Rejected: a second job in `release.yml` with `needs: [release]` — it contradicts `PROJECT.md`'s locked choice of a separate workflow, because re-running one job replays it inside the tagged run's context.
- **D-04:** The rebuild logic lives in a new **`scripts/manifest.sh`** with a required action argument, a `[tasks.*]` entry in `.mise.toml`, and a bats file in `tests/scripts/` that puts a **fake `gh` on `PATH`** — the seam Phase 5's D-04 established precisely so the argument construction is exercised rather than skipped. This keeps `CLAUDE.md`'s one-mise-task-per-CI-job rule intact and lets the maintainer rebuild and inspect the manifest locally without pushing anything.
  - Rejected: `Kevinjil/jellyfin-plugin-repo-action` — it reads global metadata from a `build.yaml` this repository does not have and does not want, since `scripts/package.sh` owns that metadata; it cannot be run or tested locally; and its checksum handling reads a `.md5` asset this repository does not publish.
  - Rejected: inline `jq` and `gh` steps in the workflow YAML — not runnable locally, not coverable by bats, and it breaks the one-mise-task-per-CI-job rule.
- **D-05:** After `deploy-pages`, the Pages workflow **fetches the published manifest URL and verifies its own output** — the document parses, and every entry's checksum still matches its live release zip. This reuses the checksum logic D-02 already requires, applying Phase 5's own rule that an existing check should carry an audit item rather than a second check that can drift.

### The changelog in the manifest (REL-04)

- **D-06:** `scripts/package.sh build` **reads the `CHANGELOG.md` section for the version it is packaging** and writes it into both `meta.json` and the manifest asset. One source of truth, frozen at release time, so the catalog text always describes the zip that actually shipped and a later edit to `CHANGELOG.md` cannot rewrite what the catalog says about an old version. `tests/scripts/package.bats` asserts it directly. This replaces the `changelog: ("Release " + $version)` placeholder at `scripts/package.sh:65`.
  - Rejected: the GitHub release body from `--generate-notes` — it emits a commit and pull request list rather than a human summary, and it puts the catalog text outside the repository where no test can check it.
  - Rejected: a separately maintained manifest blurb — two places to keep in sync, which is the drift REL-04 exists to prevent.
- **D-07:** A version section is **`## [<version>] - <YYYY-MM-DD>`**, the keepachangelog form the file's existing `### Added` / `### Removed` / `### Changed` subsections already follow. At release time the `## Unreleased` heading is renamed to that form and a fresh empty `## Unreleased` is added above it. The date is the released signal — a section still headed `## Unreleased` is by construction not extractable, so a forgotten rename cannot ship a wrong changelog. jellyrock gates on the same signal.
- **D-08:** The manifest's `changelog` field carries **the section body verbatim** — everything under the version heading down to the next `##` heading, unchanged. REL-04 says the manifest carries the *same* changelog, and verbatim is the only form where a test can assert that literally, as byte equality between the file section and the manifest field. JSON carries the newlines. Whether Jellyfin's web client renders the field as markdown or as plain text is **unverified**; the `v0.9.0.0` rehearsal is the moment to look at it on a real server. If it renders flat, the `###` headings degrade to readable lines rather than breaking anything.
- **D-09:** When the version being packaged has **no dated section**, `scripts/package.sh build` **refuses** with a message naming the missing version. It is the earliest possible point and it fires identically on a laptop, in the CI `test` job, and in the release workflow's `mise run package` step — so a release stops before `gh release create` rather than after. `package.bats` covers it the way it covers `check-tag` today. Fails closed, matching Phase 5's D-02 gate posture.
  - **Consequence the planner must handle:** `mise run test` runs `tests/scripts/package.bats`, which calls `scripts/package.sh build`. Once the build refuses an undated version, the suite depends on `CHANGELOG.md` holding a dated section for whatever `Directory.Build.props` currently says. Two things follow: the version bump and the changelog dating land in the **same commit**, and `package.bats` needs a changelog-path override in the style of the existing `PACKAGE_OUTPUT_DIR`, `RELEASE_URL_BASE`, and `RELEASE_TIMESTAMP`.

### The two releases (PUB-05)

- **D-10:** `0.9.0.0` is an **ordinary full release, listed in the manifest, and it stays listed.** Roadmap criterion 5 needs the manifest to list both versions so the update can be observed, and `PROJECT.md` rejected a manifest that drops old versions because it breaks Jellyfin's update check and any downgrade path. It is genuinely installable and genuinely works, so marking it a pre-release would claim something untrue about it. — **Reversibility:** one-way — once the release exists at a public URL, anyone may have downloaded it, and `PROJECT.md`'s keep-every-version decision forbids removing it from the manifest afterwards.
  - Rejected: `gh release create --prerelease` with the rebuild explicitly not filtering prereleases — the non-filtering would read as a bug to a later contributor.
  - Rejected: deleting the `0.9.0.0` release once `1.0.0.0` proves the update path (LizardByte's pattern) — it conflicts with the locked keep-every-version decision.
- **D-11:** `0.9.0.0`'s changelog entry is **rewritten as a first release**, not dated as-is. The current `## Unreleased` section is a delta against nothing: it describes removing a migration-behavior setting no public server ever held, renaming a scheduled-task key no public server ever configured, and an Upgrade note about a JSON-to-SQLite store no public server ever ran. Published verbatim as the first release's changelog — and D-08 makes it the catalog blurb a first-time installer reads — that text describes an upgrade nobody can have performed. The replacement says what the release *is*. This applies the repository's own rule about writing the end state rather than the route to it; the development history stays fully readable in git and in `.planning/`, which Phase 5's D-11 already publishes.
- **D-12:** `1.0.0.0`'s changelog entry is **written after the rehearsal**, from what the `0.9.0.0` catalog install and update actually turned up, plus a line stating this is the first stable release. The rehearsal exists to find problems, and its findings are exactly what belongs between the two versions. **This text cannot be drafted during planning.** If the rehearsal is clean, the entry says so plainly and nothing is invented to fill it.
  - Rejected: holding milestone content back so `1.0.0.0` has substance — then `0.9.0.0` deliberately ships less than the code does and no longer rehearses what actually gets released.
- **D-13:** **The maintainer pushes both tags personally.** The phase prepares the version bump pull request, the dated changelog, the manifest script, and the Pages workflow, then stops at a checkpoint handing over `git push origin v0.9.0.0`, and later `git push origin v1.0.0.0`. Same shape as Phase 5's D-15. A public release cannot be unpublished from anyone who already downloaded it, and the release workflow creates a real GitHub release the moment the tag lands. Putting the act in the maintainer's hands means no checkpoint answer can be misread as consent. — **Reversibility:** one-way — the tag push publishes a release at a public URL that administrators may add to a live server.
  - Note for the planner: neither PUB-05 nor roadmap criteria 4 and 5 can be closed from inside an execution run. The phase reaches a `blocking-human` checkpoint at each tag and verifies afterwards with `gh release list` and the live manifest.
- **D-14:** If `0.9.0.0`'s catalog install fails, **fix forward to `0.9.1.0`, then `0.9.2.0`.** A version number is never reused. The stateless rebuild picks up each new version with no extra work, and every extra version exercises the update path once more — which is what PUB-04 has to prove anyway. Nobody can ever hold two different zips both calling themselves `0.9.0.0`.
  - Rejected: deleting the release and tag and re-pushing `v0.9.0.0` — it lets one version number name different bytes, which is the one thing a checksum-verified catalog exists to make impossible.
- **D-15:** Sequencing, fixed: the manifest script, the Pages workflow, the changelog wiring, and the end-to-end catalog test land and are proven first; then `v0.9.0.0`; then the catalog install and update are exercised; then `v1.0.0.0`. `Directory.Build.props` reads `1.0.0.0` today, so the rehearsal moves it down to `0.9.0.0` and back up — two pull requests to `main`, because `docs/development.md:38-39` requires the version change to be merged before the tag and `scripts/release-gate.sh` refuses any tag whose commit is not at or behind the default branch.

### Proving the install (PUB-04)

- **D-16:** PUB-04 is proven by a **hermetic end-to-end test plus one manual browser check.** A new bats file serves the real generated manifest and the real zips from a local nginx — the image `nginx:1.30.5-alpine` is already pinned for `emby-proxy` — and drives `POST /Repositories`, `GET /Packages`, an install of the lower version, then an update to the higher one. That proves the manifest parses, the MD5 matches, and both install and update work, and it keeps running on every pull request. The maintainer then does one browser walkthrough against the real public URL on a clean Jellyfin 12.1 server, which is the part no local test can cover.
  - **Verified during this discussion, from Jellyfin's own `Jellyfin.Api/Controllers/PackageController.cs`:** the whole catalog flow is API-driveable — `POST /Repositories` sets `Configuration.PluginRepositories`, `GET /Packages` returns the catalog from `GetAvailablePackages`, and `POST /Packages/Installed/{name}` takes optional `version` and `repositoryUrl` query parameters and calls `GetCompatibleVersions` then `InstallPackage`. `e2e/helpers.bash:27-51` already has the authenticated `api` and `status` helpers with the `MediaBrowser` authorization header.
  - Rejected: an end-to-end test against the live public URL — it cannot pass until the repository is public, Pages is deployed, and `0.9.0.0` is released, so it would fail every CI run until the end of the phase, and afterwards every pull request would depend on GitHub Pages being reachable.
  - Rejected: a manual walkthrough alone — it leaves no automated guard, so a later change to `package.sh` or `manifest.sh` could silently break the catalog until an administrator reports a failed install.
- **D-17:** The test updates between **two genuinely built versions**, using a new documented **`PACKAGE_VERSION`** override on `scripts/package.sh` alongside the three overrides it already has. The zip, its `meta.json`, and the manifest entry all agree, so Jellyfin's installed-version detection and update check see what they would in production.
  - Accepted cost, recorded so it is not rediscovered: the override could in principle mislabel a real release zip, because the DLL's assembly version still comes from `Directory.Build.props`. The planner should document the override as test-only in the script's `# Environment:` block, next to `PACKAGE_OUTPUT_DIR`, `RELEASE_URL_BASE`, and `RELEASE_TIMESTAMP`.
  - Rejected: one build with two manifest entries — the installed `meta.json` would report the real build version and disagree with the manifest entry, so the test would not prove what it appears to.
  - Rejected: building twice from a rewritten copy of `Directory.Build.props` — it gives the script a props-path seam instead of a version seam, which is the same class of thing one step removed, and complicates the test setup.
- **D-18:** The new bats file lives **in the existing `e2e/` suite** and runs under `mise run e2e` and the existing `e2e` CI job. `ci.yml`'s `ci-success` `needs:` list and the hardcoded count of `3` beside it (`ci.yml:96`, `:119-122`) stay untouched — that job name is the contract `scripts/release-gate.sh` matches on, and Phase 5's D-09 exists to protect it.
  - **Constraint the planner must resolve:** `e2e/compose.yaml:25` bind-mounts `../artifacts/plugin` onto `/config/plugins/EmbyAuth_1.0.0.0`, and a catalog install writes into `/config/plugins/` itself. The catalog test therefore needs its own clean Jellyfin service rather than the shared one, alongside the manifest-serving nginx.
  - Rejected: a separate mise task and CI job — it would change `ci-success`'s `needs:` list and the count beside it, which `ci.yml`'s own comment warns must move together.
  - Rejected: living in the suite but skipped unless a flag is set — a test that does not run by default is a test that stops running, and it would not guard the catalog on an ordinary pull request.

### Claude's Discretion

The maintainer did not settle these; the planner does, within the constraints above.

- The action-argument names for `scripts/manifest.sh`, and the exact `jq` shape of the composed manifest, provided the schema matches what `PluginManifest` and `VersionInfo` parse.
- Whether the Pages site serves a landing `index.html` beside `manifest.json` (GeiserX and jellyrock both do) and what it says. Also `.nojekyll`, which GeiserX sets so files are served verbatim.
- How GitHub Pages gets switched to the **GitHub Actions** build type — a one-time change in repository settings, or a `gh api` call in the workflow. It must happen before any deploy works, either way.
- The exact regex or parser `scripts/package.sh` uses to find and extract a version's `CHANGELOG.md` section, and the name of the changelog-path override that `package.bats` needs.
- The wording of the README's manifest URL and catalog install and update steps (DOCS-02), and where they sit relative to the existing Install and Compatibility sections.
- The exact pin list wording in `CLAUDE.md` (DOCS-04). It must add the test project's `Jellyfin.Controller` reference (`tests/Jellyfin.Plugin.EmbyAuth.Tests/Jellyfin.Plugin.EmbyAuth.Tests.csproj:9`) and the `targetAbi` assertions in `tests/scripts/package.bats:47` and `:61` to the four pins already named at `CLAUDE.md:50`.
- The new end-to-end file's number and name, the compose service names and ports, and how the nginx serves the generated artifacts.
- Whether `scripts/manifest.sh` gets a separate `verify` action, and how much bats coverage the post-deploy smoke check gets.
- The wording of the two release checkpoints that hand the maintainer each `git push origin v<version>`.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase requirements and prior decisions

- `.planning/ROADMAP.md` §Phase 6 — the goal and the five success criteria, including the `0.9.0.0`-rehearsal-then-`1.0.0.0` order.
- `.planning/REQUIREMENTS.md` — PUB-02 (`:69`), PUB-04 (`:71`), PUB-05 (`:72`), REL-04 (`:64`), DOCS-02 (`:40`), DOCS-04 (`:42`), and the traceability rows at `:118`–`:138`.
- `.planning/PROJECT.md` §Key Decisions — a public repository with one multi-version manifest on GitHub Pages, published by a separate workflow; the release checks the CI result of the tagged commit. §Out of Scope — a manifest listing only the latest version, a `raw.githubusercontent.com` branch file, submission to the official Jellyfin repository, signed or attested releases, and automated version bump tooling were all rejected. §Constraints — the publishing constraint that gates this whole phase.
- `.planning/phases/05-public-repository/05-CONTEXT.md` — D-15 (the maintainer performs the irreversible act personally; D-13 here follows it), D-09 (the `ci-success` check-run name is the release gate's contract; D-18 here protects it), D-04 (the fake-`gh`-on-`PATH` bats seam; D-04 here reuses it), D-17 and D-18 (the README Compatibility section that DOCS-02 points at), D-11 (`.planning/` is published as-is).
- `.planning/phases/03-migration-status-and-target/03-CONTEXT.md` — D-18, the plugin never claims behavior it has not verified. Applies to every new README sentence in DOCS-02.
- `.planning/STATE.md` §Current Position — Phase 5 is verified but PUB-03 is open, awaiting the maintainer's visibility change. §Blockers/Concerns — the research flag about Pages action SHAs for this phase.

### Repository rules

- `CLAUDE.md` — one mise task per CI job, so a CI step change edits the mise task and not only the workflow; run `prek run` before each commit; a change that depends on Jellyfin behavior needs an end-to-end test; pin exact versions and look up the current stable version before a bump; `:50` is the version bump rule DOCS-04 completes.
- `~/projects/CLAUDE.md` §Conventions — scripts require an explicit action argument and never default to a mutating operation; anything repeatable belongs in `scripts/`, tracked and shellcheck-clean.
- `~/.claude/rules/github-actions.md` — pin actions to full SHAs with a version comment, set `persist-credentials: false`, scan with zizmor before committing. Phase 5 made `zizmor --persona=pedantic` the standing gate, so the new Pages workflow must be pedantic-clean: a `name:` on its job, a documented `permissions:` block, and a `concurrency:` setting.
- `.claude/rules/e2e.md` — the end-to-end harness rules the new catalog test follows.

### Files this phase changes

- `scripts/package.sh` — `:29-31` `plugin_version()`; `:33-38` `target_abi()`, the shape a `changelog_entry()` helper follows; `:10-13` the `# Environment:` block the `PACKAGE_VERSION` override joins; `:60-77` the `meta.json` `jq` call and `:65` the `changelog: ("Release " + $version)` placeholder D-06 replaces; `:84-102` the manifest `jq` call; `:25-27` and `:107-124` the usage and action-argument convention.
- `tests/scripts/package.bats` — `:17` the `MANIFEST` path; `:47` and `:61` the hardcoded `12.1.0.0` `targetAbi` assertions DOCS-04 names; `:54-63` the manifest assertions the changelog assertion joins; `:20-30` the usage and unknown-action tests every script in `scripts/` is expected to have.
- `.github/workflows/release.yml` — `:59-64` the `gh release create` step whose `GH_TOKEN: ${{ github.token }}` is why D-03 cannot use `on: release`; `:17-19` the concurrency block Phase 5 added; `:26-28` the job's `permissions:` block.
- `.github/workflows/ci.yml` — `:90-122` the `ci-success` job, its `needs:` list at `:96`, and the hardcoded count of `3` at `:119-122` that D-18 keeps untouched; `:94` the deliberate repeated job name.
- `.mise.toml` — `[tools]` at `:1-12`, which gains any new pinned tool; `[tasks.lint]` at `:20-29`; `[tasks.package]` at `:48-50`; `[tasks.e2e]` at `:52-54`. The new manifest task joins this file.
- `CHANGELOG.md` — `:5-26`, the single `## Unreleased` section D-07 restructures and D-11 rewrites.
- `Directory.Build.props` — `:3-5`, the three version properties D-15 moves to `0.9.0.0` and back to `1.0.0.0`.
- `README.md` — `:26-41` the Install section DOCS-02 rewrites around the manifest URL; `:10` the Status line that still says "Not published to a plugin repository", which stops being true; `:18-24` the Compatibility section Phase 5 added, which the new steps point at.
- `docs/development.md` — `:36-44` the Releases procedure, which gains the changelog step and the manifest and Pages behavior; `:5-16` the task table the new mise task joins.
- `e2e/compose.yaml` — `:24-28` the plugin bind mount that collides with a catalog install; `:11` the already-pinned `nginx:1.30.5-alpine`; `:18` the pinned Jellyfin image.
- `e2e/helpers.bash` — `:27-51` the `api` and `status` helpers with the `MediaBrowser` authorization header; `:154-184` the Jellyfin admin token and user helpers; `:240-241` the plugin configuration helpers.
- `scripts/manifest.sh`, `tests/scripts/manifest.bats`, `.github/workflows/pages.yml`, and the new `e2e/NN-*.bats` — new.

### Jellyfin behavior verified during this discussion

- **The catalog flow is API-driveable.** `Jellyfin.Api/Controllers/PackageController.cs`: `POST /Repositories` sets `Configuration.PluginRepositories` and saves; `GET /Packages` returns `GetAvailablePackages()`; `GET /Packages/{name}` filters by name or assembly GUID; `POST /Packages/Installed/{name}` takes optional `version` and `repositoryUrl` query parameters, filters packages by repository URL, resolves through `GetCompatibleVersions(specificVersion:)`, and calls `InstallPackage`. It returns 204 and installs asynchronously, so a test must poll rather than assume the install finished.
- **`on: release` will not fire for this repository's releases.** GitHub's documentation, in three places ("Trigger a workflow", "GITHUB_TOKEN", "Triggering a workflow"), states that events triggered by the `GITHUB_TOKEN` do not create a new workflow run, with only `workflow_dispatch` and `repository_dispatch` as unconditional exceptions. `release.yml:64` uses `github.token`.
- **The repository is not yet publishable-from.** `gh repo view` reports `PRIVATE`; `git tag --list` is empty; `gh release list` is empty; `gh api repos/:owner/:repo/pages` returns HTTP 404.
- **Checksums are plain hex MD5, no prefix.** `Emby.Server.Implementations/Updates/InstallationManager.cs` computes `Convert.ToHexString(MD5.HashData(stream))` and compares case-insensitively against `VersionInfo.Checksum`. `scripts/package.sh:83` already emits this form with `openssl dgst -md5 -r`.
- **`targetAbi` is a floor with no ceiling.** Jellyfin filters catalog entries with `Version.Parse(x.TargetAbi) <= appVer` in `GetCompatibleVersions`. `scripts/package.sh:34-38` derives `12.1.0.0` from the `Jellyfin.Controller` pin.

### Research

- `.planning/research/FEATURES.md:17` — the exact `manifest.json` schema `PluginManifest` and `VersionInfo` parse; a missing or misshapen field fails silently with a server-side `JsonException` and an empty package list. `:18` — the plain-hex-MD5 requirement and the `sha256:` documentation trap. `:20` — why the manifest must list every released version. `:22` — the unauthenticated fetch that makes the repository's visibility a hard prerequisite. `:24` and `:82` — the per-version changelog as a Jellyfin best practice and the field the catalog UI shows. `:31` — the manifest-hosting patterns and the third-party actions. `:43` — the two failure modes most likely to break the catalog silently, and the instruction to verify them end to end before tagging v1.0.0.
- `.planning/research/SUMMARY.md:71` — `targetAbi` is a minimum, not a maximum (Jellyfin issue #11331). `:88` — the research flag about Pages action SHAs for this phase, which the planner resolves by looking up current stable SHAs for `actions/configure-pages`, `actions/upload-pages-artifact`, and `actions/deploy-pages`.
- Third-party repositories read during this discussion, as patterns not as dependencies: `GeiserX/smart-covers` `.github/workflows/pages.yml` (a decoupled Pages workflow, and the stranded-catalog bug that motivated it), `jellyrock/jellyfin-plugin-jellyrock` `.github/workflows/release.yml` (gh-pages worktree, App token, `changelog-extract.sh`, the release-only-when-the-changelog-section-is-dated gate), `Kevinjil/jellyfin-plugin-repo-action` (rebuild from the Releases API), `LizardByte/jellyfin-plugin-repo` (version removal on release delete), `BlackDark/jellyfin-plugin-remoteauth` (jq append and commit back to main).

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets

- `scripts/package.sh` — the shape `scripts/manifest.sh` copies: a usage function that exits 2, a required action argument, `REPO_ROOT` resolved from `BASH_SOURCE`, small single-purpose helper functions that read one value out of one file, and environment variables for anything a test needs to control.
- `scripts/release-gate.sh` and its bats file — the closest analog to `manifest.sh`: a script whose whole job is `gh api` calls, covered by a fake `gh` on `PATH`. The planner should read it before writing either new file.
- `tests/scripts/package.bats:1-30` — the `setup_file` and `setup` shape, and the two tests asserting a missing action and an unknown action both print usage and exit 2.
- `e2e/helpers.bash:27-51` — `api` and `status`, already carrying the `MediaBrowser` authorization header the catalog endpoints need. `:154` `jellyfin_token`-style admin authentication already exists.
- `e2e/compose.yaml:11` — `nginx:1.30.5-alpine`, already pinned for `emby-proxy`, so the manifest-serving container adds no new image dependency.
- `.mise.toml` `[tasks.package]` and `[tasks.e2e]` — the one-line task shape the manifest task follows.

### Established Patterns

- Every action in both workflows is pinned to a full SHA with a trailing version comment, and every checkout sets `persist-credentials: false`. The new Pages workflow matches, and must also be `zizmor --persona=pedantic` clean — a named job, a commented `permissions:` block, and a `concurrency:` setting — because Phase 5 made pedantic the standing gate.
- CI job names, mise task names, and pre-commit hook ids line up, so `mise run <task>` locally reproduces the job.
- Scripts in `scripts/` never default to a mutating action; the action argument is required and an unknown one exits 2.
- End-to-end files are independent `NN-topic.bats` files over a shared stack from `setup_suite.bash`, numbered 10 through 90 today.
- The repository writes the end state, not the route to it — which is the rule D-11 applies to `0.9.0.0`'s changelog entry.

### Integration Points

- `scripts/package.sh` gains a changelog reader, a refusal, and a version override; `tests/scripts/package.bats` gains matching assertions and a changelog fixture seam.
- A new `.github/workflows/pages.yml` consumes the Release workflow's completion and needs `pages: write` and `id-token: write` for `deploy-pages`, plus whatever read scope `gh` needs for the Releases API.
- `.mise.toml` gains a manifest task; `docs/development.md`'s task table and Releases procedure gain the matching rows and steps.
- `e2e/compose.yaml` gains a clean Jellyfin service and a manifest-serving nginx, both used only by the new catalog file.
- GitHub Pages must be switched to the **GitHub Actions** build type before any deploy succeeds. Nothing in the repository does this today.

</code_context>

<specifics>
## Specific Ideas

- The current `## Unreleased` section is a delta against nothing, and D-08 makes it the first thing a Jellyfin administrator reads beside the version in the catalog. Write `0.9.0.0`'s entry for someone who has never seen this repository and is deciding whether to install. The development deltas are not lost — git and `.planning/` hold them, and Phase 5's D-11 publishes both.
- `1.0.0.0`'s changelog is a genuine unknown at planning time. Do not draft placeholder text for it, and do not invent content to make the entry look substantial. The plan should carry it as a task that runs after the rehearsal, with its input being what the rehearsal found.
- D-05's post-deploy smoke check follows the same principle Phase 5's D-05 and D-14 established: where an existing check can carry an audit item, let it, rather than adding a parallel check that can drift. The checksum verification D-02 needs at build time is the same code the smoke check needs at publish time.
- The maintainer holds both tag pushes (D-13), exactly as they hold the visibility change (Phase 5 D-15). Do not write a task, a script action, or a checkpoint that runs `git push origin v<version>` or `gh release create`. The phase's deliverable at each of those two points is a prepared, merged commit and the command for the maintainer to run.
- `PACKAGE_VERSION` (D-17) is the only decision in this phase that adds a way to get a release wrong. Document it as test-only in the script, and let `package.bats` prove that an unset override still reads `Directory.Build.props`.
- Phase 5 verified that branch protection returns HTTP 403 on this repository today and becomes available the moment it goes public. That is now true. It is still not in this phase's scope — see Deferred Ideas.

</specifics>

<deferred>
## Deferred Ideas

- **Branch protection requiring `ci-success` on `main`** — carried forward from Phase 5's deferred list. It becomes available the moment the repository is public, and Phase 5's D-09 deliberately preserved the `ci-success` check-run name so the rule can be added without touching the release gate. Not in this phase; worth raising immediately after the visibility switch.
- **A scheduled job that checks the live catalog on a timer** — the natural extension of D-05's post-deploy smoke check, catching a manifest that breaks without a deploy (a deleted release, a changed asset). Not raised as a requirement and not needed to close PUB-04. Revisit if the catalog ever breaks silently.
- **Submitting the manifest URL to `jellyfin.org`'s community repository list or `awesome-jellyfin`** — `.planning/research/FEATURES.md:32` and `:171` name the trigger: v1.0.0.0 running stably on at least one install that is not the maintainer's. `PROJECT.md` puts submission to the *official* repository out of scope; the community list is the lighter version of the same idea and is a v1.x follow-up.
- **A `SECURITY.md`, issue templates, or a contributing guide** — carried forward from Phase 5's deferred list. Strangers can file issues the moment the repository is public and the repository has none of these. Not in any requirement.
- **Retiring `scripts/pre-public-audit.sh`** — carried forward from Phase 5. It has now served its one purpose. If it is still unused well after v1.0.0.0, the repository's own replace-don't-deprecate rule says delete it.
- **Tidying the pushed `gsd/phase-*` branches** — carried forward from Phase 5. Housekeeping, not a publication blocker.
- **Automated version bump and changelog tooling** — `PROJECT.md` §Out of Scope rejected it as disproportionate to a single maintainer's cadence. D-07's dated-section convention is the manual discipline that replaces it; `semantic-release` or `changesets` only becomes worth reconsidering if release frequency grows.

</deferred>

---

*Phase: 6-Catalog Install and First Public Release*
*Context gathered: 2026-09-21*
