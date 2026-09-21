---
phase: 05-public-repository
verified: 2026-09-20T23:50:00-07:00
status: human_needed
score: 4/5 must-haves verified
behavior_unverified: 0
overrides_applied: 0
human_verification:
  - test: "Run `gh repo edit --visibility public --accept-visibility-change-consequences` personally, at the time you choose, after reading the 05-05-SUMMARY.md audit results."
    expected: "`gh repo view --json visibility` reports `public`. Branch protection requiring `ci-success` then becomes available for the first time (returns 403 today)."
    why_human: "PUB-03/roadmap success criterion 5 requires the maintainer's explicit approval at the time of the change. This is a project constraint (D-15), not something an automated check can satisfy or that this verifier may act on. Confirmed independently: `gh repo view --json visibility` currently reports `PRIVATE`."
deferred:
  - truth: "The Phase 5 code review's remaining open warnings (WR-03, WR-04, WR-05, WR-08, WR-10 partially fixed, WR-12, WR-13, WR-14) and info items (IN-01, IN-02) are resolved"
    addressed_in: "Not scheduled to a specific later phase; recorded in 05-REVIEW.md and left open by deliberate scope decision"
    evidence: "None of these findings map to a roadmap Phase 5 success criterion (WR-10, the audit's lack of documentation, was in fact fixed in commit 67f7fea; docs/development.md:16,22 now describe scripts/pre-public-audit.sh). The remaining items concern release-workflow edge cases (WR-03, WR-04, WR-12), audit report ergonomics (WR-05, WR-08, WR-14), an online-zizmor gap (WR-13), and minor test robustness (IN-01, IN-02) — none of which the roadmap's five success criteria for this phase require."
---

# Phase 5: Public Repository Verification Report

**Phase Goal:** The repository is safe to make public, and then it is public. Releases require a passing CI run, the workflows and the history have no open findings, and the maintainer approves the switch at the time of the change.
**Verified:** 2026-09-20T23:50:00-07:00
**Status:** human_needed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths (Roadmap Success Criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | A `v*` tag on a commit with no passing `ci-success` check run stops the release workflow before it publishes a release | ✓ VERIFIED | `scripts/release-gate.sh` wired into `.github/workflows/release.yml:46-51` between the tag check and `mise run test`, no `continue-on-error`. 25 bats tests in `tests/scripts/release-gate.bats` (all passing, re-run live) cover every D-03 refusal state (missing/`in_progress`/`cancelled`/`failure`/`skipped`/`timed_out`/unenumerated conclusion/multiple runs/403/422/wrong-named run) plus the post-review WR-01 (name filtered in `jq`, not trusted from the query string alone) and WR-02 (commit must be `identical`/`behind` the default branch, refusing a passing check run from an unmerged PR head) fixes. Live spot-check: `scripts/release-gate.sh check` against `main`'s HEAD (`ecee1ed`) exits 0; against a random unknown SHA it correctly fails closed with a 422-distinct message. The full live pass-path rehearsal is explicitly Phase 6's job per the roadmap note. |
| 2 | Each `zizmor --persona=pedantic` finding is fixed or suppressed with a written reason, and `mise run lint` runs gitleaks, so the pre-commit hook and CI check for secrets | ✓ VERIFIED | `zizmor --offline --persona=pedantic .github/workflows` → "No findings to report" (re-run live). All 5 `anonymous-definition` + 1 `concurrency-limits` findings were fixed outright (job `name:` fields, `release.yml` concurrency block); zero suppressions used. Two real defects the SUMMARYs originally missed were found by 05-REVIEW.md and are now fixed and regression-tested: CR-01 (CI's `lint` job had no `fetch-depth: 0`, so `gitleaks git` in CI scanned 1 commit — fixed in `ci.yml:30`, pinned by `tests/scripts/gitleaks.bats` tests 7-8) and CR-02 (the pre-commit `lint`/`test` hooks were gated by a `files:` regex that skipped `.md`/`.json`/`.planning/`, so a docs-only commit ran no secret scan at all — fixed by a new `always_run: true` `secrets` hook in `.pre-commit-config.yaml:10-15` backed by `[tasks.secrets]` in `.mise.toml`, pinned by `tests/scripts/gitleaks.bats` test 9). Live re-verification: `prek run --files docs/development.md` shows `mise run secrets` reports `Passed` while the file-scoped `lint`/`test` hooks report `Skipped` — confirms the fix holds for exactly the failure mode the review found. `mise run lint` itself: 223 commits scanned, no leaks found. |
| 3 | `README.md` states the tested Jellyfin and Emby versions, and that `targetAbi` sets only the minimum Jellyfin version | ✓ VERIFIED | `README.md:18-24` `## Compatibility` section states the tested images (Jellyfin `12.1.20260915-010956`, Emby `4.10.0.40`), defines `targetAbi` (`12.1.0.0`) as a floor with no ceiling, and explicitly says "Installing is not the same as supported." WR-09 (the Status line at the top of the README still named the untested `12.1.0` claim the Compatibility section was written to correct) was found and fixed — `README.md:10` now names the actual tested image tag and links to Compatibility. |
| 4 | Before the visibility change, gitleaks scans the full git history, and the Actions logs and artifacts, the issue and PR text, and the release notes are reviewed. Every finding is removed or rotated | ✓ VERIFIED | `scripts/pre-public-audit.sh` reuses `mise run lint` for the secret-scan verdict (D-14) and enumerates the remaining five surfaces via read-only `gh api`/`gh repo view` calls. CR-03 (every enumeration read only the GitHub API's default 30-item page and reported that as the total) was found and fixed with `--paginate --slurp`, pinned by a two-page fixture test (`tests/scripts/pre-public-audit.bats`, "workflow-runs count and listing reflect a second page"). I independently re-ran the now-corrected script live against the real repository and it reproduces exactly the counts recorded in `05-05-SUMMARY.md` (9 workflow runs — 7 success, 2 cancelled; 0 artifacts; 0 issues; 10 pull requests; 0 releases), so the pre-fix audit run recorded in the SUMMARY was not invalidated by the pagination bug for this repository's actual (sub-30) surface. All ten PR titles/bodies and all nine workflow runs were read and recorded in 05-05-SUMMARY with a conclusion per item; no credential-shaped string exists in that SUMMARY (independently re-checked). |
| 5 | The repository is public. The maintainer gave explicit approval at the time of the change, after criteria 1 and 4 were complete | ✗ NOT MET — by maintainer decision, not a defect | `gh repo view --json visibility` (run live during this verification) reports `PRIVATE`. Plan 05-05's Task 3 is a `checkpoint:decision gate="blocking-human"` by design (D-15): no agent may run the visibility command, and the SUMMARY records the maintainer chose to hold after being shown the completed audit and the exact command. Criteria 1 and 4 are otherwise complete, so the checkpoint is correctly positioned — the switch is a human action outside the scope of any automated check. |

**Score:** 4/5 truths verified (0 present-but-behavior-unverified). Truth 5 is neither FAILED nor VERIFIED — it is a design-intended human checkpoint, and it is the reason this report's status is `human_needed` rather than `passed`.

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|---|---|---|---|---|
| REL-01 | 05-01 | Release workflow requires passing `ci-success` on the tagged commit | ✓ SATISFIED | `scripts/release-gate.sh` + wiring in `release.yml`, 25 bats tests |
| REL-02 | 05-02 | Pedantic zizmor findings fixed or suppressed with a written reason | ✓ SATISFIED | `zizmor --offline --persona=pedantic` clean, no suppressions |
| REL-03 | 05-03 | gitleaks in `mise run lint`, checked by pre-commit and CI | ✓ SATISFIED | `[tasks.secrets]`/`[tasks.lint]`, CR-01/CR-02 fixes confirmed live |
| DOCS-03 | 05-04 | README states tested versions and `targetAbi` semantics | ✓ SATISFIED | `README.md` Compatibility section, WR-09 fix confirmed |
| PUB-01 | 05-05 | Full-history gitleaks scan + human review of Actions/issues/PRs/releases before publication | ✓ SATISFIED | `scripts/pre-public-audit.sh`, CR-03 pagination fix confirmed, live re-run matches SUMMARY |
| PUB-03 | 05-05 | Repository is public, maintainer approved at time of change | ✗ PENDING — by design | `gh repo view --json visibility` = `PRIVATE`; checkpoint correctly stopped the phase in the maintainer's hands; REQUIREMENTS.md correctly lists PUB-03 as the only Pending item for Phase 5 |

No orphaned requirements: REQUIREMENTS.md's Phase 5 row set (REL-01, REL-02, REL-03, DOCS-03, PUB-01, PUB-03) matches exactly the `requirements:` fields declared across the five PLAN.md frontmatter blocks.

### Artifacts

| Artifact | Expected | Status | Details |
|---|---|---|---|
| `scripts/release-gate.sh` | REL-01 gate, one action `check` | ✓ VERIFIED | Executable, 25 bats tests pass, wired into `release.yml`, live spot-checked against `main` HEAD and an unknown SHA |
| `tests/scripts/release-gate.bats` | Coverage of every refusal state + WR-01/WR-02 regressions | ✓ VERIFIED | 25/25 passing |
| `.github/workflows/ci.yml` | Named jobs, `ci-success` byte-identical to job id, full-history checkout on `lint` | ✓ VERIFIED | `name: ci-success` confirmed, `fetch-depth: 0` on `lint` job only (CR-01 fix), `test`/`e2e` stay shallow (pinned by tests) |
| `.github/workflows/release.yml` | Gate step, `checks: read`, never-cancelling concurrency, ancestry check | ✓ VERIFIED | All present, `actionlint` and `zizmor` clean |
| `.gitleaks.toml` | Value-only allowlist for the five known fixtures | ✓ VERIFIED (deviates from the 05-03 PLAN's literal "no `paths=`" text, in a way that strengthens the property that text was protecting — see Note below) | `useDefault = true`, one global `[[allowlists]]` table now scoped by `paths` (`^tests/`, `^\.planning/`), `targetRules = ["generic-api-key"]`, `condition = "AND"`, plus the two fixture `regexes` |
| `.mise.toml` | `[tasks.secrets]` (full-history scan), `[tasks.lint]` includes it, pedantic zizmor | ✓ VERIFIED | `mise run lint` exits 0, 223 commits scanned live |
| `.pre-commit-config.yaml` | Unconditional secret scan | ✓ VERIFIED | New `secrets` hook, `always_run: true`, no `files:` key (CR-02 fix) |
| `README.md` | Compatibility section, corrected Install sentences, corrected Status line | ✓ VERIFIED | All three present, WR-09 fix confirmed |
| `scripts/pre-public-audit.sh` | PUB-01 audit, one action `run` | ✓ VERIFIED | Executable, 8 bats tests pass, paginates (CR-03 fix), live re-run matches SUMMARY, documented in `docs/development.md` (WR-10 fix) |
| `tests/scripts/pre-public-audit.bats` | Usage, PASS/FAIL split, zero-lines, pagination | ✓ VERIFIED | 8/8 passing |

**Note on `.gitleaks.toml`:** The 05-03 PLAN's must-haves and negative acceptance criteria explicitly required no `paths=` key and no per-rule scoping ("it exempts no file path and no directory (D-07)"). A pre-review commit (`bb9da23`, landed before `05-REVIEW.md` was written) added exactly that — `paths`, `targetRules`, `condition = "AND"` — because the unscoped allowlist matched the two fixture regexes *anywhere in the repository*, including hypothetically in `src/`, which is a bigger loophole than the one D-07 was written to close. The current, narrower form is proven by `tests/scripts/gitleaks.bats` to still report a fixture-shaped value found in `src/` (test 3) and a genuine non-fixture secret found inside `tests/` (test 4), and to still report a fixture value that trips a different rule (test 5) — so the safety property REL-03's prohibition actually cares about ("MUST NOT allowlist a value that is or was a live credential") holds, even though the literal plan text it deviates from does not. This is flagged for the record, not as a gap.

### Anti-Patterns Found

None. `TBD`/`FIXME`/`XXX`/`TODO`/`HACK`/`PLACEHOLDER` grep across every file this phase touched returned no matches.

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|---|---|---|---|
| Release gate accepts a known-good commit | `scripts/release-gate.sh check ecee1ed` (main HEAD) | exit 0 | ✓ PASS |
| Release gate refuses an unknown SHA | `scripts/release-gate.sh check 0332009...` | exit 1, "GitHub API call ... failed" (422) | ✓ PASS |
| Pre-commit hook scans secrets on a docs-only change | `prek run --files docs/development.md` | `mise run secrets` → Passed; `lint`/`test` → Skipped | ✓ PASS — confirms CR-02 fix live |
| Full lint gate | `mise run lint` | exit 0, 223 commits scanned, no leaks, zizmor "No findings to report" | ✓ PASS |
| Full test suite | `mise run test` | dotnet test 0 failed; 50 bats assertions; 58 node tests | ✓ PASS |
| Pre-publication audit against the live repo | `scripts/pre-public-audit.sh run` (re-run live, post-CR-03-fix) | exit 0, counts match `05-05-SUMMARY.md` exactly | ✓ PASS |
| Repository visibility | `gh repo view --json visibility` | `PRIVATE` | as expected — matches orchestrator-confirmed state |
| Full bats suites | `bats tests/scripts/release-gate.bats` / `gitleaks.bats` / `pre-public-audit.bats` | 25/25, 9/9, 8/8 | ✓ PASS |

`mise run e2e` was intentionally not run: this phase touched no file under `src/`, `e2e/`, `tests/Jellyfin.Plugin.EmbyAuth.Tests/`, or `tests/js/`, and the project rule scopes e2e runs to `src/` changes. Not a gap.

### Gaps Summary

No gaps in the built work. The only unmet roadmap success criterion (5) and the only Pending requirement (PUB-03) are unmet by explicit, recorded maintainer decision — the checkpoint the phase was designed to stop at. Everything the phase could build before that decision is built, tested, and — where the code review found real defects the task SUMMARYs had missed (CR-01, CR-02, CR-03, WR-01, WR-02, WR-06, WR-07, WR-09, WR-11) — those defects are now fixed and pinned by new regression tests that I re-ran and confirmed pass. A residual set of lower-severity review findings (WR-03, WR-04, WR-05, WR-08, WR-12, WR-13, WR-14, IN-01, IN-02) remain open by deliberate scope decision recorded in `05-REVIEW.md`; none of them maps to a roadmap success criterion for this phase.

### Human Verification Required

1. **Run the visibility command personally, when ready.**
   **Test:** `gh repo edit --visibility public --accept-visibility-change-consequences`
   **Expected:** `gh repo view --json visibility` subsequently reports `public`; PUB-03 closes; Phase 6 (GitHub Pages manifest, rehearsal release) unblocks.
   **Why human:** D-15 requires the maintainer's own action, at the time of the change — not an agent's, and not a checkpoint reply given in advance. This verifier independently confirmed the repository is currently `PRIVATE` and did not run, and will not run, this command.

---

*Verified: 2026-09-20T23:50:00-07:00*
*Verifier: Claude (gsd-verifier)*
