---
phase: 05-public-repository
reviewed: 2026-09-21T06:03:44Z
depth: standard
files_reviewed: 12
files_reviewed_list:
  - .github/workflows/ci.yml
  - .github/workflows/release.yml
  - .gitleaks.toml
  - .mise.toml
  - .pre-commit-config.yaml
  - docs/development.md
  - README.md
  - scripts/pre-public-audit.sh
  - scripts/release-gate.sh
  - tests/scripts/gitleaks.bats
  - tests/scripts/pre-public-audit.bats
  - tests/scripts/release-gate.bats
findings:
  critical: 3
  warning: 14
  info: 2
  total: 19
status: issues_found
---

# Phase 5: Code Review Report

**Reviewed:** 2026-09-21T06:03:44Z
**Depth:** standard
**Files Reviewed:** 12
**Status:** issues_found

## Summary

The phase's two shell scripts are well structured for failure: both require an explicit action argument, both use `set -euo pipefail`, and `release-gate.sh` fails closed on every malformed input I could construct (empty `gh` output, missing `.check_runs` key, non-numeric count, `gh` non-zero exit). The gitleaks allowlist scoping is correct as configured, and I confirmed empirically that `condition = "AND"` + `paths` + `regexes` exempts `tests/Fixture.cs` while still reporting the same value in `src/`.

The defects are not in the scripts' own control flow. They are in the wiring around them: the secret scan the phase added does not scan history in either automated gate, the pre-publication audit silently truncates every surface it enumerates, and three of the tests assert outcomes that a broken or absent fixture produces identically.

Every claim below was measured against the pinned tools (gitleaks 8.30.1, zizmor 1.30.1, prek 0.5.3) or the live GitHub API, not inferred. Commands and outputs are quoted so each finding can be reproduced.

## Narrative Findings (AI reviewer)

### Critical Issues

#### CR-01: The CI secret scan reads one commit, not the git history

**File:** `.github/workflows/ci.yml:24-33` (with `.mise.toml:27`)

**Issue:** The lint job checks out with no `fetch-depth`, so `actions/checkout` uses its default of `1` — a shallow clone with a single commit. `mise run lint` then runs `gitleaks git`, which scans only the commits present in the clone. The task description (`.mise.toml:21`), `docs/development.md:7`, and `pre-public-audit.sh:24` all describe this as a history scan. In CI it is not one.

Measured: I cloned this repository, added an AWS-shaped fixture value in one commit and removed it in the next, then scanned both a full clone and a `--depth 1` clone of the same branch with the repository's own `.gitleaks.toml`.

```
=== FULL CLONE (secret present only in history, not in the HEAD tree) ===
INF 220 commits scanned.
WRN leaks found: 1

=== SHALLOW depth=1 (actions/checkout default) ===
INF 1 commits scanned.
INF no leaks found
```

A secret that was committed and later removed — the single case a history scan exists to catch, and the case that matters most immediately before a repository is made public — is missed by the CI gate. Because `ci-success` is what `release-gate.sh` requires, the release gate also inherits this blind spot. It also makes `docs/development.md:17` ("a local run of these three tasks matches CI") false for exactly the check that matters: locally the same scan covers 220 commits.

**Fix:**

```yaml
      - name: Checkout
        uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
        with:
          persist-credentials: false
          fetch-depth: 0 # gitleaks git scans the history; depth 1 would scan one commit
```

Apply to the `lint` job only (it is the only job running `gitleaks`). Then confirm the CI log shows the full commit count, not `1 commits scanned.`

---

#### CR-02: The pre-commit secret scan never runs for the file types secrets arrive in

**File:** `.pre-commit-config.yaml:9,15`

**Issue:** Both hooks are gated by a `files:` regex. `prek` skips a hook when no staged file matches, so a commit that touches no `.cs/.csproj/.slnx/.props/.sh/.bash/.bats/.yml/.yaml` file, and not `.mise.toml` or `.gitleaks.toml`, runs no gitleaks scan at all.

Measured:

```
$ prek run --files docs/development.md LICENSE
mise run lint (dotnet format, shellcheck, shfmt, actionlint, gitleaks, zizmor)...(no files to check)Skipped
mise run test (build with warnings as errors, unit tests, script tests)..........(no files to check)Skipped
```

Unscanned file classes include `.md`, `.txt`, `.json`, `.pem`, `.env`, `.netrc`, `.npmrc`, extensionless files (`Dockerfile`), and the whole `.planning/` tree — which this project commits on nearly every task and which is tracked, so it becomes public with the repository. A `.planning/` commit touches no matching extension, so the local gate is silently off for it.

CI does not backstop this: `ci.yml:5-9` triggers only on `pull_request` and `push` to `main`, so a commit pushed to a `gsd/*` branch is scanned by nothing until a PR exists — and then only at depth 1 (CR-01). Together, CR-01 and CR-02 mean no automated gate in this repository has ever scanned its history for secrets.

**Fix:** Split the history scan out of the file-filtered hook and make it unconditional.

```yaml
    -   id: gitleaks
        name: gitleaks git (scan the whole history for secrets)
        entry: gitleaks git -v --redact
        language: system
        pass_filenames: false
        always_run: true
```

Keep the existing `lint` hook for the file-scoped checks, and drop `gitleaks` from `[tasks.lint]` only if CI gains an equivalent always-on step; otherwise leave `[tasks.lint]` as the single definition and give the hook `always_run: true` instead of the narrower entry above.

---

#### CR-03: The pre-publication audit silently reports at most 30 of each surface

**File:** `scripts/pre-public-audit.sh:41-91`

**Issue:** Every enumeration calls `gh api` without `--paginate`. The GitHub REST default is 30 items per page, and `gh api` does not paginate on its own. Each function then reports `length` of the returned array as if it were the total — while the responses for runs and artifacts carry the true count in a `total_count` field the script ignores.

Measured against the live API:

```
$ gh api "repos/cli/cli/actions/runs" | jq '{total_count, returned:(.workflow_runs|length)}'
{ "total_count": 34570, "returned": 30 }

$ gh api "repos/cli/cli/releases" | jq 'length'
30
```

The script would print `REVIEW workflow-runs: 30 runs` for a repository with 34,570, and list 30 of them. The same truncation applies to `artifacts`, `issues`, `pulls`, and `releases`. This is a one-shot, irreversible decision the report exists to inform, and the report understates the surface without saying so — the operator reads a complete-looking list. This repository currently has 9 runs and 0 artifacts, so the bug is latent today and guaranteed to bite once any list exceeds 30.

The `issues` case compounds it: `issues?state=all` returns pull requests too, so one page of 30 mixed items yields an issue count well under 30 that is still wrong.

**Fix:** Paginate, and prefer the server's own total where one exists.

```bash
workflow_runs() {
	local json count
	json="$(gh api --paginate --slurp "repos/$GH_REPO/actions/runs")"
	count="$(jq '[.[] | .workflow_runs[]] | length' <<<"$json")"
	echo "REVIEW workflow-runs: $count runs"
	jq -r '.[] | .workflow_runs[] | "  id=\(.id) workflow=\(.name) conclusion=\(.conclusion)"' <<<"$json"
}
```

Apply the same `--paginate --slurp` shape to `artifacts`, `issues`, `pull_requests`, and `releases`. Add a test with a fixture of more than one page so the wiring is pinned.

---

### Warnings

#### WR-01: `release-gate.sh` trusts the query filter instead of checking the name it got back

**File:** `scripts/release-gate.sh:26-46`

**Issue:** The script asks for `?check_name=ci-success` and then reads `.check_runs[0]` without ever asserting that run is named `ci-success`. The GitHub API ignores query parameters it does not recognise rather than erroring — measured:

```
$ gh api ".../check-runs?checkname=ci-success" | jq '{total_count, names:[.check_runs[]|.name]}'
{ "total_count": 4, "names": ["ci-success","e2e","test","lint"] }
```

So a typo, a `gh` behaviour change, or an endpoint change that drops the filter yields whatever check runs exist. Today the `count != 1` guard absorbs that (4 runs → refuse), but the guard is the only thing standing between a dropped filter and a gate that approves a release on the strength of an unrelated green check run — the case where exactly one check run exists on the commit. No test can catch a lost filter either: the fake `gh` in `release-gate.bats:31-39` writes argv to a file and returns the fixture regardless of what was asked.

**Fix:** Filter in `jq` as well, so the assertion does not depend on the server honouring the query string.

```bash
	count="$(jq '[.check_runs[] | select(.name == "ci-success")] | length' <<<"$runs_json")"
	...
	status="$(jq -r '[.check_runs[] | select(.name == "ci-success")][0].status' <<<"$runs_json")"
	conclusion="$(jq -r '[.check_runs[] | select(.name == "ci-success")][0].conclusion' <<<"$runs_json")"
```

Add a test whose fixture contains `lint`, `test`, `e2e`, and `ci-success` and asserts the gate reads the `ci-success` entry.

---

#### WR-02: The release gate accepts a commit that was never merged

**File:** `.github/workflows/release.yml:41-64`, `scripts/release-gate.sh:18-46`

**Issue:** The gate requires a passing `ci-success` check run on `github.sha` and nothing else. Because `ci.yml:6` triggers on `pull_request`, the head commit of any open PR on a feature branch also carries a `ci-success` check run. Measured on this repository:

```
$ gh api ".../commits/4d9c4e38.../check-suites" | jq -c '[.check_suites[]|{app:.app.slug,event:.head_branch,conclusion}]'
[{"app":"github-actions","event":"docs/readme-guide","conclusion":"success"}]
```

The suite's `head_branch` is the feature branch, i.e. the check run came from the `pull_request` event on the PR head. So a `v*` tag pushed onto an unmerged feature-branch commit satisfies the gate, and `gh release create --verify-tag` only verifies the tag exists on the remote — not that it is reachable from `main`. A release can therefore be published from code that no review ever merged.

**Fix:** Add an ancestry check to the workflow, between the tag check and the gate.

```yaml
      - name: Require the tagged commit to be on main
        env:
          GH_TOKEN: ${{ github.token }}
          GH_REPO: ${{ github.repository }}
          SHA: ${{ github.sha }}
        run: |
          status="$(gh api "repos/$GH_REPO/compare/main...$SHA" --jq .status)"
          case "$status" in
          identical | behind) ;;
          *)
            echo "The tagged commit is $status relative to main; refusing." >&2
            exit 1
            ;;
          esac
```

---

#### WR-03: The documented recovery from a refused release cannot be performed for some commits

**File:** `docs/development.md:37`, `.github/workflows/ci.yml:5-9`

**Issue:** The docs say: "To fix it, wait for CI on that commit to finish green, then delete and push the tag again." For a commit that has no check suite at all, there is no way to make CI run on it — `ci.yml` triggers only on `pull_request` and `push` to `main`, and it has no `workflow_dispatch`. Re-pushing a tag triggers `release.yml`, never `ci.yml`. Such commits exist on `main` today: GitHub only schedules push workflows for the head of a push, so intermediate commits get nothing. Measured over the last eight `origin/main` commits:

```
ecee1ed... suites=1 ci_success_runs=1
4d9c4e3... suites=1 ci_success_runs=1
23c9b08... suites=0 ci_success_runs=0
1557678... suites=0 ci_success_runs=0
6ffdf74... suites=1 ci_success_runs=1
```

Tag `23c9b08` and the release is permanently ungateable, with the documented remedy unavailable.

**Fix:** Add a manual trigger to `ci.yml` so an operator can produce a `ci-success` run on an arbitrary commit, and say so in the docs.

```yaml
on:
  pull_request:
  push:
    branches:
      - main
  workflow_dispatch:
```

Then correct `docs/development.md:37` to name the real recovery: re-run the cancelled workflow from the Actions UI, or dispatch CI on the tagged ref.

---

#### WR-04: The docs omit a refusal state the gate implements

**File:** `docs/development.md:37` (behaviour at `scripts/release-gate.sh:36-39`)

**Issue:** The docs enumerate the refusal states as: no `ci-success` check run, a run still in progress, or a conclusion other than `success`. The script also refuses when it finds more than one `ci-success` check run ("Found $count ci-success check runs on $sha; expected exactly one."). An operator meeting that message finds no explanation in the docs, and the documented remedy — delete and re-push the tag — does not change how many check runs a commit carries.

**Fix:** Add the fourth state to the sentence at `docs/development.md:37`, and say what to do about it (re-run the workflow so the newest run is the one the API returns, or move the release to a fresh commit).

---

#### WR-05: The audit's `secret-scan` verdict is really a whole-lint verdict, and it throws the evidence away

**File:** `scripts/pre-public-audit.sh:22-32`

**Issue:** `mise run lint` runs six checks (`.mise.toml:22-29`): `dotnet format`, `shellcheck`, `shfmt`, `actionlint`, `gitleaks`, `zizmor`. Any one of them failing prints `FAIL secret-scan: mise run lint reported a finding`, sending the operator to hunt for a leak that may be a two-space indent. The header comment justifies reusing the task, and that reuse is sound; the label is not.

It is worse than mislabelled, because the output is discarded (`>/dev/null 2>&1`) and the remedy offered is "run 'mise run lint' yourself to see it" — which shows nothing useful either. Measured: `gitleaks git` without `-v` prints only a count, with no file, commit, or rule:

```
INF 220 commits scanned.
WRN leaks found: 1
```

**Fix:** Rename the item and keep the output. Also add `-v --redact` to the gitleaks line in `[tasks.lint]` so a finding names its file, line, commit, and rule while masking the value (verified: `-v --redact` prints `Secret: REDACTED` alongside `RuleID`, `File`, `Line`, `Commit`).

```bash
lint_gate() {
	local out
	if out="$(mise run lint 2>&1)"; then
		echo "PASS lint: no findings (includes the gitleaks history scan)"
		return 0
	fi
	echo "FAIL lint: a check reported a finding (includes the gitleaks history scan)" >&2
	printf '%s\n' "$out" >&2
	return 1
}
```

Update `pre-public-audit.bats:84,91` and the label-order fixture at `:112-119` to match.

---

#### WR-06: The allowlist-scope test passes when nothing was scanned

**File:** `tests/scripts/gitleaks.bats:133-138`

**Issue:** "a fixture value inside the fixture trees is ignored" asserts `status -eq 0` and `output == *"no leaks found"*`. Those are exactly the outputs `gitleaks git` produces when it sees nothing at all. Measured:

```
=== gitleaks git on a repo with NO commits (a failed fixture commit) ===
INF 0 commits scanned.
INF no leaks found      exit=0

=== file present but UNCOMMITTED ===
INF no leaks found      exit=0
```

`scan_git_repo` (`:88-106`) never checks that `git init`, `git add`, or `git commit` succeeded, and because it runs under bats `run`, errexit is off inside it. So any setup failure — a global `commit.gpgsign` (see WR-07), a `git` that cannot write the tmpdir, a typo in the `path=content` split at `:96-97` — turns the repository's one proof that the allowlist is scoped by value into a test that cannot fail. The file's own header identifies this failure mode for the detection tests ("a scanner that reported nothing at all would pass that proof identically") and then reintroduces it here.

**Fix:** Assert the fixture was actually scanned, and prove the exemption is the allowlist's doing rather than an empty scan.

```bash
@test "a fixture value inside the fixture trees is ignored" {
	run scan_git_repo "$BATS_TEST_TMPDIR/inside-fixture" \
		"tests/Fixture.cs=var apiKey = \"$(fixture_shaped_value)\";"
	[ "$status" -eq 0 ]
	[[ "$output" == *"no leaks found"* ]]
	[[ "$output" =~ 1\ commits\ scanned ]]

	# Without this repository's config the same commit is a finding, so the
	# clean result above is the allowlist, not an empty scan.
	run gitleaks git --no-banner --no-color --redact "$BATS_TEST_TMPDIR/inside-fixture"
	[ "$status" -eq 1 ]
}
```

---

#### WR-07: Fixture commits inherit the developer's global git config

**File:** `tests/scripts/gitleaks.bats:92-103`

**Issue:** `scan_git_repo` overrides `user.email` and `user.name` per command but nothing else. A developer with `commit.gpgsign = true`, a signing key that needs a passphrase, a global `core.hooksPath`, or a `commit.template`/`gpg.format` set will have the fixture commit fail or block. Combined with WR-06 the failure is silent for one test and confusing for the other two.

**Fix:** Neutralise the environment for every fixture git invocation.

```bash
	git -C "$repo" \
		-c user.email=test@example.invalid -c user.name=test \
		-c commit.gpgsign=false -c core.hooksPath=/dev/null \
		commit -qm fixture
```

Also drop the `-q` on the failure path or add `|| return 1` after `git add`/`git commit` so a setup failure is loud.

---

#### WR-08: The audit's list rendering and its issue/PR filter are untested

**File:** `tests/scripts/pre-public-audit.bats:31-34,94-101`

**Issue:** Every fixture except `RUNS_FILE` is empty (`[]` or `{"artifacts":[]}`). So:

- The `if [[ "$count" -gt 0 ]]` listing branches in `artifacts`, `issues`, `pull_requests`, and `releases` (`pre-public-audit.sh:56-58,68-70,78-80,88-90`) are never executed. A malformed `jq` expression there would ship green.
- The `select(has("pull_request") | not)` filter at `:66,69` — the one non-trivial piece of logic in the script, and the one the source comments call out as necessary — is never exercised with a pull request in the fixture. The assertion `[[ "$output" == *"REVIEW issues: 0"* ]]` at `:98` holds whether the filter works, is inverted, or is deleted.
- The `GH_REPO` unset branch (`pre-public-audit.sh:96-99`) has no test.

**Fix:** Add a fixture with mixed content and assert the discrimination.

```bash
@test "the issues item counts issues and excludes pull requests" {
	printf '[{"number":1,"title":"a real issue"},{"number":2,"title":"a pr","pull_request":{}}]' >"$ISSUES_FILE"
	run "$REPO_ROOT/scripts/pre-public-audit.sh" run
	[ "$status" -eq 0 ]
	[[ "$output" == *"REVIEW issues: 1 issues"* ]]
	[[ "$output" == *"#1 a real issue"* ]]
	[[ "$output" != *"#2 a pr"* ]]
}

@test "an unset GH_REPO refuses before any gh call" {
	unset GH_REPO
	run "$REPO_ROOT/scripts/pre-public-audit.sh" run
	[ "$status" -eq 1 ]
	[[ "$output" == *"GH_REPO is not set"* ]]
	[ ! -s "$GH_ARGV_FILE" ]
}
```

Add equivalents with non-empty `artifacts`, `pulls`, and `releases` fixtures.

---

#### WR-09: The README Status line still states the claim the Compatibility section was added to correct

**File:** `README.md:10`

**Issue:** Line 10 reads "tested only in local containers, with Jellyfin 12.1.0 and Emby 4.10.0.40". Line 20-21, added in this phase, explicitly refutes that: the tested image is `12.1.20260915-010956`, and "`12.1.0` is the version of the Jellyfin packages the plugin compiles against, which is a separate thing from the server it was run against". Verified against `e2e/compose.yaml:18` (`jellyfin/jellyfin:12.1.20260915-010956`) and `:5` (`emby/embyserver:4.10.0.40`). A reader who stops at the Status line takes away the exact wrong fact the phase set out to fix.

**Fix:**

```markdown
**Status:** tested only in local containers, against the Jellyfin `12.1.20260915-010956` and Emby `4.10.0.40` images (see [Compatibility](#compatibility)). Not tested on a production server. Not published to a plugin repository.
```

---

#### WR-10: `scripts/pre-public-audit.sh` is documented nowhere

**File:** `docs/development.md:5-15`, `README.md`

**Issue:** `rg -n "pre-public-audit" --glob '!.planning/**'` matches only the script and its own test file. The script is operator-facing, needs `GH_REPO` and `GH_TOKEN` set, and produces a report a human must read line by line — none of which is discoverable. The project rule is to keep `README.md` and `docs/` accurate when behaviour changes, and `docs/development.md` is the one table of commands a contributor reads. An undocumented gate is a gate nobody runs.

**Fix:** Add a row to the table at `docs/development.md:5-15` and a short subsection explaining the required environment, that only the secret-scan item is machine-checked, and that the rest is for human review.

```markdown
| Audit the repository before making it public | `GH_REPO=owner/repo scripts/pre-public-audit.sh run` |
```

---

#### WR-11: `.gitleaks.toml` claims test coverage that does not exist

**File:** `.gitleaks.toml:12`

**Issue:** The comment says "tests/scripts/gitleaks.bats pins all three directions" — `paths`, `regexes`, and `targetRules`. The test file's own header (`gitleaks.bats:69-76`) says two directions, and only two exist: by path (`:117`) and by value (`:124`). Nothing tests that `targetRules = ["generic-api-key"]` keeps the exemption off other rules, i.e. that a fixture value is still reported when it trips a more specific rule. A comment that overstates coverage is worse than no comment, because the next reader trusts it and skips the check.

**Fix:** Either correct the comment to name two directions, or add the third test — put an `AKIA`-shaped value (which trips `aws-access-token`, not `generic-api-key`) inside `tests/` and assert it is still reported.

---

#### WR-12: `ci-success` requires only the jobs someone remembered to list

**File:** `.github/workflows/ci.yml:68-88`

**Issue:** The gate iterates `join(needs.*.result, ' ')` over a hardcoded `needs: [lint, test, e2e]`. A job added to `ci.yml` and not added to `needs` is not required by `ci-success`, and `release-gate.sh` treats `ci-success` as the whole of CI. Nothing in the repository detects the omission. Separately, the loop passes when `RESULTS` is empty: `for result in ""` iterates zero times and the step exits 0. I could not construct a reachable empty-`RESULTS` case with the current `needs`, but the guard costs one line and the failure mode is a silent pass on the repository's only required check.

**Fix:**

```yaml
        run: |
          echo "Job results: $RESULTS"
          count=0
          for result in $RESULTS; do
            count=$((count + 1))
            if [ "$result" != "success" ]; then
              echo "A required job did not succeed."
              exit 1
            fi
          done
          if [ "$count" -ne 3 ]; then
            echo "Expected 3 required job results, got $count; update needs and this count together."
            exit 1
          fi
```

---

#### WR-13: The standing zizmor gate runs none of the online audits

**File:** `.mise.toml:28`

**Issue:** The lint task runs `zizmor --offline --persona=pedantic`. From zizmor 1.30.1's own `--help`: `--offline` "disables all online audit rules, and prevents zizmor from auditing remote repositories". So the audits that need a GitHub token never run — in CI, which is the one place a token is available. `--offline` is the right default for a laptop with no credentials; making it the only mode means the gate is permanently weaker than the pinned tool can be.

**Fix:** Keep `--offline` as the local default and run the full set in CI, e.g. a separate task the lint job also calls:

```toml
[tasks.lint-workflows-online]
description = "Run zizmor including the audits that need the GitHub API"
run = "zizmor --persona=pedantic .github/workflows"
```

```yaml
      - name: Audit the workflows online
        env:
          GH_TOKEN: ${{ github.token }}
        run: mise run lint-workflows-online
```

That needs `actions: read` on the lint job. If a second task is unwanted, at minimum record in `docs/development.md` which audits the gate does not run, so nobody reads a green zizmor as a full clearance.

---

#### WR-14: The audit mixes a local scan with a remote enumeration and never checks they are the same repository

**File:** `scripts/pre-public-audit.sh:22-39,96-99`

**Issue:** `secret_scan` scans whatever git repository the current directory belongs to (via `mise run lint` → `gitleaks git`), while every other item reports on whatever `$GH_REPO` names, and `gh repo view` also follows `$GH_REPO`. Run it with `GH_REPO` pointing at a different repository, or from a different checkout, and the report is internally coherent and describes two different things — under a "PASS overall" summary. For a one-shot, irreversible decision, the audit should refuse rather than compose.

**Fix:** Verify the local checkout is the repository being audited before reporting.

```bash
	local origin
	origin="$(git remote get-url origin 2>/dev/null || true)"
	case "$origin" in
	*"$GH_REPO"*) ;;
	*)
		echo "GH_REPO is $GH_REPO but origin is '$origin'; the secret scan and the API items would describe different repositories." >&2
		return 1
		;;
	esac
```

Also `cd` to the repository root at the top of `run`, so the scan does not depend on the invoking directory.

---

### Info

#### IN-01: `gitleaks.bats` declares no minimum bats version

**File:** `tests/scripts/gitleaks.bats:31`

**Issue:** `release-gate.bats:5` and `pre-public-audit.bats:6` both declare `bats_require_minimum_version 1.5.0`; `gitleaks.bats` declares nothing while relying on `BATS_TEST_TMPDIR` and `setup_file`. An older bats would fail obscurely instead of stating the requirement.

**Fix:** Add `bats_require_minimum_version 1.5.0` after the header comment.

---

#### IN-02: The only test of the real API request shape never runs in CI

**File:** `tests/scripts/release-gate.bats:152-169`

**Issue:** The test unsets `GH_TOKEN` and `GH_REPO`, then skips unless `gh auth status` succeeds. In CI neither is set for the `test` job, so it always skips; locally it skips whenever `gh` is absent or unauthenticated. The suite's one guard against the GET/POST request-shape regression it was written for is therefore conditional everywhere. The assertion is also satisfiable for the wrong reason — a `401` also contains no `404`.

**Fix:** Leave the skip (a networked unit test should not be mandatory) but assert the specific expected status, e.g. that the output names `422` for a well-formed unknown SHA, so a `401` does not read as a pass. Consider moving it to `e2e/` where network access is expected.

---

_Reviewed: 2026-09-21T06:03:44Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
