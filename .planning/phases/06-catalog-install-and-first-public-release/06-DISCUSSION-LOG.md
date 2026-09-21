# Phase 6: Catalog Install and First Public Release - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-09-21
**Phase:** 6-Catalog Install and First Public Release
**Areas discussed:** Manifest accumulation, Changelog into manifest, The two releases, Proving the install

---

## Area selection

| Option | Description | Selected |
|--------|-------------|----------|
| Manifest accumulation | Where the multi-version manifest lives and how each release gets into it | ✓ |
| Changelog into manifest | REL-04: how a version's CHANGELOG.md section reaches the manifest's changelog field | ✓ |
| The two releases | The 0.9.0.0 rehearsal, the version property churn, and what each changelog says | ✓ |
| Proving the install | PUB-04: manual walkthrough or an automated end-to-end catalog test | ✓ |

**User's choice:** all four.

---

## Manifest accumulation

### How should the Pages manifest accumulate versions?

| Option | Description | Selected |
|--------|-------------|----------|
| Rebuild from releases | The Pages workflow reads every release and concatenates their version entries. No write credentials, and the manifest cannot claim a version that has no release. Testable with a fake `gh` on `PATH`. | ✓ |
| Tracked manifest.json on main | `release.yml` appends with `jq` and pushes the commit back. Every change is a reviewable diff, but it needs `contents: write` plus git credentials the workflow currently refuses, and it can drift from the actual releases. | |
| Orphan gh-pages branch | The release workflow pushes to a `gh-pages` worktree. Same credential cost, and jellyrock needed a GitHub App token because the default `GITHUB_TOKEN` could not push there. | |

**User's choice:** Rebuild from releases.

### What does the rebuild read from each release?

| Option | Description | Selected |
|--------|-------------|----------|
| Asset, checksum verified | Take the entry from the release's attached `manifest.json`, then download the zip and confirm the MD5 matches. A mismatch fails loudly. | ✓ |
| Trust the attached manifest.json | Concatenate the entries as-is. Simplest, no downloads, but a zip replaced after upload would leave a stale checksum with nothing to catch it. | |
| Recompute from the zip | Ignore the manifest asset; read `meta.json` from inside each zip. More moving parts, and the changelog would still have to come from elsewhere. | |

**User's choice:** Asset, checksum verified.
**Notes:** This settles the fate of the per-release `manifest.json` asset — it becomes the rebuild's input rather than something redundant.

### What triggers the Pages workflow?

| Option | Description | Selected |
|--------|-------------|----------|
| workflow_run + dispatch | `on: workflow_run` for the Release workflow, guarded on success, plus `workflow_dispatch`. | ✓ |
| workflow_dispatch only | Publish by hand after each release. No trigger chain to reason about, but a forgotten dispatch strands the catalog on the previous version. | |
| A second job in release.yml | Simplest wiring, but it contradicts PROJECT.md's locked choice of a separate workflow. | |

**User's choice:** workflow_run + dispatch.
**Notes:** Raised before the question and confirmed against GitHub's own documentation in three places: events triggered by the `GITHUB_TOKEN` do not create a new workflow run, with only `workflow_dispatch` and `repository_dispatch` as unconditional exceptions. `release.yml:64` creates the release with `github.token`, so `on: release: published` would never fire. That ruled out the otherwise obvious option before it was offered.

### Where does the rebuild logic live?

| Option | Description | Selected |
|--------|-------------|----------|
| scripts/manifest.sh + mise task | A shell-and-jq script with a required action argument, a mise task, and bats coverage using a fake `gh`. Matches every convention the repository already enforces. | ✓ |
| Kevinjil's action | Least code to write, but it expects a `build.yaml` this repository does not have, cannot be run locally, and reads a `.md5` asset this repository does not publish. | |
| Inline in the workflow YAML | No new files, but it breaks the one-mise-task-per-CI-job rule and cannot be covered by bats. | |

**User's choice:** scripts/manifest.sh + mise task.

**Continue check:** offered more questions on a Pages landing page and the Pages build-type switch. User chose **Next area**; both recorded as Claude's discretion.

---

## Changelog into manifest

### Where does the manifest's changelog text come from?

| Option | Description | Selected |
|--------|-------------|----------|
| CHANGELOG.md, read at build | `package.sh build` reads the section for the version it is packaging. One source of truth, frozen at release time, assertable by `package.bats`. | ✓ |
| GitHub release notes | Already the text on the release page, but `--generate-notes` emits a commit and PR list, and it puts the catalog text where no test can check it. | |
| A separate changelog field | Lets the catalog blurb be shorter, at the cost of two places to keep in sync — the drift REL-04 exists to prevent. | |

**User's choice:** CHANGELOG.md, read at build.

### What heading form marks a version section, and what happens to `## Unreleased`?

| Option | Description | Selected |
|--------|-------------|----------|
| `## [1.0.0.0] - 2026-09-21` | keepachangelog's form, which the file's subsections already follow. `## Unreleased` is renamed at release time and a fresh empty one added above. The date is the released signal. | ✓ |
| `## 1.0.0.0` | Simplest regex, but no released-versus-pending signal and no date. | |
| `## v1.0.0.0` | Matches the tag exactly, but diverges from keepachangelog and the `v` is redundant inside this file. | |

**User's choice:** `## [1.0.0.0] - 2026-09-21`.

### What form does the changelog text take in the manifest field?

| Option | Description | Selected |
|--------|-------------|----------|
| The section body, verbatim | The only form where "the same changelog" is literally testable as byte equality. JSON carries the newlines. | ✓ |
| Flattened one-liner | jellyrock's `--oneline` approach. Safe in any client, but it is a transformation and it drops the Upgrade note text. | |
| Markdown stripped, lines kept | Reads cleanly anywhere, but the stripping rules become their own thing to maintain and test. | |

**User's choice:** The section body, verbatim.
**Notes:** Flagged as unverified whether Jellyfin's web client renders the field as markdown or plain text. The `v0.9.0.0` rehearsal is the moment to look at it on a real server.

### What happens when the version has no dated CHANGELOG.md section?

| Option | Description | Selected |
|--------|-------------|----------|
| package.sh build refuses | Earliest possible point; fires identically on a laptop, in CI, and in the release workflow's `mise run package` step. Fails closed. | ✓ |
| release-gate.sh checks it | Keeps every release precondition in one script, but it only runs inside the release workflow. | |
| Warn and fall back | Nothing ever blocks a release, so REL-04 can be silently unmet. | |

**User's choice:** package.sh build refuses.
**Notes:** Surfaced afterwards: `mise run test` runs `package.bats`, which calls `package.sh build`, so the suite becomes dependent on `CHANGELOG.md` holding a dated section for the current `Directory.Build.props` version. Two consequences recorded in CONTEXT.md — the bump and the dating land in the same commit, and `package.bats` needs a changelog-path override.

**Continue check:** offered more questions on the `package.bats` fixture seam and the bump-and-date-together rule. User chose **Next area**; both left to the planner.

---

## The two releases

### Is 0.9.0.0 a GitHub pre-release, and does the manifest list it?

| Option | Description | Selected |
|--------|-------------|----------|
| Full release, listed | An ordinary release that stays in the manifest. Criterion 5 needs both versions listed, and PROJECT.md rejected dropping old versions. | ✓ |
| Pre-release on GitHub, still listed | Honest about intent, but the rebuild would have to explicitly not filter prereleases, which reads as a bug to a later contributor. | |
| Full release, removed later | Ends with a clean two-version catalog, but conflicts with the locked keep-every-version decision. | |

**User's choice:** Full release, listed.

### What does 0.9.0.0's changelog entry say, given it is the first release ever?

| Option | Description | Selected |
|--------|-------------|----------|
| Rewrite as a first release | Replace the accumulated deltas with what the release actually is. The development history stays in git and in `.planning/`. | ✓ |
| Keep the accumulated deltas | Zero rewriting and truthful for anyone following the repository, but it opens by describing the removal of a setting no installer ever had. | |
| Both, split by section | Nothing lost and the right thing first, but the verbatim manifest field would carry all of it into the catalog. | |

**User's choice:** Rewrite as a first release.
**Notes:** Raised before the question: the current `## Unreleased` section is a delta against nothing. It describes removing a migration-behavior setting, renaming a scheduled-task key, and a JSON-to-SQLite upgrade note — none of which any public server can have been in, because no version was ever released.

### What does 1.0.0.0's changelog entry say?

| Option | Description | Selected |
|--------|-------------|----------|
| Written after the rehearsal | Whatever the 0.9.0.0 catalog install turned up, plus a first-stable-release line. Cannot be drafted during planning. | ✓ |
| No functional change from 0.9.0.0 | Lets the whole phase be planned up front, but assumes in advance the one thing the rehearsal exists to determine. | |
| Hold content back for 1.0.0.0 | Gives the headline release substance, but then 0.9.0.0 no longer rehearses what actually gets released. | |

**User's choice:** Written after the rehearsal.

### Who pushes the two release tags?

| Option | Description | Selected |
|--------|-------------|----------|
| You push both tags | The phase stops at a checkpoint handing over each `git push origin v<version>`. Same shape as Phase 5's D-15. | ✓ |
| You push v0.9.0.0, the phase pushes v1.0.0.0 | Fewer interruptions, but 1.0.0.0 is the tag that most deserves a deliberate human push. | |
| The phase pushes both | Fastest, but breaks the precedent D-15 set one phase ago for exactly this class of act. | |

**User's choice:** You push both tags.

### If the 0.9.0.0 catalog install fails, what happens?

| Option | Description | Selected |
|--------|-------------|----------|
| Fix forward: 0.9.1.0, 0.9.2.0 | A version number is never reused. Each extra version exercises the update path once more. | ✓ |
| Delete and re-push v0.9.0.0 | Ends with a clean catalog, but lets one version number name different bytes. | |
| Treat a failure as a phase stop | Maximum caution, but turns a one-line workflow fix into a restart. | |

**User's choice:** Fix forward.
**Notes:** User explicitly chose **More questions** at the continue check to settle this rather than leave it to be decided under pressure mid-execution.

---

## Proving the install

### How is PUB-04's catalog install and update proven?

| Option | Description | Selected |
|--------|-------------|----------|
| Hermetic e2e + one manual check | A bats file serves the real manifest and zips from a local nginx and drives the catalog API; plus one browser walkthrough against the real public URL. | ✓ |
| E2E against the live public URL | Proves exactly what the requirement describes, but cannot pass until the end of the phase and then depends on GitHub Pages being reachable on every PR. | |
| Manual walkthrough only | Matches the requirement's wording and needs no harness, but leaves no automated guard. | |

**User's choice:** Hermetic e2e + one manual check.
**Notes:** Two constraints raised before the question — `e2e/compose.yaml:25` bind-mounts onto `/config/plugins/EmbyAuth_1.0.0.0`, which a catalog install would collide with, and a live-URL test cannot exist until the repository is public and 0.9.0.0 is out. Jellyfin's `PackageController.cs` was read to confirm the flow is API-driveable.

### How does the hermetic test get two versions to update between?

| Option | Description | Selected |
|--------|-------------|----------|
| Two real builds via a version override | A documented `PACKAGE_VERSION` override beside the three `package.sh` already has. Zip, `meta.json`, and manifest entry all agree. | ✓ |
| One build, two manifest entries | No script change, but the installed `meta.json` would disagree with the manifest entry. | |
| Build twice from a temp props copy | Keeps the footgun out of `package.sh`, but gives the script a props-path seam instead — the same class of thing one step removed. | |

**User's choice:** Two real builds via a version override.
**Notes:** The accepted cost was stated in the option itself — the override could mislabel a real release zip, because the DLL's assembly version still comes from `Directory.Build.props`. Recorded in CONTEXT.md with an instruction to document it as test-only.

### What covers the real public URL?

| Option | Description | Selected |
|--------|-------------|----------|
| Browser walkthrough + deploy smoke check | One manual install, plus the Pages workflow verifying its own published output after every deploy. Reuses the checksum logic the rebuild already needs. | ✓ |
| Browser walkthrough only | Exactly what the requirement asks for, but a later deploy that publishes a broken manifest goes unnoticed. | |
| Walkthrough + an on-demand verify action | Gives a tool rather than a gate, so it only catches a broken catalog when someone remembers to run it. | |

**User's choice:** Browser walkthrough + deploy smoke check.

### Where does the hermetic catalog test run?

| Option | Description | Selected |
|--------|-------------|----------|
| In the existing e2e suite | One suite, one command, and `ci-success`'s `needs:` list and hardcoded count of 3 stay untouched — that name is the release gate's contract. | ✓ |
| Its own mise task and CI job | Isolates the slower test, but changes `ci-success`'s needs list and the count beside it, which `ci.yml`'s own comment warns must move together. | |
| In the suite, skipped by default | Keeps ordinary runs fast, but a test that does not run by default is a test that stops running. | |

**User's choice:** In the existing e2e suite.

---

## Claude's Discretion

- The action-argument names for `scripts/manifest.sh` and the `jq` shape of the composed manifest.
- Whether the Pages site serves a landing `index.html` and `.nojekyll`, and what the page says.
- How GitHub Pages is switched to the GitHub Actions build type — repository settings or a `gh api` call.
- The regex or parser that extracts a `CHANGELOG.md` section, and the name of the changelog-path override `package.bats` needs.
- The README's manifest URL and catalog step wording (DOCS-02), and its placement relative to Install and Compatibility.
- The `CLAUDE.md` pin list wording (DOCS-04).
- The new end-to-end file's number and name, and the compose service names, ports, and artifact serving.
- Whether `scripts/manifest.sh` gets a separate `verify` action, and how much bats coverage the smoke check gets.
- The wording of the two release checkpoints.

## Deferred Ideas

- Branch protection requiring `ci-success` on `main` — becomes available the moment the repository is public; Phase 5's D-09 preserved the check-run name for it.
- A scheduled job checking the live catalog on a timer — the extension of the post-deploy smoke check; not needed to close PUB-04.
- Submitting the manifest URL to `jellyfin.org`'s community list or `awesome-jellyfin` — trigger recorded in `FEATURES.md:171`.
- A `SECURITY.md`, issue templates, or a contributing guide — carried from Phase 5.
- Retiring `scripts/pre-public-audit.sh` now that it has served — carried from Phase 5.
- Tidying the pushed `gsd/phase-*` branches — carried from Phase 5.
- Automated version bump and changelog tooling — rejected in `PROJECT.md`; D-07's dated-section convention is the manual discipline that replaces it.
