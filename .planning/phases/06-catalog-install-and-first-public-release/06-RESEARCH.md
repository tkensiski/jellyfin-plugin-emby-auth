# Phase 6: Catalog Install and First Public Release - Research

**Researched:** 2026-09-21
**Domain:** Jellyfin plugin repository manifests, GitHub Pages publishing from Actions, GitHub release/changelog tooling
**Confidence:** HIGH for Jellyfin server behavior and the current repository's scripts (read from source this session); HIGH for GitHub Actions/Pages mechanics (read from GitHub's own docs and API this session); MEDIUM for the exact shape of the new `scripts/manifest.sh` (explicitly Claude's Discretion in 06-CONTEXT.md — patterns given here, not a final design).

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**The Pages manifest (PUB-02)**

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

**The changelog in the manifest (REL-04)**

- **D-06:** `scripts/package.sh build` **reads the `CHANGELOG.md` section for the version it is packaging** and writes it into both `meta.json` and the manifest asset. One source of truth, frozen at release time, so the catalog text always describes the zip that actually shipped and a later edit to `CHANGELOG.md` cannot rewrite what the catalog says about an old version. `tests/scripts/package.bats` asserts it directly. This replaces the `changelog: ("Release " + $version)` placeholder at `scripts/package.sh:65`.
  - Rejected: the GitHub release body from `--generate-notes` — it emits a commit and pull request list rather than a human summary, and it puts the catalog text outside the repository where no test can check it.
  - Rejected: a separately maintained manifest blurb — two places to keep in sync, which is the drift REL-04 exists to prevent.
- **D-07:** A version section is **`## [<version>] - <YYYY-MM-DD>`**, the keepachangelog form the file's existing `### Added` / `### Removed` / `### Changed` subsections already follow. At release time the `## Unreleased` heading is renamed to that form and a fresh empty `## Unreleased` is added above it. The date is the released signal — a section still headed `## Unreleased` is by construction not extractable, so a forgotten rename cannot ship a wrong changelog. jellyrock gates on the same signal.
- **D-08:** The manifest's `changelog` field carries **the section body verbatim** — everything under the version heading down to the next `##` heading, unchanged. REL-04 says the manifest carries the *same* changelog, and verbatim is the only form where a test can assert that literally, as byte equality between the file section and the manifest field. JSON carries the newlines. Whether Jellyfin's web client renders the field as markdown or as plain text is **unverified**; the `v0.9.0.0` rehearsal is the moment to look at it on a real server. If it renders flat, the `###` headings degrade to readable lines rather than breaking anything.
- **D-09:** When the version being packaged has **no dated section**, `scripts/package.sh build` **refuses** with a message naming the missing version. It is the earliest possible point and it fires identically on a laptop, in the CI `test` job, and in the release workflow's `mise run package` step — so a release stops before `gh release create` rather than after. `package.bats` covers it the way it covers `check-tag` today. Fails closed, matching Phase 5's D-02 gate posture.
  - **Consequence the planner must handle:** `mise run test` runs `tests/scripts/package.bats`, which calls `scripts/package.sh build`. Once the build refuses an undated version, the suite depends on `CHANGELOG.md` holding a dated section for whatever `Directory.Build.props` currently says. Two things follow: the version bump and the changelog dating land in the **same commit**, and `package.bats` needs a changelog-path override in the style of the existing `PACKAGE_OUTPUT_DIR`, `RELEASE_URL_BASE`, and `RELEASE_TIMESTAMP`.

**The two releases (PUB-05)**

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

**Proving the install (PUB-04)**

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

### Deferred Ideas (OUT OF SCOPE)

- **Branch protection requiring `ci-success` on `main`** — carried forward from Phase 5's deferred list. It becomes available the moment the repository is public, and Phase 5's D-09 deliberately preserved the `ci-success` check-run name so the rule can be added without touching the release gate. Not in this phase; worth raising immediately after the visibility switch.
- **A scheduled job that checks the live catalog on a timer** — the natural extension of D-05's post-deploy smoke check, catching a manifest that breaks without a deploy (a deleted release, a changed asset). Not raised as a requirement and not needed to close PUB-04. Revisit if the catalog ever breaks silently.
- **Submitting the manifest URL to `jellyfin.org`'s community repository list or `awesome-jellyfin`** — `.planning/research/FEATURES.md:32` and `:171` name the trigger: v1.0.0.0 running stably on at least one install that is not the maintainer's. `PROJECT.md` puts submission to the *official* repository out of scope; the community list is the lighter version of the same idea and is a v1.x follow-up.
- **A `SECURITY.md`, issue templates, or a contributing guide** — carried forward from Phase 5's deferred list. Strangers can file issues the moment the repository is public and the repository has none of these. Not in any requirement.
- **Retiring `scripts/pre-public-audit.sh`** — carried forward from Phase 5. It has now served its one purpose. If it is still unused well after v1.0.0.0, the repository's own replace-don't-deprecate rule says delete it.
- **Tidying the pushed `gsd/phase-*` branches** — carried forward from Phase 5. Housekeeping, not a publication blocker.
- **Automated version bump and changelog tooling** — `PROJECT.md` §Out of Scope rejected it as disproportionate to a single maintainer's cadence. D-07's dated-section convention is the manual discipline that replaces it; `semantic-release` or `changesets` only becomes worth reconsidering if release frequency grows.

**Execution is blocked until the repository is public.** Verified 2026-09-21: `gh repo view` reports `PRIVATE`, no tags, no releases, `gh api repos/:owner/:repo/pages` returns 404. Phase 5's D-15 reserves the visibility change for the maintainer. No task in this phase can run before that switch.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| PUB-02 | One `manifest.json` on GitHub Pages lists every released version with a plain hex MD5 checksum, published by a separate workflow when it changes | Verified `PackageInfo`/`VersionInfo` schema (top-level JSON array, field names and types) against `jellyfin/jellyfin` tag `v12.1` source; verified checksum comparison code; `gh api repos/.../releases --paginate` pattern for exhaustive enumeration (a plain `gh release list` truncates at 30 by default — see Pitfall 1); GitHub Actions `workflow_run` + `workflow_dispatch` trigger mechanics and the default-branch activation requirement (Pitfall 3); current stable SHAs for `actions/configure-pages`, `actions/upload-pages-artifact`, `actions/deploy-pages`; the `pages: write` + `id-token: write` + `environment: github-pages` shape `deploy-pages` requires; the `POST`/`PUT /repos/{owner}/{repo}/pages` API to switch the build type, and why it likely cannot run from the workflow's own `GITHUB_TOKEN` (Pitfall 4) |
| REL-04 | Each release has a `CHANGELOG.md` entry; the manifest entry for that version carries the same changelog | Verified `VersionInfo.Changelog` is `string?`, no length or format constraint server-side; an `awk`-based verbatim-section-extraction pattern that matches keepachangelog's `## [x.y.z] - date` heading convention already decided in D-07 |
| DOCS-02 | README gives the manifest URL and catalog install/update steps | Confirmed the expected GitHub Pages project-site URL shape (`https://<owner>.github.io/<repo>/`) from GitHub's own docs; confirmed the administrator-facing flow (`Dashboard > Plugins > Repositories > Add Repository`) is unchanged from what `README.md:41` already half-describes |
| DOCS-04 | The `CLAUDE.md` version-bump rule names every pin | No new research needed beyond what 06-CONTEXT.md already specifies (`CLAUDE.md:50`, the test project's `Jellyfin.Controller` reference, and the two `targetAbi` assertions in `package.bats`); confirmed by reading `CLAUDE.md` and `tests/scripts/package.bats` this session |
| PUB-04 | An administrator adds the manifest URL, installs, and receives an update | Verified `PackageController.cs`'s API surface (already confirmed in 06-CONTEXT.md); confirmed GitHub Pages serves `.json`-named files as `application/json` by default (its MIME table is generated from `mime-db`), which is what .NET's `GetFromJsonAsync<PackageInfo[]>` requires — a wrong content type throws `NotSupportedException`, which `InstallationManager.cs`'s catch block mislabels as a URL-scheme problem (Pitfall 2); confirmed CORS is not a factor for this fetch (Jellyfin's `HttpClient` is a server-side .NET client, not a browser, so no CORS header applies) |
| PUB-05 | v1.0.0.0 tagged and published through the release workflow | No new research beyond D-13 through D-15 in 06-CONTEXT.md; this requirement is closed by the maintainer's own tag push, not by anything an execution run can automate |
</phase_requirements>

## Summary

This phase has almost no new *Jellyfin* behavior to learn — 06-CONTEXT.md already verified the parts that mattered most (checksum algorithm, `targetAbi` filtering, the API-driveable catalog flow, why `on: release` cannot be used). What this research adds is the exact shape of the two schema classes Jellyfin's `InstallationManager` deserializes (`PackageInfo` and `VersionInfo`, read from the `jellyfin/jellyfin` `v12.1` tag this session), the current stable pins for the three GitHub Pages actions, the mechanics and gotchas of `workflow_run` + `deploy-pages`, and one concrete, previously undocumented failure mode: `gh release list` defaults to 30 results, which is exactly wrong for a rebuild that must include *every* released version (D-01, D-10).

The single highest-leverage finding is about **who can flip GitHub Pages to the "GitHub Actions" build type**. GitHub's own fine-grained-PAT permission table lists `POST`/`PUT /repos/{owner}/{repo}/pages` under the **Administration** permission — a scope that does not exist among the permissions a workflow's `GITHUB_TOKEN` can be granted at all (`pages: write` only covers *deployments*, not the site's build-type setting). This means the "Claude's Discretion" question in 06-CONTEXT.md ("a one-time change in repository settings, or a `gh api` call in the workflow") is narrower than it looks: it cannot reliably be a `gh api` call *inside* the Pages workflow using `${{ github.token }}`. It has to be either the Settings UI or a `gh api` call the maintainer runs personally with their own authenticated `gh` — the same shape as the visibility change (Phase 5 D-15) and the two tag pushes (D-13).

The second finding worth planning around is `workflow_run`'s default-branch activation rule: a `workflow_run`-triggered workflow only fires once its own file is merged to `main`, and even then some reports describe a "first completion after landing" that does not fire. D-15's sequencing (land the Pages workflow and prove it before `v0.9.0.0`) already anticipates this, but the plan should treat the rehearsal's first Pages-workflow run as unverified until it is actually observed to fire — this is precisely the kind of thing D-16's rehearsal exists to catch, not something to assume works from documentation alone.

**Primary recommendation:** Build `scripts/manifest.sh` around `gh api repos/$GH_REPO/releases --paginate` (never `gh release list`'s default-limited form), `gh release download <tag> --pattern 'manifest.json'` per release into a per-tag scratch directory (every release's asset is literally named `manifest.json`, so releases must be processed into separate directories to avoid overwriting each other), re-verify each zip's MD5 against the recorded checksum before folding it into the combined `PackageInfo[]` array, and let the maintainer flip the Pages build type by hand before the first deploy — do not spend planning time trying to script that one step from inside the workflow.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Manifest rebuild (merge every release into one `PackageInfo[]`) | CI/CD (GitHub Actions) | — | Runs entirely in `scripts/manifest.sh`, invoked from a workflow step; no server or client code involved |
| Manifest checksum verification (D-02, D-05) | CI/CD (GitHub Actions) | — | Same script, both at build time (before publish) and as a post-deploy smoke check |
| Manifest hosting | CDN / Static (GitHub Pages) | — | GitHub Pages is a static-file CDN; it serves the file Actions uploaded, nothing more |
| Manifest fetch and parse | API / Backend (Jellyfin server) | — | `InstallationManager.GetPackages` runs server-side inside Jellyfin; there is no browser tier in this flow at all — the "browser" only exists in the Jellyfin dashboard's own admin UI, which calls Jellyfin's own REST API, not the manifest URL directly |
| Zip download, checksum check, extraction | API / Backend (Jellyfin server) | Database / Storage (plugin folder on disk) | `InstallationManager.PerformPackageInstallation` downloads, hashes, and writes to `/config/plugins/` |
| Changelog authoring and extraction | CI/CD (release-time script) | — | `CHANGELOG.md` is a repository file; extraction happens in `scripts/package.sh build`, which runs both locally and in `release.yml` |
| Version-bump PR and tag push | Developer workflow (maintainer, out of automation) | — | D-13/D-15: two merged PRs plus two tag pushes, all performed by the maintainer personally |

## Standard Stack

### Core

| Component | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `actions/configure-pages` | v6.0.0 (`45bfe0192ca1faeb007ade9deae92b16b8254a0d`) | Detects the repository's Pages source and outputs metadata (`base_path`, `origin`) the deploy step can use | Official GitHub Actions org action, the one GitHub's own "Using custom workflows with GitHub Pages" doc leads with; `[VERIFIED: GitHub API, this session — releases/latest and git/refs/tags]` |
| `actions/upload-pages-artifact` | v5.0.0 (`fc324d3547104276b827a68afc52ff2a11cc49c9`) | Packages a directory as the tar/gzip artifact `deploy-pages` expects | Official GitHub Actions org action; `[VERIFIED: GitHub API, this session]` |
| `actions/deploy-pages` | v5.0.1 (`368f82528645a54fb793d4d04e342629a3f51346`) | Publishes the uploaded artifact to the `github-pages` environment | Official GitHub Actions org action; `[VERIFIED: GitHub API, this session]` |

All three tags dereference directly to a commit (`git/refs/tags/<tag>` returned `object.type: commit` for all three, confirmed this session), so the SHAs above are safe to pin as-is with no extra indirection through an annotated tag object.

### Supporting

| Tool | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `gh` CLI | already a GitHub-hosted-runner tool, and locally pinned indirectly via the maintainer's own install (2.97.0 measured locally) | Enumerate releases, download per-release assets, create the GitHub release | `scripts/manifest.sh` (new) and `scripts/release-gate.sh` (existing) both already depend on it |
| `jq` | 1.8.2 (already pinned in `.mise.toml`) | Merge per-release `PackageInfo` documents into one array; extract/compose JSON | Same tool `scripts/package.sh` already uses for `meta.json` and the single-version manifest |
| `openssl` | system tool (already used) | MD5 checksum of each downloaded zip, for the D-02/D-05 re-verification | `openssl dgst -md5 -r`, the exact invocation `scripts/package.sh:83` already uses |
| `awk` | system tool (POSIX) | Extract a `CHANGELOG.md` version section verbatim (D-08) | See Code Examples — matches the existing preference for small, single-purpose shell helpers over a new language runtime |

No new package-manager dependency (npm, pip, cargo) is introduced by this phase. The only new pins are the three GitHub Actions above.

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Rebuilding the manifest with `scripts/manifest.sh` | `Kevinjil/jellyfin-plugin-repo-action` or `LizardByte/jellyfin-plugin-repo` | Both read metadata this repository does not want to duplicate (a separate `build.yaml`), cannot be run or tested locally, and `LizardByte`'s pattern removes old versions on release delete — conflicts with D-10's keep-every-version decision. `[CITED: 06-CONTEXT.md D-04, D-10]` |
| `awk` for changelog extraction | `jellyrock`'s `changelog-extract.sh` (a third-party script) | Not vendored; reading it as a pattern rather than a dependency avoids adding an external script this repository would then have to maintain. `[CITED: 06-CONTEXT.md, "Research" list — read as pattern, not dependency]` |
| GitHub Pages via Actions | Committing `manifest.json` to a `gh-pages` branch, or `raw.githubusercontent.com` on `main` | Already rejected in `PROJECT.md`/D-01; a branch source and Actions source are mutually exclusive Pages build types, and `raw.githubusercontent.com` caches per-IP for several minutes with no bypass (`.planning/research/PITFALLS.md:140`, cited in `SUMMARY.md:30`) |

**Installation:** no `npm install`/`pip install` step — the three actions are referenced by SHA directly in the new `.github/workflows/pages.yml`, following the same `uses: owner/repo@<sha> # vX.Y.Z` convention already used for `actions/checkout` and `jdx/mise-action` in this repository.

**Version verification performed this session:**
```
gh api repos/actions/configure-pages/releases/latest --jq '{tag: .tag_name, published: .published_at}'
  -> {"tag":"v6.0.0","published":"2026-03-25T17:00:49Z"}
gh api repos/actions/upload-pages-artifact/releases/latest --jq '{tag: .tag_name, published: .published_at}'
  -> {"tag":"v5.0.0","published":"2026-04-10T18:22:59Z"}
gh api repos/actions/deploy-pages/releases/latest --jq '{tag: .tag_name, published: .published_at}'
  -> {"tag":"v5.0.1","published":"2026-09-01T21:31:19Z"}
```
`[VERIFIED: GitHub API, this session]` — these are current as of the research date; re-check at implementation time if execution is delayed, per this repository's own "look up the current stable version before a bump" rule.

## Package Legitimacy Audit

This phase installs no npm/PyPI/crates packages. The only new external dependencies are three GitHub Actions, all published by the `actions` GitHub organization — the same org that already publishes `actions/checkout`, pinned twice in this repository's existing workflows.

| Package | Registry | Source Repo | Verdict | Disposition |
|---------|----------|-------------|---------|-------------|
| `actions/configure-pages` | GitHub Actions | github.com/actions/configure-pages | OK | Approved — official `actions` org, verified via GitHub API this session |
| `actions/upload-pages-artifact` | GitHub Actions | github.com/actions/upload-pages-artifact | OK | Approved — official `actions` org, verified via GitHub API this session |
| `actions/deploy-pages` | GitHub Actions | github.com/actions/deploy-pages | OK | Approved — official `actions` org, verified via GitHub API this session |

**Packages removed due to [SLOP] verdict:** none.
**Packages flagged as suspicious [SUS]:** none.

The `gsd_run query package-legitimacy check` seam targets npm/PyPI/crates ecosystems and does not have a GitHub-Actions mode; the verdicts above were formed by directly querying the GitHub API for the owning org, release cadence, and tag-to-commit resolution (all three commands and outputs are in the Standard Stack section above), which is the ecosystem-appropriate verification this action type has.

## Architecture Patterns

### System Architecture Diagram

```
Maintainer's machine
  |
  |  git push origin v<version>   (D-13: manual, blocking-human checkpoint)
  v
[ release.yml ]  (tag push trigger, unchanged by this phase)
  |  1. check-tag (scripts/package.sh check-tag)
  |  2. release-gate.sh check (ci-success on the tagged commit)
  |  3. mise run test
  |  4. mise run package
  |       -> scripts/package.sh build
  |            -> reads CHANGELOG.md's dated section for this version (D-06/D-09, NEW)
  |            -> zip + meta.json + a SINGLE-version manifest.json
  |  5. gh release create <tag> *.zip manifest.json --generate-notes
  |       -> a GitHub Release now holds: the zip, and THIS release's own manifest.json
  |
  |  workflow_run (types: [completed], on Release workflow)   <- D-03, NEW
  v
[ pages.yml ]  (NEW workflow; also workflow_dispatch)
  |  guard: github.event.workflow_run.conclusion == 'success'
  |  1. mise run manifest -> scripts/manifest.sh rebuild        <- D-01/D-02/D-04, NEW
  |       -> gh api repos/$GH_REPO/releases --paginate          (every release, no 30-item cap)
  |       -> for each release tag:
  |            gh release download <tag> --pattern 'manifest.json' --dir <scratch>/<tag>
  |            gh release download <tag> --pattern '*.zip'      --dir <scratch>/<tag>
  |            openssl dgst -md5 -r <zip>  ==  <manifest's recorded checksum>  ? else FAIL LOUD
  |       -> jq: merge every release's VersionInfo into one PackageInfo[] entry
  |       -> writes the combined manifest.json
  |  2. actions/configure-pages, actions/upload-pages-artifact, actions/deploy-pages
  |       -> publishes to https://<owner>.github.io/<repo>/manifest.json
  |  3. smoke check (D-05, NEW): curl the just-published URL, re-run the checksum
  |       verification against the LIVE document, fail the workflow if it disagrees
  v
GitHub Pages (static CDN, HTTPS enforced, Content-Type: application/json for *.json files)


Administrator's Jellyfin 12.1 server (separate machine, unrelated to the above)
  |
  |  Dashboard > Plugins > Repositories > Add Repository > paste manifest URL
  v
[ Jellyfin InstallationManager ]
  |  GET manifest.json  (plain HttpClient.GetFromJsonAsync<PackageInfo[]>, NO auth headers)
  |  filters versions[] by Version.Parse(targetAbi) <= server's own version (floor, not ceiling)
  |
  |  administrator clicks Install (or Jellyfin's own update check fires)
  v
  POST /Packages/Installed/{name}?version=&repositoryUrl=
  |  GetCompatibleVersions -> InstallPackage
  |  downloads the zip from sourceUrl, computes MD5, compares case-insensitively to checksum
  |  extracts DLL + meta.json into /config/plugins/EmbyAuth_<version>/
  v
  Jellyfin restart -> plugin loads
```

### Recommended Project Structure

```
scripts/
  manifest.sh              # NEW — rebuild action(s); required action argument per repo convention
  package.sh                # existing — gains changelog_entry() and PACKAGE_VERSION override
tests/scripts/
  manifest.bats             # NEW — fake `gh` on PATH, same seam as release-gate.bats
  package.bats               # existing — gains changelog and PACKAGE_VERSION assertions
.github/workflows/
  pages.yml                  # NEW — workflow_run + workflow_dispatch, builds and deploys the manifest
  release.yml                 # existing — unchanged trigger surface; only package.sh's output changes
e2e/
  NN-catalog-install.bats     # NEW — nginx-served manifest + zips, a dedicated clean Jellyfin service
```

### Pattern 1: Enumerate every release without the default 30-item cap

**What:** `gh release list` defaults to `-L 30` (confirmed this session via `gh release list --help`: `-L, --limit int  Maximum number of items to fetch (default 30)`). A manifest rebuild that must include *every* released version (D-01, D-10) cannot use this command's default form — once the repository accumulates more than 30 releases, the oldest ones would silently vanish from the manifest with no error. Existing scripts in this repository already prefer `gh api ... --paginate` for exactly this reason (`release-gate.sh` uses `gh api`, not a higher-level `gh` subcommand).

**When to use:** Any enumeration that must be exhaustive, not just the enumeration in `scripts/manifest.sh`.

**Example:**
```bash
# Source: gh CLI, verified this session (`gh release list --help`, `gh api --help`)
tags="$(gh api "repos/$GH_REPO/releases" --paginate --jq \
  '[.[] | select(.draft == false)] | sort_by(.tag_name) | .[].tag_name')"
```

### Pattern 2: Per-release scratch directories, because every release's asset is named `manifest.json`

**What:** `release.yml:64` runs `gh release create "$TAG" artifacts/release/*.zip artifacts/release/manifest.json ...` — so every single release's manifest asset is literally named `manifest.json`, not `manifest-v1.0.0.0.json`. Downloading two releases' assets into the same directory overwrites the first with the second.

**When to use:** `scripts/manifest.sh`'s rebuild loop.

**Example:**
```bash
# Source: gh CLI, verified this session (`gh release download --help`)
for tag in $tags; do
  dir="$SCRATCH_DIR/$tag"
  mkdir -p "$dir"
  gh release download "$tag" --dir "$dir" --pattern 'manifest.json' --clobber
  gh release download "$tag" --dir "$dir" --pattern '*.zip' --clobber
done
```

### Pattern 3: Re-verify the checksum against the downloaded zip before trusting the recorded value (D-02, D-05)

**What:** Never fold a release's recorded `checksum` into the combined manifest without recomputing it from the actual downloaded bytes. This is the exact check Jellyfin's server performs (`Convert.ToHexString(MD5.HashData(stream))`, case-insensitive `string.Equals` against `VersionInfo.Checksum` — `[VERIFIED: jellyfin/jellyfin, Emby.Server.Implementations/Updates/InstallationManager.cs:586-594, tag v12.1]`, quoted below), so recomputing it at publish time (and again post-deploy per D-05) catches a corrupted upload before an administrator's Jellyfin server does.

```csharp
// Source: jellyfin/jellyfin, tag v12.1, Emby.Server.Implementations/Updates/InstallationManager.cs:586-594
var hash = Convert.ToHexString(await MD5.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
if (!string.Equals(package.Checksum, hash, StringComparison.OrdinalIgnoreCase))
{
    ...
    throw new InvalidDataException("The checksum of the received data doesn't match.");
}
```

**Example (shell side, matching `scripts/package.sh:83`'s existing invocation):**
```bash
# Source: scripts/package.sh:83, this repository, unchanged pattern
checksum="$(openssl dgst -md5 -r "$zip" | cut -d' ' -f1)"
recorded="$(jq -r '.[0].versions[0].checksum' "$dir/manifest.json")"
if [[ "$checksum" != "$recorded" ]]; then
    echo "Checksum mismatch for $tag: the downloaded zip hashes to $checksum but its manifest.json records $recorded." >&2
    exit 1
fi
```

### Pattern 4: Extract a `CHANGELOG.md` section verbatim (D-06, D-08)

**What:** Grab everything between a `## [<version>] - <date>` heading and the next `## ` heading, unmodified, so it can be asserted byte-for-byte equal to the manifest's `changelog` field.

**Example:**
```bash
# Illustrative pattern for the planner to adapt — the exact parser is Claude's Discretion (06-CONTEXT.md)
changelog_entry() {
	local version="$1" changelog_path="${CHANGELOG_PATH:-$REPO_ROOT/CHANGELOG.md}"
	awk -v ver="$version" '
		$0 ~ "^## \\[" ver "\\] - " { found=1; next }
		found && /^## / { exit }
		found { print }
	' "$changelog_path"
}
```
This intentionally does not trim leading/trailing blank lines — D-08 requires the section body verbatim, and a test should assert the exact captured text rather than a normalized version of it.

### Pattern 5: The GitHub Pages deploy job shape

**What:** The three actions have a fixed collaboration contract: `configure-pages` first (detects the site), `upload-pages-artifact` packages a directory, `deploy-pages` publishes it. The deploy job needs `pages: write` and `id-token: write`, and should target the `github-pages` environment so the deployment URL surfaces as a job output.

```yaml
# Source: docs.github.com/en/pages/getting-started-with-github-pages/using-custom-workflows-with-github-pages
# and github.com/actions/deploy-pages README, both read this session; SHAs verified via gh api (see Standard Stack)
permissions:
  contents: read
  pages: write
  id-token: write

jobs:
  build:
    # ... checkout, mise-action, `mise run manifest` (produces artifacts/release/manifest.json or similar)
    steps:
      - name: Upload manifest artifact
        uses: actions/upload-pages-artifact@fc324d3547104276b827a68afc52ff2a11cc49c9 # v5.0.0
        with:
          path: <the directory containing manifest.json>

  deploy:
    needs: build
    environment:
      name: github-pages
      url: ${{ steps.deployment.outputs.page_url }}
    steps:
      - name: Setup Pages
        uses: actions/configure-pages@45bfe0192ca1faeb007ade9deae92b16b8254a0d # v6.0.0
      - name: Deploy to GitHub Pages
        id: deployment
        uses: actions/deploy-pages@368f82528645a54fb793d4d04e342629a3f51346 # v5.0.1
```
`configure-pages` is typically run before `upload-pages-artifact` in the same build job (both docs examples above show this ordering); splitting `build` and `deploy` into two jobs, as this repository's own convention for named, single-purpose jobs suggests, is compatible as long as `deploy` declares `needs: build`.

### Anti-Patterns to Avoid

- **Calling `gh api -X POST/PUT /repos/{owner}/{repo}/pages` with `${{ github.token }}` inside the Pages workflow to enable the Actions build type:** GitHub's fine-grained-PAT permission reference lists this endpoint under the **Administration** repository permission, which is not one of the permissions a workflow's `GITHUB_TOKEN` can be granted (the grantable list is `actions`, `attestations`, `checks`, `contents`, `deployments`, `id-token`, `issues`, `discussions`, `packages`, `pages`, `pull-requests`, `repository-projects`, `security-events`, `statuses`, plus a few org-level permissions — no `administration`). `[CITED: docs.github.com/en/rest/authentication/permissions-required-for-fine-grained-personal-access-tokens]` This is a one-time setup step the maintainer must do through the Settings UI or their own authenticated `gh`, not something to script into the workflow.
- **Trusting a release's recorded `checksum` field without recomputing it:** defeats the entire point of D-02/D-05.
- **Using `gh release list` without `--paginate`/a high `--limit` in the rebuild script:** silently drops old versions once the release count exceeds 30 (Pitfall 1, Pattern 1 above).
- **Downloading two releases' `manifest.json` assets into the same directory:** the second overwrites the first (Pattern 2 above).

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Deploying a static file to GitHub Pages from a workflow | A hand-rolled `git worktree`/orphan-branch push, or a raw REST call sequence to the Pages Deployments API | `actions/configure-pages` + `actions/upload-pages-artifact` + `actions/deploy-pages` | These three actions already handle artifact packaging (tar+gzip, size limits), OIDC-based deployment authentication, and status polling; re-implementing the polling/timeout logic (`deploy-pages` has `timeout`, `error_count`, `reporting_interval` inputs) is exactly the kind of thing this repository's "no premature abstraction" rule warns against building without a second consumer |
| MD5 checksum computation | A custom hash routine, a language-runtime hash library pulled in just for this | `openssl dgst -md5 -r`, the exact command `scripts/package.sh:83` already uses | One tool, already pinned indirectly via the OS, already proven correct in this repository — no reason to introduce a second implementation that could disagree with the first |
| Multi-version manifest merge | A stateful "append to an existing manifest.json" script | The stateless full-rebuild-from-releases-API approach (D-01) | A stateful append needs the manifest to be read back from somewhere (the Pages site itself, or a cached artifact) and can drift from the actual set of releases with nothing to catch it; a full rebuild is idempotent and self-correcting by construction |

**Key insight:** every piece of this phase that touches Jellyfin's install path (the manifest schema, the checksum algorithm, the `targetAbi` comparison) is dictated by C# code this plugin does not control — there is no room for a "close enough" reimplementation, because Jellyfin's own deserializer and checksum comparison are exact and were read from source this session.

## Runtime State Inventory

Not applicable — no rename, refactor, or migration is in scope for this phase. Every change is either new (`scripts/manifest.sh`, `pages.yml`, the catalog e2e file) or additive (the changelog wiring, the `PACKAGE_VERSION` override, the two version bumps). Nothing renames an existing identifier, key, or path that a running server or external service already depends on. **Confirmed by reading the phase's Files-Changed list in 06-CONTEXT.md** — every entry there is an addition to an existing file's behavior, not a rename.

## Common Pitfalls

### Pitfall 1: `gh release list`'s default 30-item limit silently truncates the manifest

**What goes wrong:** Once the repository has more than 30 releases, a manifest rebuild written against `gh release list` (rather than `gh api ... --paginate`) stops including the oldest releases, with no error — the command simply returns fewer results than exist.
**Why it happens:** `gh release list`'s `-L`/`--limit` flag defaults to `30`. `[VERIFIED: gh CLI --help output, this session: "-L, --limit int  Maximum number of items to fetch (default 30)"]`
**How to avoid:** Use `gh api repos/$GH_REPO/releases --paginate` (as `release-gate.sh` already does for other endpoints), not `gh release list`.
**Warning signs:** The manifest's `versions[]` array is shorter than `gh release list --limit 1000` (or the GitHub UI's release count) once past 30 releases. Not observable during this phase (the repository will have exactly two releases), but worth a comment in `scripts/manifest.sh` so a future contributor does not "simplify" the enumeration to the higher-level command.

### Pitfall 2: A wrong Content-Type on the manifest produces a misleading Jellyfin log message

**What goes wrong:** If the manifest is ever served with a Content-Type other than `application/json` (or a `+json` suffix), .NET's `GetFromJsonAsync<PackageInfo[]>` throws `NotSupportedException`, and `InstallationManager.GetPackages`'s catch block for that exception type logs **"The URL scheme configured for the plugin repository is not supported"** — a message about the URL scheme, not the content type, that would send anyone debugging a real content-type problem in the wrong direction.
**Why it happens:** `System.Net.Http.Json`'s `ReadFromJsonAsync` validates the response's media type before attempting to deserialize (`HttpContentJsonExtensions.ValidateContent`), and throws the same exception type (`NotSupportedException`) for both an unsupported URI scheme and an unsupported content type — the two failure modes share one catch block in Jellyfin's source. `[CITED: github.com/dotnet/aspnetcore issue #27797 and a StackOverflow reproduction, both showing the exact exception text "The provided ContentType is not supported; the supported types are 'application/json' and the structured syntax suffix 'application/+json'."; VERIFIED against InstallationManager.cs's catch(NotSupportedException) block, tag v12.1, this session]`
**How to avoid:** In this phase's case the risk is low — GitHub Pages generates its MIME type table from the `mime-db` project, which maps the `.json` extension to `application/json` `[CITED: docs.github.com "About GitHub Pages" — MIME types section]`, and the manifest file will be named literally `manifest.json`, so the default behavior is already correct with no extra configuration (no custom `_headers` file, no Jekyll front matter needed). Do not rename the published file to something without a recognized extension.
**Warning signs:** A Jellyfin log line reading "The URL scheme configured for the plugin repository is not supported" for a URL whose scheme is plainly `https://` — that message is the signal to check Content-Type, not the URL.

### Pitfall 3: `workflow_run` only fires from the default branch, and possibly not on its very first eligible completion

**What goes wrong:** A `workflow_run`-triggered workflow (`pages.yml`, watching `Release`) is evaluated using the **version of the trigger definition on the default branch**, not on whatever branch it was authored on — merging a PR that adds `pages.yml` does not make it live until that merge lands on `main`. Separately, some community reports describe the very first upstream completion after the trigger workflow is merged not firing, requiring either a manual dispatch or a second upstream run.
**Why it happens:** Documented (if tersely) GitHub behavior: `[CITED: docs.github.com/en/actions/reference/events-that-trigger-workflows, github/docs issue #14205 (GitHub staff confirmation: "Your triggered workflow ... must be on the default branch")]`. The "first-run" non-firing is **not** in official docs — it appears only in community bug reports (e.g. a GitHub issue describing `chain-image.yml` not firing on the first `scrape-pack` completion after being added) `[CITED: community report, unverified against this repository — treat as a possibility to watch for, not a confirmed mechanism]`.
**How to avoid:** This is exactly why D-15 sequences "the Pages workflow lands and is proven first" before `v0.9.0.0` is tagged — merge `pages.yml` to `main` well before the rehearsal tag, and treat the rehearsal's Pages-workflow trigger as something to *observe firing*, not assume from documentation. If it does not fire on the first `v0.9.0.0` release, the `workflow_dispatch` fallback (already in D-03) is the immediate recovery, and a second tagged release (D-14's fix-forward path) is the natural second observation.
**Warning signs:** `gh run list --workflow=pages.yml` shows no run after `release.yml` completes for `v0.9.0.0`.

### Pitfall 4: The workflow's own `GITHUB_TOKEN` most likely cannot switch the Pages build type to "GitHub Actions"

**What goes wrong:** A `gh api -X POST /repos/{owner}/{repo}/pages -f build_type=workflow` step placed inside `pages.yml`, authenticated with `${{ github.token }}`, would be expected to fail with a 403, because this endpoint requires the **Administration** repository permission.
**Why it happens:** GitHub's fine-grained-PAT permission tables place `POST`/`PUT`/`DELETE /repos/{owner}/{repo}/pages` under "Administration," and the standard list of permissions a workflow's `GITHUB_TOKEN` can be granted (via the `permissions:` key) does not include an `administration` scope at all. `[CITED: docs.github.com/en/rest/authentication/permissions-required-for-fine-grained-personal-access-tokens]` This claim is an inference from two separately-documented facts (the endpoint's required PAT permission, and the absence of that permission name from the `GITHUB_TOKEN` grantable list) rather than a direct test against this repository's own `GITHUB_TOKEN`, since the repository is not yet public and Pages cannot be exercised end to end yet — flagged in the Assumptions Log below.
**How to avoid:** Treat "switch GitHub Pages to the Actions build type" as a one-time, maintainer-performed setup step (Settings UI, or a `gh api` call the maintainer runs personally with their own broader-scoped `gh` authentication) — the same shape as Phase 5's visibility change and this phase's own tag pushes. Do not spend a task trying to automate it from inside `pages.yml`.
**Warning signs:** The first `pages.yml` run's `deploy-pages` step fails outright (not a checksum or content mismatch, but a deployment-target error) because no Pages site of build type `workflow` exists yet.

### Pitfall 5: `PACKAGE_VERSION` is a real-release footgun if it ever leaks into a non-test invocation

**What goes wrong:** If `PACKAGE_VERSION` were accidentally set during a real `mise run package` (rather than only inside the new e2e test's setup), the produced zip's `meta.json` and manifest entry would report a version that disagrees with the DLL's actual embedded `AssemblyVersion` (still sourced from `Directory.Build.props`).
**Why it happens:** D-17 deliberately adds this override as a version-only seam; it does not touch `Directory.Build.props` or the compiled assembly.
**How to avoid:** Document `PACKAGE_VERSION` as test-only in `scripts/package.sh`'s `# Environment:` header comment, exactly as 06-CONTEXT.md's D-17 already specifies, and never reference it from `release.yml` or `docs/development.md`'s release procedure.
**Warning signs:** A shipped release whose `meta.json` version does not match its DLL's `AssemblyVersion` — not reachable if the override is only ever set inside the new e2e test file.

## Code Examples

### Extracting a dated `CHANGELOG.md` section, refusing when absent (D-06, D-09)

```bash
# Illustrative — exact parser and refusal wording are Claude's Discretion.
# Pattern follows this repository's existing small-single-purpose-helper style
# (see plugin_version(), target_abi() in scripts/package.sh).
changelog_entry() {
	local version="$1" changelog_path="${CHANGELOG_PATH:-$REPO_ROOT/CHANGELOG.md}"
	local entry
	entry="$(awk -v ver="$version" '
		$0 ~ "^## \\[" ver "\\] - " { found=1; next }
		found && /^## / { exit }
		found { print }
	' "$changelog_path")"

	if [[ -z "$entry" ]]; then
		echo "CHANGELOG.md has no dated section for version $version. Add \"## [$version] - <date>\" before building." >&2
		return 1
	fi
	printf '%s' "$entry"
}
```

### Rebuilding the multi-version manifest with checksum re-verification (D-01, D-02)

```bash
# Illustrative — exact action name and jq shape are Claude's Discretion (06-CONTEXT.md).
rebuild() {
	local scratch tags combined="[]"
	scratch="$(mktemp -d)"
	trap 'rm -rf "$scratch"' RETURN

	tags="$(gh api "repos/$GH_REPO/releases" --paginate --jq \
		'[.[] | select(.draft == false)] | sort_by(.tag_name) | .[].tag_name')"

	for tag in $tags; do
		local dir="$scratch/$tag" zip checksum recorded
		mkdir -p "$dir"
		gh release download "$tag" --dir "$dir" --pattern 'manifest.json' --clobber
		gh release download "$tag" --dir "$dir" --pattern '*.zip' --clobber

		zip="$(find "$dir" -name '*.zip' -print -quit)"
		checksum="$(openssl dgst -md5 -r "$zip" | cut -d' ' -f1)"
		recorded="$(jq -r '.[0].versions[0].checksum' "$dir/manifest.json")"
		if [[ "$checksum" != "$recorded" ]]; then
			echo "Checksum mismatch for $tag: zip hashes to $checksum, manifest.json says $recorded." >&2
			return 1
		fi

		combined="$(jq -s '
			.[0] as $acc | .[1] as $entry |
			if ($acc | length) == 0 then [$entry[0]]
			else $acc | (.[0].versions) += $entry[0].versions
			end
		' <(printf '%s' "$combined") "$dir/manifest.json")"
	done

	echo "$combined" | jq 'sort_by(.guid) | map(.versions |= sort_by(.version))' >"$OUTPUT_DIR/manifest.json"
}
```
This grows the first (and, for this plugin, only) `PackageInfo` entry's `versions[]` array by one element per release; a second plugin entry would need to be matched and merged by `guid` rather than assumed to be index `0` — not a concern for this single-plugin repository but worth a comment if the pattern is ever reused.

### The verified `PackageInfo`/`VersionInfo` schema (top-level array)

```csharp
// Source: jellyfin/jellyfin, tag v12.1, MediaBrowser.Model/Updates/PackageInfo.cs (verified this session)
public class PackageInfo
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("description")] public string Description { get; set; }
    [JsonPropertyName("overview")] public string Overview { get; set; }
    [JsonPropertyName("owner")] public string Owner { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; }
    [JsonPropertyName("guid")] public Guid Id { get; set; }
    [JsonPropertyName("versions")] public IList<VersionInfo> Versions { get; set; }
    [JsonPropertyName("imageUrl")] public string? ImageUrl { get; set; }
}

// Source: jellyfin/jellyfin, tag v12.1, MediaBrowser.Model/Updates/VersionInfo.cs (verified this session)
public class VersionInfo
{
    [JsonPropertyName("version")] public string Version { get; set; }       // parsed via System.Version.Parse — must be a valid Version string
    [JsonPropertyName("changelog")] public string? Changelog { get; set; }  // plain string, no format enforced server-side
    [JsonPropertyName("targetAbi")] public string? TargetAbi { get; set; }
    [JsonPropertyName("sourceUrl")] public string? SourceUrl { get; set; }
    [JsonPropertyName("checksum")] public string? Checksum { get; set; }
    [JsonPropertyName("timestamp")] public string? Timestamp { get; set; }  // NOTE: string, not DateTime — no parsing/format is enforced
    // RepositoryName / RepositoryUrl are OVERWRITTEN by Jellyfin itself after fetch
    // (InstallationManager.GetPackages, lines 123-124) — do not bother setting them.
}
```
And the top-level fetch call, confirming the manifest is a **bare JSON array**, not an object wrapping an array:
```csharp
// Source: jellyfin/jellyfin, tag v12.1, Emby.Server.Implementations/Updates/InstallationManager.cs:108-109
PackageInfo[]? packages = await _httpClientFactory.CreateClient(NamedClient.Default)
        .GetFromJsonAsync<PackageInfo[]>(new Uri(manifest), _jsonSerializerOptions, cancellationToken)
        .ConfigureAwait(false);
```
This matches `scripts/package.sh`'s existing single-version manifest shape exactly (`jq -n '[{...}]'` — an array with one `PackageInfo` object), so the rebuild's output format is a drop-in continuation of the existing convention, just with `versions[]` grown to hold every release instead of one.

**A field that is easy to get wrong by analogy with `meta.json`:** `PluginManifest.Timestamp` (used in `meta.json`, the file inside the zip) is a `DateTime`. `VersionInfo.Timestamp` (used in the repository manifest) is a `string?` with no parsing at all — `[VERIFIED: both classes read from jellyfin/jellyfin tag v12.1 this session]`. `scripts/package.sh`'s existing manifest `jq` call already handles this correctly (it copies the same ISO-8601 string into both places, and since `VersionInfo.Timestamp` is an unparsed string, any consistent format works there — the constraint is only on `meta.json`'s field).

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|---------------|--------|
| Per-release single-version `manifest.json` attached only to that GitHub Release | One rebuilt multi-version `manifest.json` published to GitHub Pages | This phase (D-01) | The per-release asset does not go away — D-02 makes it the *input* to the rebuild, not a competing artifact administrators might paste by mistake |
| `actions/deploy-pages@v3` / `@v4` (seen in some third-party examples and older official-docs snippets) | `actions/deploy-pages@v5.0.1` | Verified current this session | Same permission/environment contract across major versions per the official docs; no migration concern beyond pinning the current SHA |
| `## Unreleased` as the only `CHANGELOG.md` section | Dated `## [<version>] - <date>` sections, with a fresh `## Unreleased` re-added at release time | This phase (D-07) | Makes the changelog machine-extractable (D-06) without changing its existing keepachangelog-style subsection structure |

**Deprecated/outdated:** nothing in this phase's stack is deprecated; all three GitHub Actions pins and the Jellyfin manifest schema are the current, actively maintained forms.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | The default `GITHUB_TOKEN` (even with every grantable permission scope set to `write`) cannot successfully call `POST`/`PUT /repos/{owner}/{repo}/pages` to set `build_type: workflow`, because that endpoint needs the Administration permission, which is absent from the grantable list | Pitfall 4, Anti-Patterns | If wrong, a task that tries to script this step would simply fail at execution time with a 403 and be redirected to the manual path anyway — low risk, self-correcting, but worth a `checkpoint:human-verify` or an accepted "try it, fall back to manual" task order rather than asserting the manual path is mandatory up front |
| A2 | GitHub's "workflow_run only fires from the default branch" first-completion caveat (some community reports say the very first eligible completion after merge does not fire) applies to this repository too | Pitfall 3 | If the Pages workflow simply works on the very first `v0.9.0.0` release, this assumption cost nothing; if it does not fire, the rehearsal (D-16) is exactly the safety net designed to catch it, and `workflow_dispatch` is the documented fallback (D-03) |
| A3 | Jellyfin's web dashboard renders the manifest's `changelog` field as plain text rather than parsed Markdown (so the `###` subheadings in a verbatim-extracted section degrade to plain lines rather than rendering oddly) | D-08 (carried from 06-CONTEXT.md, restated here since it affects whether Pattern 4's verbatim extraction is safe to ship as-is) | If wrong (i.e., if the dashboard actually renders raw `##`/`###` characters literally, cluttering the catalog UI), the fix is cosmetic — trim the changelog to its bullet lines before D-06 writes it — not a re-architecture; 06-CONTEXT.md already names the `v0.9.0.0` rehearsal as the moment to look at this on a real server |

## Open Questions

1. **Does the very first `pages.yml` run actually fire after `v0.9.0.0`'s `release.yml` completes?**
   - What we know: `workflow_run` requires the trigger workflow to be merged to the default branch first (documented, confirmed by GitHub staff in a public issue thread); a "does not fire on the very first eligible completion" behavior is reported by at least one third party but is not in official documentation.
   - What's unclear: whether this repository will hit that specific edge case.
   - Recommendation: treat the rehearsal's first Pages-workflow run as something to *watch fire* (`gh run list --workflow=pages.yml`) immediately after the `v0.9.0.0` tag push, with `workflow_dispatch` as the documented, already-decided fallback (D-03) if it does not.

2. **Does Jellyfin's dashboard render the manifest `changelog` field as Markdown or plain text?**
   - What we know: `VersionInfo.Changelog` is an unconstrained `string?`; nothing in the deserialization path parses it as Markdown.
   - What's unclear: the dashboard's own rendering behavior, which is client-side JavaScript this research did not trace.
   - Recommendation: 06-CONTEXT.md already assigns this to the `v0.9.0.0` rehearsal (D-08). No planning action needed beyond keeping that observation step in the plan.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| `gh` CLI | `scripts/manifest.sh`, `scripts/release-gate.sh` (existing), the two tag-push checkpoints | ✓ | 2.97.0 (local); GitHub-hosted runners ship a current `gh` preinstalled | — |
| `jq` | `scripts/manifest.sh`, `scripts/package.sh` | ✓ | 1.8.2, pinned in `.mise.toml` | — |
| `openssl` | checksum computation | ✓ | system tool, already used at `scripts/package.sh:83` | — |
| Docker | the new catalog-install e2e file, `mise run e2e` | ✓ (existing e2e suite already depends on it) | — | — |
| `nginx:1.30.5-alpine` | serving the manifest + zips in the new e2e file | ✓ (already pulled for `emby-proxy`) | 1.30.5-alpine | — |
| GitHub Pages (external service) | PUB-02, PUB-04 | ✗ — not yet enabled (`gh api repos/:owner/:repo/pages` returns 404 as of this session) | — | None; this is a hard, maintainer-performed prerequisite (Pitfall 4) — no task in this phase can substitute for it |
| Public repository visibility | everything in this phase | ✗ — repository is `PRIVATE` as of this session | — | None; Phase 5's D-15 reserves this for the maintainer, and 06-CONTEXT.md states execution is blocked until it happens |

**Missing dependencies with no fallback:**
- GitHub Pages must be created and switched to the `workflow` build type by the maintainer before `pages.yml`'s first run can succeed.
- The repository must be public before any release, Pages deploy, or catalog install in this phase can be exercised for real (the hermetic e2e test in D-16 is the one part of this phase that works regardless of visibility, since it uses a local nginx rather than the live URL).

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | bats 1.14.0 (shell/script and e2e tests); xUnit v3 (unrelated to this phase); node:test (unrelated to this phase) |
| Config file | none — bats needs none; `.mise.toml` pins the binary |
| Quick run command | `bats tests/scripts/manifest.bats` (new), `bats tests/scripts/package.bats` (extended) |
| Full suite command | `mise run test` (unit + script + JS) and `mise run e2e` (Docker-based, includes the new catalog file) |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| PUB-02 | `scripts/manifest.sh` rebuild produces a schema-correct, checksum-verified, every-version manifest | unit/script | `bats tests/scripts/manifest.bats` | ❌ Wave 0 — new file |
| REL-04 | `scripts/package.sh build` writes the exact `CHANGELOG.md` section into `meta.json` and the single-version manifest; refuses an undated version | unit/script | `bats tests/scripts/package.bats` | ✅ exists, needs new `@test` cases |
| DOCS-02 | README states the manifest URL and install/update steps | manual-only | — | manual-only; prose accuracy is human-reviewed, consistent with how DOCS-01/03/05 shipped in earlier phases with no dedicated automated test |
| DOCS-04 | `CLAUDE.md`'s pin rule names every pin | manual-only | — | manual-only; prose, same justification as DOCS-02 |
| PUB-04 | Catalog install then update, end to end, against a hermetic local manifest+zip host | e2e | `bats e2e/NN-catalog-install.bats` (also runs under `mise run e2e`) | ❌ Wave 0 — new file, new compose services |
| PUB-05 | v1.0.0.0 tagged and released | manual-only, post-execution | `gh release list` (verification, not a test) | not applicable — closed by the maintainer's own tag push (D-13), outside anything an execution run can automate |

### Sampling Rate
- **Per task commit:** `bats tests/scripts/manifest.bats` and/or `bats tests/scripts/package.bats`, whichever the task touches
- **Per wave merge:** `mise run test` (fast) and `mise run e2e` (Docker, slower — required by `.claude/rules/plugin.md`'s "a change that depends on Jellyfin or Emby behavior needs an end-to-end test")
- **Phase gate:** Full suite (`mise run lint`, `mise run test`, `mise run e2e`) green before either tag push checkpoint, matching the existing `release-gate.sh` posture

### Wave 0 Gaps
- [ ] `tests/scripts/manifest.bats` — covers PUB-02, with a fake `gh` on `PATH` per the D-04 seam
- [ ] `e2e/NN-catalog-install.bats` — covers PUB-04, plus its compose additions (a manifest-serving nginx, a clean Jellyfin service)
- [ ] `tests/scripts/package.bats` additions — covers REL-04's changelog wiring and D-09's refusal, and D-17's `PACKAGE_VERSION` override behavior
- Framework install: none — bats, jq, gh, openssl, nginx, and Docker are all already available per the Environment Availability table above

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | This phase adds no authentication surface; the manifest fetch is deliberately unauthenticated (Jellyfin's own requirement) |
| V3 Session Management | no | Not applicable |
| V4 Access Control | partial | GitHub Actions least-privilege `permissions:` blocks on the new `pages.yml` (contents: read, pages: write, id-token: write only — no `contents: write` anywhere in this phase, preserving Phase 5's D-08 zizmor posture) |
| V5 Input Validation | yes | Every value written into the combined manifest's JSON must go through `jq --arg`/`--argjson` (never raw string interpolation into a JSON literal), matching `scripts/package.sh`'s existing pattern, so a changelog entry containing a `"` or backslash cannot corrupt the JSON structure |
| V6 Cryptography | partial, externally constrained | MD5 is cryptographically broken for collision resistance, but this is Jellyfin's own fixed mechanism (`InstallationManager.cs` hard-codes `MD5.HashData`), not a choice this plugin's tooling makes — flagged here as an accepted external constraint, not a defect to fix. The checksum's actual security purpose in this flow is integrity-against-corruption (a bad upload, a truncated download), not integrity-against-a-deliberate-attacker who already controls the release asset — an attacker who can replace the zip can also replace the recorded checksum. |
| V14 Configuration/CI-CD | yes | `persist-credentials: false` on every checkout (already the established pattern); `zizmor --persona=pedantic` clean on the new `pages.yml` (named job, documented `permissions:` block, `concurrency:` setting — Phase 5's standing gate); no workflow in this phase gains `contents: write` |

### Known Threat Patterns for this stack

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| A manifest served over a non-HTTPS or attacker-controlled endpoint | Tampering / Spoofing | GitHub Pages enforces HTTPS unconditionally for `*.github.io` URLs (no custom domain is in scope for this phase); README (DOCS-02) states the canonical `https://` URL so an administrator does not paste a look-alike |
| A corrupted or tampered release asset reaching the manifest unnoticed | Tampering | D-02/D-05's checksum re-verification, both at publish time and post-deploy |
| Secret leakage through workflow logs | Information Disclosure | No new secret is introduced by this phase; `GH_TOKEN: ${{ github.token }}` is the only credential used, already the established pattern, never echoed |
| Workflow token over-permissioned, then abused if the workflow definition is ever compromised via a malicious PR | Elevation of Privilege | Least-privilege `permissions:` block scoped per job (matching `release.yml`'s and `ci.yml`'s existing pattern of naming exactly why each permission is needed in a comment) |

## Sources

### Primary (HIGH confidence)

- `jellyfin/jellyfin`, tag `v12.1` — `MediaBrowser.Model/Updates/PackageInfo.cs`, `MediaBrowser.Model/Updates/VersionInfo.cs`, `MediaBrowser.Common/Plugins/PluginManifest.cs`, `Emby.Server.Implementations/Updates/InstallationManager.cs` (lines 104-172 for `GetPackages`, 586-594 for checksum comparison) — all read via `gh api repos/jellyfin/jellyfin/contents/...?ref=v12.1` this session
- GitHub REST API, queried this session via `gh api` — `actions/configure-pages`, `actions/upload-pages-artifact`, `actions/deploy-pages` release tags and their commit SHAs
- `gh` CLI, this session's `--help` output — `gh release list` (30-item default), `gh release download` (`--pattern`, `--dir`, `--clobber`)
- This repository, read this session — `scripts/package.sh`, `tests/scripts/package.bats`, `scripts/release-gate.sh`, `tests/scripts/release-gate.bats`, `.github/workflows/release.yml`, `.github/workflows/ci.yml`, `.mise.toml`, `CHANGELOG.md`, `Directory.Build.props`, `README.md`, `docs/development.md`, `CLAUDE.md`, `e2e/compose.yaml`, `e2e/helpers.bash`

### Secondary (MEDIUM confidence)

- docs.github.com — "Using custom workflows with GitHub Pages", "Events that trigger workflows" (`workflow_run`), "Permissions required for fine-grained personal access tokens", "REST API endpoints for GitHub Pages", "About GitHub Pages" (MIME types section)
- github.com/actions/deploy-pages README — permissions and environment requirements, confirmed consistent with the official docs above
- github/docs issue #14205 — GitHub staff confirmation that a `workflow_run` trigger workflow must be on the default branch
- github.com/dotnet/aspnetcore issue #27797, github.com/dotnet/runtime issue #38713 — `GetFromJsonAsync`'s content-type validation behavior and exact exception text

### Tertiary (LOW confidence)

- A community bug report describing a `workflow_run` trigger not firing on its very first eligible completion after being added (Pitfall 3, Open Question 1) — not corroborated by official GitHub documentation; carried as a risk to watch for during the rehearsal, not a confirmed mechanism

## Metadata

**Confidence breakdown:**
- Standard stack (GitHub Actions pins): HIGH — SHAs and tags verified directly via `gh api` this session
- Jellyfin manifest schema: HIGH — read from `jellyfin/jellyfin` source at the exact tag (`v12.1`) this plugin targets
- Pages/`workflow_run` mechanics: HIGH for the documented behavior, MEDIUM for the undocumented "first-run" edge case
- Pitfall 4 (Administration permission blocking `GITHUB_TOKEN`): MEDIUM — a well-supported inference from two separately documented facts, not a direct test against this repository (the repository cannot be tested until it is public)
- Manifest rebuild script shape: MEDIUM — patterns given are illustrative; exact action names and `jq` composition are explicitly Claude's Discretion per 06-CONTEXT.md

**Research date:** 2026-09-21
**Valid until:** 30 days for the GitHub Actions SHAs (re-verify at implementation time per this repository's own version-bump rule); effectively indefinite for the Jellyfin `v12.1`-tagged schema, since this plugin is pinned to that ABI and the schema is read from that exact tag
