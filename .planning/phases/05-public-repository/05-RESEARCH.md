# Phase 5: Public Repository - Research

**Researched:** 2026-09-20
**Domain:** GitHub Actions CI/CD gating, secret scanning (gitleaks), static workflow analysis (zizmor), GitHub REST API (check runs, repository visibility)
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

### The release gate (REL-01)

- **D-01:** The gate is a new `scripts/release-gate.sh`, called from a step in `.github/workflows/release.yml`, with tests in `tests/scripts/`. It is not inline YAML and it is not a new action on `scripts/package.sh`. The repository already puts testable release logic in a script and covers it with bats — `scripts/package.sh check-tag` is called from `release.yml:35` and asserted by `tests/scripts/package.bats` — so the gate follows the precedent rather than establishing a second shape. Keeping it out of `package.sh` keeps packaging and GitHub API concerns in separate files. Per the workspace rule, the script requires an explicit action argument.
- **D-02:** The gate fails closed immediately. If the tagged commit has no completed, successful `ci-success` check run at the moment the gate runs, it refuses. There is no poll loop, no timeout knob, and no grace window. The maintainer re-pushes the tag once CI is green. This matches the recorded decision that the release checks the CI **result** of the tagged commit rather than waiting for or re-running CI (`PROJECT.md` §Key Decisions, §Out of Scope).
- **D-03:** The pass condition is exactly one check run named `ci-success` whose `conclusion` is `success`. Every other state refuses: `failure`, `cancelled`, `skipped`, `timed_out`, any other conclusion, a run still in progress, or no such check run at all. The gate does **not** require every check run on the commit to be green — that would couple the release to whatever checks are added later (Dependabot, CodeQL, the Phase 6 Pages workflow) and would break the first time one appears. `ci.yml:65-81` already makes `ci-success` fail when any job did not succeed, so the one name carries the whole suite.
  - **Verified during this discussion:** `cancelled` is a reachable state, not a theoretical one. `ci.yml:14-17` sets `cancel-in-progress: true` grouped by workflow and ref, so two commits landing on `main` in quick succession cancel the earlier run. Tagging that earlier commit leaves `ci-success` as `cancelled`, and the gate must refuse it — a cancelled run proves nothing about the commit.
- **D-04:** The bats tests make the script testable by putting a fake `gh` on `PATH` in a temporary directory, which prints fixture JSON. The alternative env-var seams (a variable naming a JSON fixture file, or a variable naming the command) were rejected because they skip the `gh api` invocation itself — the argument construction is the part most likely to be wrong, and it is the part a fake on `PATH` still exercises. No environment variable exists in the script solely for the benefit of tests.

### gitleaks (REL-03)

- **D-05:** `mise run lint` runs `gitleaks git`, a full-history scan, not `gitleaks dir` and not `--staged`. **Measured during this discussion: 0.5 s over the repository's 204 commits**, so the usual objection that a history scan is too slow for a pre-commit hook does not apply here. `--staged` was rejected outright: CI has nothing staged, so the lint job would scan nothing and the check would be theatre. Choosing the history scan also means REL-03 and PUB-01's history-scan requirement are satisfied by the same command, so the pre-publication scan is something the pre-commit hook and CI already prove continuously rather than a one-time event.
- **D-06:** Suppression uses a `.gitleaks.toml` allowlist. `.gitleaksignore` fingerprints were rejected because history fingerprints are commit-pinned, entries accumulate, and any line move needs a new entry. Inline `gitleaks:allow` comments were rejected on a structural ground: an old commit's blob does not contain a comment added today, so inline comments cannot clear a historical finding at all, and D-05 scans history.
- **D-07:** The allowlist is narrow — regexes that match the fixture **values**, not the paths that hold them. A path allowlist over `tests/` was rejected because `tests/` is exactly where a real credential could land by mistake, and allowlisting the directory would mean it is never reported.
  - **Verified during this discussion:** two value regexes, `0123456789abcdef` and `sentinel-api-key-`, clear every finding in both the history scan and the working-tree scan, exit 0, with no path exempted. The planner settles the exact `regexTarget` and regex form; the constraint is that no path or whole-directory exemption is used.
  - **The findings these cover, all confirmed to be deliberate test fixtures, none a real credential:** `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyClientTests.cs:17`, `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthSettingsTests.cs:11`, `tests/js/configPage.test.js` (two occurrences), and one historical occurrence in `.planning/codebase/TESTING.md`. Nothing needs rotation.
  - Note for the planner: gitleaks exits 1 when it reports a finding, so `mise run lint` fails until the allowlist exists. The allowlist and the lint-task change land together or lint breaks.

### zizmor (REL-02)

- **D-08:** `[tasks.lint]` moves to `zizmor --offline --persona=pedantic .github/workflows` permanently. Pedantic becomes the standing gate, not a one-time sweep, so a later workflow edit that reintroduces one of these findings fails CI and the pre-commit hook. The usual objection — that a future tool release adds a new audit and breaks an unrelated pull request — does not apply here, because `zizmor` is pinned to `1.30.1` in `.mise.toml` and a new audit can only arrive through a deliberate bump.
- **D-09:** The `ci-success` job gets `name: ci-success`, identical to its job id. This satisfies zizmor's `anonymous-definition` audit while keeping the check-run name byte-identical. The name carries no new information, and that is the accepted trade.
  - **Why this matters, verified live against this repository:** a job's `name:` becomes its check-run name, and with no `name:` set the check run takes the job id. Querying the commits API for commit `ecee1ed` returned exactly four check runs — `ci-success`, `e2e`, `test`, `lint`. Giving the aggregate job a descriptive name would therefore rename the check run that D-03's gate matches on, and would also break any future branch-protection rule that requires `ci-success`. REL-02 and REL-01 collide at this single point, and D-09 is the resolution.
  - The other four jobs (`lint`, `test`, `e2e` in `ci.yml`, `release` in `release.yml`) may take descriptive names freely. Nothing matches on their check-run names.
- **D-10:** `release.yml` gains a workflow-level concurrency group keyed on the ref with `cancel-in-progress: false`. It deliberately differs from `ci.yml:14-17`, which sets `cancel-in-progress: true`. Cancelling a queued CI run is correct; cancelling an in-flight release is not, because the run can be interrupted after `gh release create` has made the release but before every asset is uploaded. Releases serialize; they are never killed.
- **Measured pedantic inventory, the full set REL-02 must resolve — 7 findings, all informational or low, every one fixable without a suppression:** 5 × `anonymous-definition` (the four `ci.yml` jobs and `release.yml`'s `release` job), 1 × `undocumented-permissions` (`release.yml:19`, the `contents: write` grant needs an explanatory comment), 1 × `concurrency-limits` (`release.yml` has no concurrency setting, which D-10 adds). The default persona is already clean — it reports "No findings to report. Good job! (7 suppressed)" — so these seven are exactly the pedantic-only set.

### What the public sees

- **D-11:** `.planning/` stays and is published as-is. All 106 tracked files, including the 79 under `phases/`, the seven codebase maps, and the five research documents. No deletion and no history rewrite.
  - The deciding constraint: deleting `.planning/` from HEAD achieves nothing, because the full 204-commit history becomes readable at the same moment and `.planning/` is present in it from the third commit onward. Only a history rewrite removes it, and that would invalidate every commit SHA, every `file:line` citation the planning documents make about each other, and the pushed `gsd/phase-*` branches. The maintainer chose to keep the record rather than pay that.
  - Checked before deciding: the three phase security audits all close clean — `01-SECURITY.md` and `03-SECURITY.md` record `verdict: SECURED`, `02-SECURITY.md` records `status: verified`. No open security finding is published by this decision.
- **D-12:** `CLAUDE.md` and `.claude/rules/plugin.md` and `.claude/rules/e2e.md` stay public. They record verified Jellyfin and Emby behaviors that the code depends on, which is what a human contributor needs regardless of whether they use an agent. Keeping `CLAUDE.md` also leaves Phase 6's DOCS-04 target in place, since that requirement names the version bump rule in `CLAUDE.md` specifically.

### The audit and the switch (PUB-01, PUB-03)

- **D-13:** The audit is a new `scripts/pre-public-audit.sh`, tracked, shellcheck-clean, and requiring an explicit action argument per the workspace rule that anything repeatable belongs in `scripts/` rather than as an ad-hoc one-liner. It prints a pass-or-fail report per item. The counter-argument was weighed and rejected: this gate fires once, so the script arguably becomes dead code the day after. The maintainer chose the script anyway, for reproducibility and for the evidence it produces.
- **D-14:** The audit's history-scan item is already covered by D-05 and should not be reimplemented — the script invokes the same `mise run lint` path or the same `gitleaks git` command rather than a second, differently-configured scan. Two scans that can disagree is the failure mode to avoid.
- **D-15:** **The maintainer runs the visibility change personally.** The phase completes the audit, reports the result, and stops. The command is `gh repo edit --visibility public --accept-visibility-change-consequences`, confirmed present in the installed `gh`. No agent runs it. The act is irreversible for anyone who copies the repository in the interval, and `PROJECT.md` §Constraints requires the maintainer's explicit approval at the time of the change; putting the trigger in the maintainer's hands means no checkpoint answer can be misread as consent.
- **D-16:** The ordering is strict: the gate and the tooling land and are proven first, then the audit runs, then the switch. Not parallel. This matches ROADMAP criterion 5 exactly — the switch happens "after criteria 1 and 4 were complete" — and it means the repository only becomes public once the release path is already safe. Nothing in this phase is slow enough for parallelism to buy anything.
- Note for the planner: because D-15 hands the switch to the maintainer, PUB-03 cannot be marked complete from inside the execution run. The phase verifies it afterward with `gh repo view --json visibility`.

### The README (DOCS-03)

- **D-17:** A new **Compatibility** section carries the tested versions and the `targetAbi` floor together. Phase 6's catalog-install instructions can then point at it. Rejected: folding it into the Requirements bullet at `README:14`, and folding it into the Status line at `README:10`, which would bury a real compatibility caveat in a status blurb an administrator may skim past.
- **D-18:** The section states the version facts and claims nothing beyond them. It says what was tested — Jellyfin 12.1.0 and Emby 4.10.0.40, in local containers — and it says that `targetAbi` declares a minimum Jellyfin version, so Jellyfin will offer and install the plugin on a newer server that nobody has tested. It does **not** say or imply that a newer Jellyfin works. This applies Phase 3's D-18 rule that the plugin never claims behavior it has not verified.
  - The mechanism, for the planner: `scripts/package.sh:33-38` derives `targetAbi` from the `Jellyfin.Controller` package version plus a fourth part, giving `12.1.0.0`, and writes it into both `meta.json` and `manifest.json`. `tests/scripts/package.bats:47` and `:61` assert both. Jellyfin filters catalog entries with `Version.Parse(x.TargetAbi) <= appVer`, so the value is a floor with no corresponding ceiling (`.planning/research/FEATURES.md:19`, `.planning/research/SUMMARY.md:71`, citing Jellyfin issue #11331).
- **D-19:** The sentences that say the repository is private are corrected in **this** phase, not deferred to Phase 6. `README:21` — "The repository is private, so you need access to it" — becomes false at the instant PUB-03 lands, and `README:33`'s note that the manifest works "only when the release files are public" changes meaning. Phase 6's DOCS-02 then rewrites the Install section around the manifest URL on top of a README that was never wrong. The Status line's "Not published to a plugin repository" stays true until Phase 6 and is left alone.

### Claude's Discretion

The maintainer did not settle these; the planner does, within the constraints above.

- The exact `regexTarget` and regex form in `.gitleaks.toml`, provided no path or whole-directory exemption is used (D-07).
- Where the gate step sits in `release.yml` relative to the existing `check-tag` step, and the permission scoping the gate job needs — reading check runs requires more than the current top-level `contents: read`.
- The descriptive `name:` values for the four jobs that are free to take one (D-09), and the explanatory comment wording for `release.yml:19`'s `contents: write` (REL-02).
- The action-argument names for `scripts/release-gate.sh` and `scripts/pre-public-audit.sh`, and the exact set of checks the audit script reports on, given the measured surface: 9 workflow runs, 0 artifacts, 0 issues, 0 releases, 10 pull requests.
- Whether `scripts/pre-public-audit.sh` gets bats coverage, and how much.
- Exact wording throughout the README and any new comments.

### Deferred Ideas (OUT OF SCOPE)

- **Branch protection requiring `ci-success` on `main`** — impossible today (verified: HTTP 403, needs GitHub Pro or a public repository) and therefore not a Phase 5 deliverable. It becomes available the moment PUB-03 lands. D-09 deliberately preserves the `ci-success` check-run name so the rule can be added later without touching the gate. Worth raising after the switch.
- **A `SECURITY.md`, issue templates, or a contributing guide** — raised as a candidate at the final gate and not selected. Strangers can file issues the moment the repository is public, and the repository has none of these today. Not in any requirement; belongs in its own phase or a quick task.
- **`release-gate.sh` also verifying the tag is an ancestor of `main`** — raised and not selected. D-03's `ci-success` check already proves the commit passed the suite; an ancestry check would additionally prove it went through the branch, which is what branch protection will do properly once it is available.
- **What becomes of the pushed `gsd/phase-*` branches once the repository is public** — raised and not selected. They are published by D-11's keep-everything decision. Tidying them is housekeeping, not a publication blocker.
- **Retiring `scripts/pre-public-audit.sh` after it has served** — the "one-time gate becomes dead code" objection was raised against D-13 and the maintainer chose the script anyway. If it is still unused well after v1.0.0, the repository's own "replace, don't deprecate" rule says delete it rather than leave it.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| REL-01 | The release workflow publishes a release only when the tagged commit has a passing `ci-success` check run | Pattern 1 (server-side `check_name` filtering), Code Example 1, Pitfall 1 (`checks: read` permission), Validation Architecture REL-01 row |
| REL-02 | Each `zizmor --persona=pedantic` finding is fixed or suppressed with a written reason | `zizmor` audit definitions (`anonymous-definition`, `undocumented-permissions`, `concurrency-limits`) under Sources/Standard Stack; Pitfall 4 (`--offline` compatibility); Security Domain threat table |
| REL-03 | gitleaks runs in `mise run lint`, so the pre-commit hook and CI check for secrets | Pattern 2 (`.gitleaks.toml` allowlist shape), Code Example 3, Pitfall 2 (commit ordering), Pitfall 3 (v8.30.1 tag non-issue) |
| DOCS-03 | `README.md` states the tested Jellyfin and Emby versions, and that `targetAbi` sets only the minimum Jellyfin version | Mechanism already verified in D-18 (copied above); no new research needed beyond confirming `scripts/package.sh:33-38` and `tests/scripts/package.bats:47,61` this session |
| PUB-01 | Before the repository goes public, gitleaks scans the full git history, and the Actions logs and artifacts, issue and PR text, and release notes are reviewed; every finding is removed or rotated | Architecture Diagram (`pre-public-audit.sh` flow), Validation Architecture PUB-01 row, Open Question 1 (bats coverage scope) |
| PUB-03 | The repository is public, after PUB-01 and REL-01 are complete and the maintainer approves the change at that time | Architectural Responsibility Map ("Repository visibility switch" row — maintainer-only, no script), Validation Architecture PUB-03 row |
</phase_requirements>

## Summary

This phase adds no new library dependency to the plugin itself — every deliverable is a shell script, a GitHub Actions workflow edit, a tool config file, or prose. The two external tools this phase newly wires into the lint task, gitleaks and zizmor, are both already resolved: zizmor 1.30.1 is already pinned in `.mise.toml:8` and installed locally; gitleaks 8.30.1 is confirmed as the newest release (`mise ls-remote gitleaks`, `gh` release page) and installs and runs cleanly through `mise`'s `aqua:gitleaks/gitleaks` backend, verified locally in this session (`gitleaks version` → `8.30.1`).

01-CONTEXT.md's decisions (D-01 through D-19) already resolve the architecture, the sequencing, and the security posture of this phase in detail — this research does not re-litigate any of them. Its job is to close the "Claude's Discretion" items with verified technical grounding: the exact `.gitleaks.toml` allowlist shape, the GitHub REST endpoint and permission scope `scripts/release-gate.sh` needs to check `ci-success`, and the precise wording zizmor's `undocumented-permissions` and `concurrency-limits` pedantic audits expect. The standout finding is the `check_name` query parameter on the check-runs endpoint: filtering server-side (`?check_name=ci-success`) turns D-03's "exactly one check run named `ci-success`" rule into a single filtered API call instead of a client-side scan of every check run on the commit, and its default `filter=latest` already collapses re-runs to the most recent result — no extra logic needed for that case.

**Primary recommendation:** Build `scripts/release-gate.sh` around `gh api "repos/$GH_REPO/commits/$SHA/check-runs?check_name=ci-success"`, reading the response with the repository's pinned `jq`, add `checks: read` to the release job's `permissions:` block (the current `contents: write` does not cover it), and build `.gitleaks.toml` as a single global `[[allowlists]]` table with `regexes` left on the default `regexTarget = "secret"` — no `regexTarget` override is needed because D-07's two value regexes already match the extracted secret itself, which is what "secret" targets by default.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Release gating (block a release without passing CI) | CI/CD (GitHub Actions `release.yml`) | Repository Tooling (`scripts/release-gate.sh`) | The workflow step triggers the check; the script owns the `gh api` call and the pass/fail logic, matching the existing `check-tag` precedent (`release.yml:32-35`) |
| Static workflow security scanning (zizmor) | Repository Tooling (`.mise.toml` `[tasks.lint]`) | CI/CD (`ci.yml` `lint` job) | `mise run lint` is the single source of truth; the CI job only invokes it |
| Secret scanning (gitleaks) | Repository Tooling (`.mise.toml` `[tasks.lint]`) | CI/CD (`ci.yml` `lint` job) + local pre-commit hook | Same task, three invocation contexts (local dev, pre-commit hook, CI), one command |
| Pre-publication audit | Repository Tooling (`scripts/pre-public-audit.sh`) | GitHub Platform (Actions logs, issues, PRs, releases it reads via `gh`) | A one-shot script that queries the GitHub API and re-runs the existing lint-level gitleaks scan (D-14); it owns no new scanning logic |
| Repository visibility switch | GitHub Platform (`gh repo edit`) | — (maintainer-run, not scripted, per D-15) | Irreversible action; no tier below the human is trusted with it |
| Compatibility documentation | Documentation (`README.md`) | — | Static prose, no runtime component |

## Standard Stack

### Core
| Tool | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| gitleaks | 8.30.1 | Full-history and working-tree secret scanning | De facto standard git secret scanner; `git`/`dir`/`stdin` subcommands cover every CI and pre-commit shape this repo needs [VERIFIED: github.com/gitleaks/gitleaks/releases/tag/v8.30.1 — confirmed via `mise ls-remote gitleaks` and a local `gitleaks version` run, both this session] |
| zizmor | 1.30.1 | Static analysis of GitHub Actions workflow YAML | Already the repo's workflow linter (`.mise.toml:8`); `--persona=pedantic` is a documented, first-class mode, not a hack [VERIFIED: .mise.toml:8, quote: `zizmor = "1.30.1"`] |
| gh CLI | 2.97.0 (locally installed; GitHub-hosted runners ship a current version by default) | `gh api`, `gh release create`, `gh repo edit` | Already used in `release.yml:48` (`gh release create`) and assumed present on `ubuntu-latest` without a `.mise.toml` pin — this phase's new scripts follow that same existing convention rather than introducing a new pinned tool [VERIFIED: release.yml:48, quote: `gh release create "$TAG" ...`; local `gh --version` → `gh version 2.97.0 (2026-07-31)`] |
| bats | 1.14.0 | Shell script tests (`scripts/release-gate.sh`, optionally `scripts/pre-public-audit.sh`) | Already pinned and used by `tests/scripts/package.bats` [VERIFIED: .mise.toml:3, quote: `bats = "1.14.0"`] |

### Supporting
| Tool | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `jq` | 1.8.2 | Parsing `gh api` JSON output in bash | Already pinned [VERIFIED: .mise.toml:4, quote: `jq = "1.8.2"`]. `gh api --jq` uses its own embedded jq, but scripts may still shell out to the pinned `jq` for readability. |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `gh api ... --jq` for the check-runs query | `curl` + `jq` directly against the REST API | `gh api` already handles auth (`GH_TOKEN`), the `Accept` header, and pagination; `curl` would duplicate all of that for no benefit — the existing "Create the GitHub release" step already establishes `gh` as the API client of choice in this workflow |
| A global `[[allowlists]]` table in `.gitleaks.toml` | Per-rule `[[rules.allowlists]]` | A GitHub issue thread (gitleaks#1878) shows `[[rules.allowlists]]` only clears findings for the one named rule; a global `[[allowlists]]` clears the finding regardless of which built-in rule matched the fixture, which is what D-07's "two value regexes ... clear every finding" measurement implies |

**Installation:**
```bash
mise use gitleaks@8.30.1
```
(zizmor, bats, jq, and gh are already pinned or already present; no other installation step is needed.)

**Version verification:** `mise ls-remote gitleaks` (run this session) lists `8.30.1` as the newest of the ten most recent releases; the GitHub releases page confirms the same tag, published 2026-03-21. `gitleaks version` against the locally installed `8.30.1` binary returns `8.30.1` — the binary that `mise` installs actually runs, which matters given the note below.

## Package Legitimacy Audit

> This phase installs no npm/PyPI/crates package. `gitleaks` is a Go binary distributed as a GitHub release and resolved through `mise`'s `aqua:gitleaks/gitleaks` backend (`mise registry` output, this session), so the npm/pip/cargo registry-verification protocol does not apply. `zizmor`, `bats`, `jq`, and `gh` are pre-existing pins or already-present tools; none is new to this phase.

| Package | Registry | Age | Downloads | Source Repo | Verdict | Disposition |
|---------|----------|-----|-----------|-------------|---------|-------------|
| gitleaks | GitHub Releases (via mise `aqua` backend) | Long-established project (190+ tagged releases observed on the releases page) | 6M+ downloads on the single `linux_x64` asset of v8.30.1 alone | github.com/gitleaks/gitleaks (canonical, maintainer `zricethezav`) | OK | Approved — installed and run locally this session, no `postinstall`-equivalent risk (single static Go binary) |

**Packages removed due to [SLOP] verdict:** none.
**Packages flagged as suspicious [SUS]:** none.

## Architecture Patterns

### System Architecture Diagram

```
Push a "v*" tag
       |
       v
+----------------------+
| release.yml: release |
+----------------------+
       |
       v
[1] Checkout + mise install (tools only, cache: false)
       |
       v
[2] scripts/package.sh check-tag "$TAG"  --------> refuse if tag != v<Directory.Build.props version>
       |
       v
[3] scripts/release-gate.sh check "$GITHUB_SHA"    (NEW — REL-01)
       |
       |  gh api "repos/$GH_REPO/commits/$GITHUB_SHA/check-runs?check_name=ci-success"
       |  response read with the pinned jq
       |
       |  exactly one run, status=="completed", conclusion=="success"?
       |        NO  -----> exit 1, workflow stops, no release created
       |        YES ----> continue
       v
[4] mise run test  (rebuild + unit tests, belt-and-suspenders — pre-existing step)
       |
       v
[5] mise run package  (builds the zip + manifest.json)
       |
       v
[6] gh release create  (pre-existing — publishes zip + manifest.json)

Separately, on every push/PR (ci.yml):
  lint job    --> mise run lint  (dotnet format, shellcheck, shfmt, actionlint,
                                   zizmor --pedantic, gitleaks git  <- NEW, REL-02/REL-03)
  test job    --> mise run test
  e2e job     --> mise run e2e
  ci-success  --> fails if lint|test|e2e did not all succeed
                  (this is the check run release-gate.sh reads)

Before PUB-03 (one-time, human-triggered):
  scripts/pre-public-audit.sh run
       |
       +--> re-invokes the same gitleaks git history scan (D-14, no second scanner)
       +--> gh api: enumerates workflow runs, artifacts, issues, PRs, releases
       |        for human review (script surfaces evidence; a human judges content)
       v
  Report: pass/fail per item, printed for the maintainer
       |
       v
  Maintainer personally runs:
    gh repo edit --visibility public --accept-visibility-change-consequences
```

### Recommended Project Structure
```
scripts/
├── package.sh              # existing — build, check-tag
├── release-gate.sh         # NEW — checks ci-success on a commit
└── pre-public-audit.sh     # NEW — one-shot pre-publication evidence report
tests/scripts/
├── package.bats             # existing
├── release-gate.bats        # NEW
└── pre-public-audit.bats    # NEW (discretion: at minimum, usage/unknown-action tests)
.gitleaks.toml               # NEW — repo-root allowlist, auto-discovered
.github/workflows/
├── ci.yml                   # job `name:` fields added; ci-success unchanged
└── release.yml               # gate step + checks:read permission + concurrency block
```

### Pattern 1: Server-side `check_name` filtering instead of client-side scanning
**What:** Query the check-runs endpoint with `check_name=ci-success` rather than fetching every check run on the commit and filtering in the script.
**When to use:** Any time the gate only cares about one named check, regardless of how many other checks exist on the commit (branch protection, Dependabot, CodeQL, the future Pages workflow — none of which should make the gate's query grow).
**Example:**
```bash
# Source: https://docs.github.com/rest/checks/runs (List check runs for a Git reference)
# check_name filters server-side; filter defaults to "latest", which already
# collapses a re-run to the most recent result for that name.
runs_json="$(gh api "repos/${GH_REPO}/commits/${SHA}/check-runs?check_name=ci-success")"

count="$(jq '.check_runs | length' <<<"$runs_json")"
if [[ "$count" -ne 1 ]]; then
  echo "Expected exactly one ci-success check run on ${SHA}, found ${count}." >&2
  exit 1
fi

status="$(jq -r '.check_runs[0].status' <<<"$runs_json")"
conclusion="$(jq -r '.check_runs[0].conclusion' <<<"$runs_json")"
if [[ "$status" != "completed" || "$conclusion" != "success" ]]; then
  echo "ci-success on ${SHA} is ${status}/${conclusion}, not completed/success." >&2
  exit 1
fi
```
This directly implements D-02/D-03: fails closed on `cancelled`, `failure`, `skipped`, `timed_out`, any other conclusion, an in-progress run, or no matching run at all (`count == 0`).

**The filter goes in the query string, not in a field flag — measured, not assumed.** `gh api` switches the HTTP method to POST as soon as a field flag is passed, unless `-X GET` is also passed. Three forms were run against the live repository:

| Invocation | Measured result |
|---|---|
| `gh api "repos/{owner}/{repo}/commits/$SHA/check-runs"` with the filter passed as a field flag | **HTTP 404 Not Found** — sent as POST, so it never reaches the endpoint |
| the same call with `-X GET` added | HTTP 422 "No commit found for SHA" |
| `gh api "repos/{owner}/{repo}/commits/$SHA/check-runs?check_name=ci-success"` | HTTP 422 "No commit found for SHA" |

The 422s are the correct response for a SHA GitHub does not know, and they prove the endpoint path, the `{owner}`/`{repo}` expansion, and the auth all resolve. The 404 proves the field-flag form does not reach the endpoint at all. A 404 lands in the gate's "API call failed" branch, so the gate fails closed and refuses **every** release. Do not "tidy" the query string back into a field flag.

### Pattern 2: A single global `[[allowlists]]` table, default `regexTarget`
**What:** One `[[allowlists]]` block at the top level of `.gitleaks.toml`, `regexes` left on the implicit default `regexTarget = "secret"`.
**When to use:** Suppressing specific fixture *values* (D-07) without touching a path or a specific rule ID.
**Example:**
```toml
# Source: https://github.com/gitleaks/gitleaks (README "Configuration" section,
# and gitleaks#1878 clarifying [[allowlists]] vs [[rules.allowlists]])
title = "jellyfin-plugin-emby-auth gitleaks config"

[extend]
useDefault = true

[[allowlists]]
description = "Deliberate test fixtures, not real credentials (see 05-CONTEXT.md D-07)"
regexes = [
  '''0123456789abcdef''',
  '''sentinel-api-key-''',
]
```
`regexTarget` is left unset because its default, `"secret"`, tests against the extracted secret value — exactly what D-07 measured against. A per-rule `[[rules.allowlists]]` would need one block per matching rule ID and was rejected by the maintainer's own gitleaks#1878 reading.

### Anti-Patterns to Avoid
- **Fetching all check runs and filtering client-side:** works today (4 check runs) but re-derives what the `check_name` query parameter already does server-side, and silently degrades if the commit ever accumulates more than one page of check runs (default `per_page` is 30).
- **A path-based `.gitleaks.toml` allowlist over `tests/`:** explicitly rejected by D-07 — `tests/` is exactly where a real credential could land by mistake.
- **Copying zizmor's `concurrency-limits` remediation text verbatim:** the audit's generic guidance says add `cancel-in-progress: true`; this repo's `release.yml` needs `cancel-in-progress: false` (D-10) because an in-flight release must never be killed mid-upload. The audit only checks that a `concurrency:` block exists, not its `cancel-in-progress` value — confirmed by D-10's own live measurement in 05-CONTEXT.md, which found the `false` variant still clears the finding.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Detecting whether a commit's CI passed | A custom polling loop against `gh run list` or the Checks webhook | `GET /repos/{owner}/{repo}/commits/{ref}/check-runs?check_name=ci-success` | One documented, purpose-built endpoint returns exactly this; D-02 already forbids polling/waiting, so there is nothing to build beyond a single request |
| Secret pattern detection | A grep-based secret-pattern script | gitleaks' built-in rule set (`useDefault = true`) | gitleaks ships hundreds of maintained detection rules (AWS keys, private keys, generic high-entropy strings, etc.); a hand-rolled grep would need to reinvent all of them and would miss the ones nobody thinks of in advance |
| Workflow security review | A manual checklist against `ci.yml`/`release.yml` | `zizmor --persona=pedantic` | zizmor already encodes the specific findings this phase needs (`anonymous-definition`, `undocumented-permissions`, `concurrency-limits`) as machine-checked audits, so a later regression fails CI automatically instead of depending on a human noticing |

**Key insight:** Every tool this phase needs already exists and is either already pinned (zizmor, bats) or trivially pinned (gitleaks); the phase's actual engineering work is wiring, not building.

## Common Pitfalls

### Pitfall 1: `GITHUB_TOKEN` cannot read check runs without an explicit `checks: read` permission
**What goes wrong:** `gh api .../check-runs` fails (403, or an empty/partial result) if the job's effective token permissions don't include `checks: read`.
**Why it happens:** The `release` job in `release.yml:18-19` currently declares only `permissions: { contents: write }`, which overrides the workflow-level `permissions: { contents: read }` (`release.yml:11-12`) — GitHub Actions job-level `permissions:` is a full replacement, not an addition. Neither block currently grants `checks`.
**How to avoid:** Add `checks: read` to the `release` job's `permissions:` block alongside `contents: write` [CITED: docs.github.com/rest/checks/runs — "GitHub Apps must have the checks:read permission on a private repository ... to get check runs"]. This is also the answer to 05-CONTEXT.md's open discretion item on permission scoping.
**Warning signs:** `release-gate.sh` returns a falsely-empty check-runs list (indistinguishable from "no ci-success run yet" without inspecting the HTTP status/error body) — write the bats fake-`gh` tests to cover this failure mode explicitly, not just the happy path and the wrong-conclusion path.

### Pitfall 2: `.gitleaks.toml` must land in the same commit as the `mise run lint` change
**What goes wrong:** gitleaks exits 1 (its documented default exit code on any finding) the moment the lint task starts scanning, before the allowlist exists to clear the four known working-tree fixtures.
**Why it happens:** `mise run lint`'s ordering runs every listed command in sequence; adding the `gitleaks git` line without the allowlist already in the tree breaks `mise run lint` (and therefore the pre-commit hook and CI) for every commit between the two changes.
**How to avoid:** Land `.gitleaks.toml` and the `[tasks.lint]` edit in one commit, exactly as 05-CONTEXT.md's D-07 note already specifies. [CITED: config/gitleaks.toml default — `--exit-code` defaults to `1`]
**Warning signs:** `prek run` or CI's `lint` job fails immediately after this phase's first lint-related commit if the two changes are split.

### Pitfall 3: The v8.30.1 GitHub release object is orphaned from `master` — but this does not affect this repo
**What goes wrong (elsewhere):** `pre-commit autoupdate` in *other* repos that manage gitleaks as a `.pre-commit-config.yaml` `repo:` entry (not this repo's shape) silently downgrades `v8.30.1` back to `v8.30.0`, because pre-commit only considers tags reachable from the tool's default branch, and `v8.30.1`'s tag commit is not on `gitleaks/gitleaks`'s `master` [CITED: github.com/gitleaks/gitleaks/issues/2086].
**Why it doesn't apply here:** This repo pins tool versions in `.mise.toml` and resolves them through `mise`'s `aqua:gitleaks/gitleaks` backend, which fetches the named release's assets directly — not through pre-commit's hook-repo ancestry check. Verified empirically this session: `mise ls-remote gitleaks` lists `8.30.1`, and the locally `mise`-installed `8.30.1` binary runs and reports `8.30.1` via `gitleaks version`.
**How to avoid:** No action needed — pin `8.30.1` as 05-CONTEXT.md already measured. Documented here only so a future contributor who finds gitleaks#2086 doesn't assume it blocks this repo.
**Warning signs:** None expected; flag only if a future `mise install` for gitleaks starts failing, at which point re-check whether `aqua`'s resolution strategy changed.

### Pitfall 4: `zizmor --offline` cannot fetch data some pedantic findings might need
**What goes wrong:** `[tasks.lint]` already runs `zizmor --offline .github/workflows` (`.mise.toml:26`); adding `--persona=pedantic` must not silently drop the `--offline` flag.
**Why it happens:** Some zizmor audits (not the three this phase's inventory names) need network access for *auto-fix* suggestions specifically; detection itself for `anonymous-definition`, `undocumented-permissions`, and `concurrency-limits` all work offline per their own doc tables [CITED: docs.zizmor.sh/audits/ — all three list "Works offline: ✅"].
**How to avoid:** Change the line to `zizmor --offline --persona=pedantic .github/workflows`, keeping both flags (D-08 already specifies this exact command).
**Warning signs:** A CI run that behaves differently locally vs. in the lint job (network access differs) — should not happen given all three relevant audits are offline-capable, but worth a spot-check after the change.

## Code Examples

### Querying a commit's `ci-success` check run
```bash
# Source: https://docs.github.com/rest/checks/runs
# The filter is a query-string parameter. Passing it as a field flag makes
# gh send the request as a POST, which 404s — see the measurement in Pattern 1.
gh api "repos/${GH_REPO}/commits/${SHA}/check-runs?check_name=ci-success"
```
`filter` defaults to `latest`, which "returns the most recent check runs and all pending check runs for a given check name" — a re-run of `ci-success` on the same SHA does not produce a duplicate the gate has to reason about.

### Adding the `checks: read` permission
```yaml
# release.yml — job-level permissions (job-level fully replaces workflow-level)
permissions:
  contents: write   # gh release create
  checks: read       # release-gate.sh: read the ci-success check run
```

### A minimal `.gitleaks.toml`
```toml
[extend]
useDefault = true

[[allowlists]]
description = "Deliberate test fixtures (05-CONTEXT.md D-07)"
regexes = [
  '''0123456789abcdef''',
  '''sentinel-api-key-''',
]
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|---------------|--------|
| `.gitleaksignore` fingerprint files for suppression | `.gitleaks.toml` `[[allowlists]]` with `regexTarget` | `regexTarget` support has existed since early gitleaks 8.x (PR #1107) | D-06 already chose the allowlist route for exactly the reasons the ecosystem has converged on — fingerprints are commit-pinned and brittle |
| `--staged`-only pre-commit gitleaks scans | Full-history `gitleaks git` scans in CI and pre-commit | N/A — this is a project-specific choice (D-05), not an ecosystem shift | A `--staged` scan in CI (which has nothing staged) is a known anti-pattern; this repo avoids it from the start |

**Deprecated/outdated:** none identified — every tool and API surface this phase touches (gitleaks 8.30.1, zizmor 1.30.1, GitHub's check-runs REST endpoint) is current.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | GitHub-hosted `ubuntu-latest` runners ship a `gh` CLI version that accepts a query string on a `gh api` path argument | Standard Stack | Low — `gh api <path>` with an inline query string is the oldest and plainest form of the command, and this repo does not pin a runner image version. The gate passes no field flag, so `gh`'s method-selection behavior (the 404 measured in Pattern 1) is out of the picture entirely |
| A2 | `gitleaks git` invoked from the repository root without `--source` treats `.` as the target path, so `.gitleaks.toml` at the repo root is auto-discovered under rule 4 of the config precedence order | Architecture Patterns, Pitfall 2 | Low — directly read from gitleaks' own `cmd/root.go` source this session, but the exact invocation form the planner chooses for `mise run lint`'s new line should still be spot-checked (e.g. `gitleaks git -v` run once locally) before relying on it in CI |

## Open Questions

1. **Exact bats coverage for `scripts/pre-public-audit.sh`**
   - What we know: 05-CONTEXT.md leaves this to the planner's discretion; the audit's history-scan item reuses `mise run lint`'s gitleaks invocation (D-14) rather than a second scanner, and the rest of the script mostly enumerates GitHub API resources (workflow runs, artifacts, issues, PRs, releases) for human review rather than making pass/fail judgments about their content.
   - What's unclear: Whether "enumerate for human review" items need any bats coverage beyond the usage/unknown-action convention tests every `scripts/` file gets, since their correctness is about what a human sees, not an assertable exit code.
   - Recommendation: At minimum, cover the usage/unknown-action pair (matching `package.bats:20-30`'s convention) and the reused-gitleaks-scan item (which does have a clean pass/fail). Treat the enumerate-for-review items as out of scope for automated assertions — their "correctness" is that they printed, not that their content passed a check.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| gitleaks | REL-03, PUB-01 | ✓ (installed this session via `mise use gitleaks@8.30.1`) | 8.30.1 | — |
| zizmor | REL-02 | ✓ | 1.30.1 (already pinned, `.mise.toml:8`) | — |
| gh CLI | REL-01, PUB-01, PUB-03 | ✓ | 2.97.0 locally; GitHub-hosted runners ship a current build | — |
| bats | REL-01 tests | ✓ | 1.14.0 (already pinned) | — |
| Branch protection requiring `ci-success` | Deferred (post-PUB-03) | ✗ | — | Not needed this phase; `gh api .../branches/main/protection` returns HTTP 403 today ("Upgrade to GitHub Pro or make this repository public") — already verified live during 05-CONTEXT.md's discussion, re-confirmed as a known limit here |

**Missing dependencies with no fallback:** none — everything this phase's success criteria depend on is available today.
**Missing dependencies with fallback:** Branch protection is unavailable until PUB-03 lands; D-01 through D-03 already design the gate to not depend on it, so no fallback is needed within this phase.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | bats 1.14.0 (shell scripts); no new framework needed |
| Config file | none — bats test files are self-contained `.bats` files under `tests/scripts/`, matching the existing `package.bats` shape |
| Quick run command | `bats tests/scripts/release-gate.bats` |
| Full suite command | `mise run test` (already runs `bats tests/scripts` — new `.bats` files are picked up automatically) |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| REL-01 | `release-gate.sh` refuses a tag whose commit has no passing `ci-success` (missing, in-progress, `cancelled`, `failure`, `skipped`, `timed_out`, or any non-`success` conclusion) and passes when `status=="completed"` and `conclusion=="success"` | unit (bats, fake `gh` on `PATH`, per D-04) | `bats tests/scripts/release-gate.bats` | ❌ Wave 0 |
| REL-02 | `zizmor --offline --persona=pedantic .github/workflows` reports zero findings | integration (CI job, not bats — zizmor is the assertion) | `mise run lint` | N/A — covered by existing lint task once the persona flag lands |
| REL-03 | `gitleaks git` (via `mise run lint`) exits 0 against both history and working tree with the new `.gitleaks.toml` in place | integration (CI job) | `mise run lint` | N/A — covered by existing lint task once the gitleaks line and `.gitleaks.toml` land together |
| DOCS-03 | README Compatibility section states tested Jellyfin/Emby versions and the `targetAbi` floor-not-ceiling fact | manual-only (prose) — no automated assertion exists for README wording today | — | — |
| PUB-01 | `pre-public-audit.sh` re-runs the history-scan (reusing REL-03's own check, D-14) and enumerates Actions logs/artifacts, issues/PRs, and release notes for human review | unit (bats, at minimum usage/unknown-action) + manual (content review, per Open Question 1) | `bats tests/scripts/pre-public-audit.bats` | ❌ Wave 0 |
| PUB-03 | Repository visibility is `public`, verified after the maintainer's manual switch | manual-only, post-execution verification | `gh repo view --json visibility` | N/A — cannot be automated inside the execution run per D-15 |

### Sampling Rate
- **Per task commit:** `bats tests/scripts/release-gate.bats` (and `pre-public-audit.bats` once it exists) plus `mise run lint` for the zizmor/gitleaks changes
- **Per wave merge:** `mise run test` (full bats suite) and `mise run lint`
- **Phase gate:** Full `mise run lint` and `mise run test` green before `/gsd-verify-work`; PUB-03 is verified separately, after the maintainer's manual switch, via `gh repo view --json visibility`

### Wave 0 Gaps
- [ ] `tests/scripts/release-gate.bats` — covers REL-01, using a fake `gh` on `PATH` per D-04 (fixture JSON for: no matching check run, in-progress, `cancelled`, `failure`, `skipped`, `timed_out`, and `success`)
- [ ] `tests/scripts/pre-public-audit.bats` — covers PUB-01's automatable slice (usage/unknown-action, and the reused-gitleaks-scan item); see Open Question 1 for scope
- Framework install: none — bats is already pinned and already wired into `mise run test`

## Security Domain

### Applicable ASVS Categories

This phase has no application-level attack surface (no user input, no authentication flow, no session) — it hardens the *build and release pipeline* instead. Most OWASP ASVS V-categories (V2 Authentication, V3 Session Management, V4 Access Control, V5 Input Validation) do not apply to shell scripts and workflow YAML. The relevant category is configuration/deployment hardening:

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | — |
| V3 Session Management | no | — |
| V4 Access Control | no | — |
| V5 Input Validation | no | — |
| V6 Cryptography | no | — |
| V14 Configuration | yes | Least-privilege `permissions:` blocks per job (already the repo's pattern — `release.yml:11-12` workflow-level `contents: read`, job-level overrides), pinned-to-SHA actions (`~/.claude/rules/github-actions.md`), and a static analyzer (`zizmor`) enforced in CI rather than reviewed ad hoc |

### Known Threat Patterns for GitHub Actions CI/CD (this phase's actual domain)

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Overscoped `GITHUB_TOKEN` (e.g. granting `contents: write` when only `checks: read` is needed) | Elevation of Privilege | Per-job `permissions:` blocks, one scope per actual need — this phase adds exactly `checks: read` to the `release` job, nothing broader |
| Secrets committed to git history, never rotated | Information Disclosure | `gitleaks git` full-history scan in both the pre-commit hook and CI (REL-03), plus the pre-publication history re-scan (PUB-01) before the repository's history becomes world-readable |
| A release published from a commit whose CI never actually passed (e.g. a `cancelled` run mistaken for "fine") | Tampering | `release-gate.sh`'s exact-match on `status=="completed" && conclusion=="success"` (REL-01/D-02/D-03) — every other state fails closed |
| An in-flight release interrupted mid-upload by a concurrency cancellation, leaving a partial GitHub release | Denial of Service (self-inflicted) | `release.yml`'s new concurrency group uses `cancel-in-progress: false` (D-10) — releases queue, they are never killed |
| Undocumented `permissions:` grants accumulating unreviewed elevated scopes over time | Elevation of Privilege (process, not immediate) | zizmor's `undocumented-permissions` pedantic audit requires an inline comment justifying every explicit permission grant (REL-02) |

## Sources

### Primary (HIGH confidence)
- github.com/gitleaks/gitleaks/releases/tag/v8.30.1 — confirmed newest release; asset download counts
- github.com/gitleaks/gitleaks (README, `cmd/root.go`, `config/gitleaks.toml`) — config precedence order, `regexTarget` semantics, default `--exit-code`
- docs.github.com/rest/checks/runs — check-runs endpoint, `check_name`/`filter` query params, GitHub App `checks:read` permission requirement
- docs.zizmor.sh/audits/ and docs.zizmor.sh/usage/ — `anonymous-definition`, `undocumented-permissions`, `concurrency-limits` audit definitions and persona behavior
- Local tool verification this session: `mise ls-remote gitleaks`, `mise use gitleaks@8.30.1`, `gitleaks version`, `gh --version`, `mise registry | grep gitleaks`
- In-repo files read this session: `.github/workflows/release.yml`, `.github/workflows/ci.yml`, `.mise.toml`, `scripts/package.sh`, `tests/scripts/package.bats`, `README.md`, `.pre-commit-config.yaml`, `docs/development.md`

### Secondary (MEDIUM confidence)
- github.com/gitleaks/gitleaks/issues/1878 — `[[allowlists]]` vs `[[rules.allowlists]]` scoping, community-confirmed by a gitleaks maintainer (`@rgmz`) in the thread
- github.com/gitleaks/gitleaks/issues/2086 — the v8.30.1 orphaned-tag issue and its actual (narrow) blast radius

### Tertiary (LOW confidence)
- none — every claim in this document traces to an official doc, an official repository, or a command run in this session

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — every tool version is pinned in-repo or verified by a local command run this session
- Architecture: HIGH — the check-runs API surface and the gitleaks allowlist syntax are both documented and were exercised (installed/run) this session; the only unexercised piece is the exact shell script content, which is intentionally left as a pattern, not a mandate, per 05-CONTEXT.md's discretion list
- Pitfalls: HIGH — each pitfall traces to a specific doc citation or an empirical check this session

**Research date:** 2026-09-20
**Valid until:** 30 days (stable ecosystem; re-check gitleaks/zizmor versions if planning is delayed past mid-October 2026)
