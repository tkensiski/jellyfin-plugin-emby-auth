# Phase 5: Public Repository - Context

**Gathered:** 2026-09-20
**Status:** Ready for planning

<domain>
## Phase Boundary

This phase makes the repository safe to publish, and then it is published. It covers REL-01 (the release workflow refuses a tag whose commit has no passing `ci-success`), REL-02 (every `zizmor --persona=pedantic` finding fixed or suppressed with a written reason), REL-03 (gitleaks runs in `mise run lint`), DOCS-03 (`README.md` states the tested Jellyfin and Emby versions and what `targetAbi` means), PUB-01 (the audit before the visibility change), and PUB-03 (the repository is public, with the maintainer's approval at that time).

In scope: a new `scripts/release-gate.sh` with its bats tests, a gate step in `.github/workflows/release.yml`, a `.gitleaks.toml` allowlist, the gitleaks and pedantic-zizmor changes to `[tasks.lint]` in `.mise.toml`, job names and a concurrency group in both workflows, a new `scripts/pre-public-audit.sh`, the README Compatibility section, and the correction of the README sentences that say the repository is private.

Out of scope, and reserved for Phase 6: the GitHub Pages manifest and its workflow (PUB-02), the per-release `CHANGELOG.md` entries (REL-04), the manifest URL and catalog install steps in the README (DOCS-02), the version bump rule in `CLAUDE.md` (DOCS-04), the rehearsal release 0.9.0.0 and version 1.0.0.0 (PUB-04, PUB-05). Also out of scope: any change to `src/`, to the plugin's behavior, or to the e2e suite.

</domain>

<decisions>
## Implementation Decisions

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

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase requirements and prior decisions

- `.planning/ROADMAP.md` §Phase 5 — the goal and the five success criteria.
- `.planning/REQUIREMENTS.md` — REL-01, REL-02, REL-03, DOCS-03, PUB-01, PUB-03, and the Phase 6 entries that bound this phase's scope (REL-04, PUB-02, PUB-04, PUB-05, DOCS-02, DOCS-04).
- `.planning/PROJECT.md` §Constraints — publishing cannot be undone for anyone who copied the repository; the history scan comes first and the switch needs explicit approval at that time. §Key Decisions — the release checks the CI result of the tagged commit. §Out of Scope — re-running lint and e2e inside `release.yml` was rejected, and so was a private repository with a separate public release repository.
- `.planning/phases/03-migration-status-and-target/03-CONTEXT.md` — D-18, the plugin never claims behavior it has not verified, which D-18 here applies to the README.
- `.planning/STATE.md` §Blockers/Concerns — the research flag that tags are assumed to be pushed from `main` behind the CI gate. D-02 and D-03 resolve what the gate does; branch protection itself remains unavailable until the repository is public (see below).

### Repository rules

- `CLAUDE.md` — one mise task per CI job, so a CI step change edits the mise task and not only the workflow; pin exact versions and look up the current stable version before a bump; run `prek run` before each commit; warnings are errors.
- `~/projects/CLAUDE.md` §Conventions — scripts require an explicit action argument and never default to a mutating operation; anything repeatable belongs in the repository's `scripts/`, tracked and shellcheck-clean.
- `~/.claude/rules/github-actions.md` — pin actions to full SHAs with a version comment, set `persist-credentials: false`, scan with zizmor before committing.

### Files this phase changes

- `.github/workflows/release.yml` — `:11` the top-level `contents: read`; `:19` the `contents: write` grant that REL-02 wants documented; `:32-35` the existing `check-tag` step the gate joins; the file has no concurrency block, which D-10 adds.
- `.github/workflows/ci.yml` — `:14-17` the concurrency block with `cancel-in-progress: true` that makes a `cancelled` conclusion reachable; `:19`, `:34`, `:49`, `:65` the four unnamed jobs; `:65-81` the `ci-success` aggregate whose job id is the contract D-03 matches on.
- `.mise.toml` — `[tasks.lint]` at `:19-27`, which gains gitleaks and the pedantic persona; `[tools]` at `:1-11`, which gains a pinned gitleaks. Look up the current stable gitleaks at plan time; `8.30.1` was the newest `mise ls-remote` offered during this discussion and is what every measurement here was taken with.
- `scripts/package.sh` — `:8` and `:26` the action-argument and usage convention the new scripts follow; `:10-13` the environment-override convention; `:33-38` `target_abi()`, the mechanism D-18 documents.
- `tests/scripts/package.bats` — `:1-18` the `setup_file` and `setup` shape the new bats file follows; `:20-30` the usage-and-unknown-action tests every script in `scripts/` is expected to have; `:47` and `:61` the `targetAbi` assertions.
- `README.md` — `:10` the Status line that already carries the tested versions; `:12-16` the Requirements section; `:21` and `:33` the sentences D-19 corrects; the new Compatibility section from D-17.
- `.gitleaks.toml` — new, at the repository root, where gitleaks finds it without a `--config` flag.
- `scripts/release-gate.sh` and `scripts/pre-public-audit.sh` — new.
- `.pre-commit-config.yaml` — the `lint` hook's `files` pattern at the `local` repo block; check whether a new root `.gitleaks.toml` and the new `scripts/` files are matched by the existing patterns.

### Verified live during this discussion

- **Branch protection is unavailable on this repository today.** `gh api repos/tkensiski/jellyfin-plugin-emby-auth/branches/main/protection` returns HTTP 403, "Upgrade to GitHub Pro or make this repository public to enable this feature." The gate therefore cannot depend on branch protection, and branch protection can only be configured after PUB-03. This confirms D-01 through D-03's self-contained design and resolves the `STATE.md` research flag.
- **Check-run names are job ids today.** The commits API for `ecee1ed` returns exactly `ci-success`, `e2e`, `test`, `lint`. This is the evidence behind D-09.
- **The pedantic finding set is 7**, listed in full under D-08 through D-10. The default persona reports zero findings with 7 suppressed.
- **gitleaks over the full history takes 0.5 s and reports 5 findings; the working tree reports 4.** All are test fixtures. A `.gitleaks.toml` value-regex allowlist clears both to exit 0. gitleaks exits 1 on any finding.
- **The audit surface is small:** 204 commits, 9 workflow runs, 0 Actions artifacts, 0 issues, 0 releases, 10 pull requests (numbers 1 to 10, all predating this milestone, titles benign).
- **`gh repo edit` requires `--accept-visibility-change-consequences` whenever `--visibility` is used.** This is the exact command D-15 hands to the maintainer.
- **`gitleaks 8.30.1` subcommands are `dir`, `git`, and `stdin`**, with `--staged` and `--pre-commit` as flags on `git`. Suppression mechanisms are the config allowlist, `--baseline-path`, `.gitleaksignore`, and inline `gitleaks:allow` comments.

### Research

- `.planning/research/SUMMARY.md:71` — `targetAbi` is a minimum, not a maximum (Jellyfin issue #11331); untested newer Jellyfin versions can still install the plugin. `:88` — the research flag about tags and the CI gate.
- `.planning/research/FEATURES.md:19` — Jellyfin filters versions with `Version.Parse(x.TargetAbi) <= appVer`, read from `InstallationManager.GetCompatibleVersions`.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets

- `scripts/package.sh` — the shape every new script in `scripts/` copies: a usage function that exits 2, a required action argument, `REPO_ROOT` resolved from `BASH_SOURCE`, and environment variables for anything a test needs to control.
- `tests/scripts/package.bats` — the bats conventions the gate's test file follows, including the two tests that assert a missing action and an unknown action both print usage and exit 2.
- `.github/workflows/ci.yml:65-81` — `ci-success` already aggregates every job and fails when any did not succeed, so D-03's single-name match needs no new CI structure.
- `.mise.toml` `[tasks.lint]` — already chains five tools in one task, so adding gitleaks is an entry in an existing list rather than a new task or a new CI job. This keeps the `CLAUDE.md` one-mise-task-per-CI-job rule intact.

### Established Patterns

- Every action in both workflows is pinned to a full SHA with a trailing version comment, and every checkout sets `persist-credentials: false`. New steps match.
- CI job names, mise task names, and the pre-commit hook ids line up, so `mise run lint` locally reproduces the `lint` job exactly. Any lint change must keep that true.
- `.pre-commit-config.yaml` gates each hook on a `files` regex rather than running everything on every commit.
- Scripts in `scripts/` never default to a mutating action; the action argument is required and an unknown one exits 2.

### Integration Points

- `release.yml` gains the gate step and a concurrency block; `ci.yml` gains job names only. The gate's job needs permission to read check runs, which the current `contents: read` does not grant.
- `.mise.toml` gains a pinned gitleaks in `[tools]` and two changes in `[tasks.lint]` — the gitleaks invocation, and the pedantic persona on the existing zizmor line.
- A new root `.gitleaks.toml` must land in the same commit as the lint change, or lint fails on the four working-tree fixtures.
- `.pre-commit-config.yaml`'s `lint` hook `files` pattern decides whether editing `.gitleaks.toml` or the new scripts re-runs lint.
- `README.md` gains a section and loses two stale sentences; `docs/development.md:29-33` describes the release procedure and should be checked against the gate's new refusal behavior.

</code_context>

<specifics>
## Specific Ideas

- The maintainer asked "wtf is targetAbi" during the discussion, and the answer shaped D-17 and D-18: it is a floor with no ceiling, so a Jellyfin 13 administrator will be offered this plugin by the catalog and Jellyfin will install it, with nobody having tested that. The README's job is to stop "it installed" being read as "it is supported". Write the section for a reader who has never heard the term.
- REL-02 permits a suppression with a written reason, and the pedantic set was measured at 7 findings that are all fixable without one. If the planner finds itself writing a suppression, that is a signal to re-check the finding rather than a licence to suppress.
- D-05 was chosen partly so that PUB-01's history scan is not a separate one-time event. Prefer this shape generally in this phase: where an existing continuous check can carry an audit item, let it, rather than adding a parallel check that can drift.
- The maintainer holds the visibility change (D-15). Do not write a task, a script action, or a checkpoint that runs `gh repo edit --visibility`. The phase's last deliverable is a completed audit report and the command for the maintainer to run.

</specifics>

<deferred>
## Deferred Ideas

- **Branch protection requiring `ci-success` on `main`** — impossible today (verified: HTTP 403, needs GitHub Pro or a public repository) and therefore not a Phase 5 deliverable. It becomes available the moment PUB-03 lands. D-09 deliberately preserves the `ci-success` check-run name so the rule can be added later without touching the gate. Worth raising after the switch.
- **A `SECURITY.md`, issue templates, or a contributing guide** — raised as a candidate at the final gate and not selected. Strangers can file issues the moment the repository is public, and the repository has none of these today. Not in any requirement; belongs in its own phase or a quick task.
- **`release-gate.sh` also verifying the tag is an ancestor of `main`** — raised and not selected. D-03's `ci-success` check already proves the commit passed the suite; an ancestry check would additionally prove it went through the branch, which is what branch protection will do properly once it is available.
- **What becomes of the pushed `gsd/phase-*` branches once the repository is public** — raised and not selected. They are published by D-11's keep-everything decision. Tidying them is housekeeping, not a publication blocker.
- **Retiring `scripts/pre-public-audit.sh` after it has served** — the "one-time gate becomes dead code" objection was raised against D-13 and the maintainer chose the script anyway. If it is still unused well after v1.0.0, the repository's own "replace, don't deprecate" rule says delete it rather than leave it.

</deferred>

---

*Phase: 5-Public Repository*
*Context gathered: 2026-09-20*
