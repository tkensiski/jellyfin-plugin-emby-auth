# Phase 5: Public Repository - Pattern Map

**Mapped:** 2026-09-20
**Files analyzed:** 10
**Analogs found:** 9 / 10

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---|---|---|---|---|
| `scripts/release-gate.sh` | utility (CLI script) | request-response (calls `gh api`, judges pass/fail) | `scripts/package.sh` | exact (same repo-root/usage/case-dispatch shape) |
| `scripts/pre-public-audit.sh` | utility (CLI script) | batch (enumerate + report) | `scripts/package.sh` | role-match (same script shape; different data flow — enumeration vs. build) |
| `tests/scripts/release-gate.bats` | test | request-response (fake `gh` on `PATH`) | `tests/scripts/package.bats` | exact |
| `tests/scripts/pre-public-audit.bats` | test | batch | `tests/scripts/package.bats` | role-match |
| `.gitleaks.toml` | config | transform (allowlist filter) | none in repo (new tool) | no analog — use RESEARCH.md Pattern 2 |
| `.mise.toml` (`[tasks.lint]`, `[tools]`) | config | batch (chained lint commands) | itself, existing `[tasks.lint]` | exact (edit in place) |
| `.github/workflows/release.yml` | config/CI | request-response (gate step + permissions) | itself, existing `check-tag` step | exact (edit in place) |
| `.github/workflows/ci.yml` | config/CI | event-driven (job names only) | itself | exact (edit in place) |
| `README.md` (Compatibility section, D-19 fixes) | config/docs | transform (prose) | itself, Status line at `:10` and Requirements at `:12-16` | exact (edit in place) |
| `.pre-commit-config.yaml` | config | event-driven (file-pattern gating) | itself, `lint` hook `files` regex | exact (edit in place) |

## Pattern Assignments

### `scripts/release-gate.sh` (utility, request-response)

**Analog:** `scripts/package.sh` (all 127 lines read)

**Header/usage pattern** (lines 1-27):
```bash
#!/usr/bin/env bash
set -euo pipefail

# <one-line purpose>
#
# Usage:
#   scripts/package.sh build           Build artifacts/release/....
#   scripts/package.sh check-tag TAG   Fail unless TAG is v<version>.
#
# Environment:
#   PACKAGE_OUTPUT_DIR  Output folder. Default: artifacts/release.
#   ...

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly REPO_ROOT

usage() {
	echo "Usage: $0 build | check-tag TAG" >&2
}
```
Copy this shape exactly for `release-gate.sh`: a usage comment block naming every action, a `REPO_ROOT` resolved from `BASH_SOURCE`, and a `usage()` function that prints to stderr. Per D-04, no env var exists solely for test seams — the fake is a `gh` binary on `PATH`, not an env-var-selected fixture path, so do **not** copy `package.sh`'s `PACKAGE_OUTPUT_DIR`-style test-seam env vars here; the only env var this script needs is whatever `gh` itself reads (`GH_REPO`, `GH_TOKEN`), already supplied by the workflow step.

**Action-dispatch / `main()` pattern** (lines 107-127):
```bash
main() {
	case "${1:-}" in
	build)
		build
		;;
	check-tag)
		if [[ $# -ne 2 ]]; then
			usage
			exit 2
		fi
		check_tag "$2"
		;;
	*)
		usage
		exit 2
		;;
	esac
}

main "$@"
```
`release-gate.sh` copies this `case` dispatch verbatim in shape: an explicit action (e.g. `check`), argument-count validation before calling the worker function, `usage; exit 2` for both a missing and an unknown action.

**Fail-closed worker function pattern** (lines 40-47, `check_tag`):
```bash
check_tag() {
	local tag="$1" version
	version="$(plugin_version)"
	if [[ "$tag" != "v$version" ]]; then
		echo "The tag $tag does not match the plugin version $version in Directory.Build.props. Tag the release v$version." >&2
		return 1
	fi
}
```
Same shape for the gate's `check()` function: compute/query, compare, `echo ... >&2; return 1` on failure, implicit success (falls through) otherwise. Combine with RESEARCH.md's Pattern 1 code (the `gh api ... check-runs` call and status/conclusion comparison) as the body.

**Error-handling convention:** `set -euo pipefail` at top (from `shell.md` rule and `package.sh:2`); every user-facing failure message goes to stderr via `>&2` and returns non-zero — `package.sh` never uses `exit` inside a worker function, only `return`, leaving `main`'s dispatcher as the single `exit` point outside the `usage` cases.

---

### `scripts/pre-public-audit.sh` (utility, batch)

**Analog:** `scripts/package.sh` (same read as above — header, usage, `main()` dispatch reused verbatim in shape)

Differs from `release-gate.sh` in body: instead of one pass/fail judgment, this script prints a pass/fail line per audit item (history scan reused per D-14, workflow runs/artifacts/issues/PRs/releases enumerated via `gh api` for human review). Follow `package.sh`'s `build()` function as the shape for a multi-step worker that echoes progress to stderr and a final summary to stdout — but unlike `build()`, most items here don't have a single return value; each item prints its own `PASS`/`REVIEW` line, and the script's exit code reflects only the automatable items (the reused gitleaks scan), matching RESEARCH.md's Open Question 1 recommendation.

**Action name:** discretion item — RESEARCH.md/architecture diagram uses `scripts/pre-public-audit.sh run`, following `package.sh build`'s no-argument-object convention (a single verb, no flags).

---

### `tests/scripts/release-gate.bats` (test, request-response)

**Analog:** `tests/scripts/package.bats` (all 77 lines read)

**setup_file / setup pattern** (lines 4-18):
```bash
setup_file() {
	REPO_ROOT="$(cd "$BATS_TEST_DIRNAME/../.." && pwd)"
	PACKAGE_OUTPUT_DIR="$BATS_FILE_TMPDIR/release"
	export REPO_ROOT PACKAGE_OUTPUT_DIR
	...
	"$REPO_ROOT/scripts/package.sh" build >"$BATS_FILE_TMPDIR/build.out"
}

setup() {
	VERSION="$(sed -n '...' "$REPO_ROOT/Directory.Build.props" | head -1)"
	...
}
```
`release-gate.bats` copies the `REPO_ROOT` resolution line verbatim. Per D-04, `setup_file`/`setup` instead build a temp `PATH` directory containing a fake `gh` script that echoes fixture JSON keyed by test case, and `export PATH="$fake_gh_dir:$PATH"` — the fake-binary-on-PATH seam, not `package.bats`'s output-directory seam.

**Usage/unknown-action test pair** (lines 20-30) — copy verbatim in shape, substituting the script name:
```bash
@test "release-gate.sh without an action prints usage and fails" {
	run "$REPO_ROOT/scripts/release-gate.sh"
	[ "$status" -eq 2 ]
	[[ "$output" == *"Usage:"* ]]
}

@test "release-gate.sh with an unknown action prints usage and fails" {
	run "$REPO_ROOT/scripts/release-gate.sh" publish
	[ "$status" -eq 2 ]
	[[ "$output" == *"Usage:"* ]]
}
```

**Pass/fail assertion pattern** (lines 67-76, `check-tag` tests):
```bash
@test "check-tag accepts the tag of the plugin version" {
	run "$REPO_ROOT/scripts/package.sh" check-tag "v$VERSION"
	[ "$status" -eq 0 ]
}

@test "check-tag refuses a tag for another version" {
	run "$REPO_ROOT/scripts/package.sh" check-tag "v0.0.1"
	[ "$status" -eq 1 ]
	[[ "$output" == *"does not match"* ]]
}
```
`release-gate.bats` needs one such pair per fixture state named in RESEARCH.md's Wave 0 Gaps: no matching check run, in-progress, `cancelled`, `failure`, `skipped`, `timed_out`, `success` — seven `run`/`status`/`output` triples in this shape, each pointed at a differently-configured fake `gh`.

---

### `tests/scripts/pre-public-audit.bats` (test, batch)

**Analog:** `tests/scripts/package.bats`, same usage/unknown-action pair as above. Per RESEARCH.md Open Question 1, add one further test asserting the reused-gitleaks-scan item's pass/fail (mirroring the `check-tag` accept/refuse pair) — the enumerate-for-human-review items get no assertion beyond "the command exits 0 and prints something," since their correctness is what a human reads, not a comparable value.

---

### `.mise.toml` `[tasks.lint]` and `[tools]` (config, batch)

**Analog:** itself, current state (lines 1-27)

**Current `[tools]` block** (lines 1-11) — add `gitleaks = "8.30.1"` as a new line in this existing table, alphabetical-ish placement not enforced (current order is functional grouping, not alphabetical — `dotnet, bats, jq, shellcheck, shfmt, actionlint, zizmor, prek, act, node`).

**Current `[tasks.lint]`** (lines 19-27):
```toml
[tasks.lint]
description = "Check C# formatting, and lint the shell scripts and GitHub workflows"
run = [
  "dotnet format Jellyfin.Plugin.EmbyAuth.slnx --verify-no-changes",
  "shellcheck -x e2e/*.bash e2e/*.bats scripts/*.sh tests/scripts/*.bats",
  "shfmt -d e2e scripts tests/scripts",
  "actionlint",
  "zizmor --offline .github/workflows",
]
```
Per D-08, the last line becomes `"zizmor --offline --persona=pedantic .github/workflows"` — same list position, both flags kept (Pitfall 4). Per D-05, add a new line `"gitleaks git"` to the `run` array — this is an edit to an existing list, not a new task, keeping the `CLAUDE.md` one-mise-task-per-CI-job rule intact (§Reusable Assets already notes this).

**Shell-array style convention:** every existing `run` entry is a single-line double-quoted string; the new `gitleaks git` entry matches — no multi-line heredoc, no `\` continuation, consistent with the five existing lines.

---

### `.github/workflows/release.yml` (config/CI, request-response)

**Analog:** itself, current state (all 48 lines read)

**Job permissions pattern** (lines 18-19, needs `checks: read` added per Pitfall 1):
```yaml
    permissions:
      contents: write
```
becomes (per RESEARCH.md Code Example "Adding the `checks: read` permission", and REL-02's `undocumented-permissions` requirement):
```yaml
    permissions:
      contents: write   # gh release create
      checks: read       # release-gate.sh: read the ci-success check run
```

**Existing step-and-gh-api-call pattern to copy** (lines 32-35 for step shape, lines 43-48 for `gh` invocation shape):
```yaml
      - name: Check that the tag matches the plugin version
        env:
          TAG: ${{ github.ref_name }}
        run: scripts/package.sh check-tag "$TAG"
```
```yaml
      - name: Create the GitHub release
        env:
          GH_TOKEN: ${{ github.token }}
          GH_REPO: ${{ github.repository }}
          TAG: ${{ github.ref_name }}
        run: gh release create "$TAG" ...
```
The new gate step copies both shapes: a `name:` step with an `env:` block passing exactly the variables the script needs (`GH_TOKEN`, `GH_REPO`, and the commit SHA — `${{ github.sha }}`), and a one-line `run:` invoking the script. Per D-16/discretion, this step sits after `check-tag` and before `mise run test`, matching the architecture diagram's step order [3] in RESEARCH.md.

**Concurrency block (D-10, new — no existing analog in this file):** copy `ci.yml`'s shape (see below) but with `cancel-in-progress: false`.

**Action-pinning convention** (lines 21-24, 26-30) — every `uses:` is pinned to a full SHA with a trailing `# vX.Y.Z` comment and `persist-credentials: false` on checkout; no new `uses:` steps are added by this phase, so no new pins are needed, but any future step must match this shape per `~/.claude/rules/github-actions.md`.

---

### `.github/workflows/ci.yml` (config/CI, event-driven)

**Analog:** itself, current state

**Existing concurrency block to copy into `release.yml`** (lines 14-16):
```yaml
concurrency:
  group: ${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true
```
`release.yml` copies this shape with `cancel-in-progress: false` per D-10.

**Job name addition (D-09):** each of `lint:`, `test:`, `e2e:` (lines 19, 34, 49) gains a `name:` field directly under the job key, e.g.:
```yaml
  lint:
    name: Lint
    runs-on: ubuntu-latest
```
and `ci-success:` (line 65) gains `name: ci-success` — identical to its job id, per D-09, so the check-run name `release-gate.sh` matches on (`ci-success`) does not change. `release.yml`'s `release:` job may take a free descriptive name the same way.

---

### `README.md` (docs, transform)

**Analog:** itself, current state (all 67 lines read)

**Sentences D-19 corrects:**
- Line 21: `From a GitHub release of this repository. The repository is private, so you need access to it.` — becomes a sentence that no longer claims privacy (repository is public after PUB-03).
- Line 33: `Jellyfin downloads a repository manifest and the zip without GitHub credentials, so the manifest works as a repository URL only when the release files are public.` — the conditional clause changes meaning once files are in fact public; D-19 says correct it now, leave Phase 6's DOCS-02 to rewrite the Install section fully around the manifest URL later.
- Line 10 Status line (`Not published to a plugin repository`) — leave unchanged per D-19 (stays true until Phase 6).

**New Compatibility section (D-17):** insert as its own `## Compatibility` heading, sibling to `## Requirements` (lines 12-16) and `## Install` (lines 18-33) — copy the existing section-heading/prose style (short paragraph, no nested subheadings, plain sentences, consistent with `## Configure` and `## Migrate users`). Content per D-18: state tested versions (Jellyfin 12.1.0, Emby 4.10.0.40, local containers) and that `targetAbi` is a floor, not a ceiling — Jellyfin will install this plugin on a newer, untested server. Apply Phase 3's D-18 rule (never claim untested behavior works) — do not say a newer Jellyfin is supported.

**Existing Requirements bullet style** (lines 12-16) to match tone/format:
```markdown
## Requirements

- Jellyfin 12.1. The plugin is built against the Jellyfin 12.1.0 packages.
- An Emby server that Jellyfin can reach over HTTP or HTTPS.
- An API key for the plugin on that Emby server.
```

---

### `.pre-commit-config.yaml` (config, event-driven)

**Analog:** itself, current state (all 15 lines read)

**`files` regex pattern to check/extend** (line 9):
```yaml
files: \.(cs|csproj|slnx|props|sh|bash|bats|ya?ml)$|^\.mise\.toml$
```
This already matches `.sh`, `.bats`, and `.yml`/`.yaml` extensions, so new `scripts/release-gate.sh`, `scripts/pre-public-audit.sh`, `tests/scripts/*.bats`, and the workflow edits are already covered. `.gitleaks.toml` is **not** matched by this regex (no `.toml` extension pattern) — per Integration Points in CONTEXT.md, the planner must add `.gitleaks.toml` to this pattern (e.g. `|^\.gitleaks\.toml$`) or the lint hook will not re-run when only that file changes.

---

## Shared Patterns

### Script header/usage/dispatch shape
**Source:** `scripts/package.sh:1-27, 107-127`
**Apply to:** `scripts/release-gate.sh`, `scripts/pre-public-audit.sh`
Every new script in `scripts/` copies: `#!/usr/bin/env bash` + `set -euo pipefail`; a top-of-file comment block listing `Usage:` and `Environment:`; `REPO_ROOT` resolved via `$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)`; a `usage()` function printing to stderr; a `main()` with a `case` dispatch on `${1:-}` requiring an explicit action, `usage; exit 2` for missing/unknown action (workspace rule: never default to a mutating operation).

### bats test file shape
**Source:** `tests/scripts/package.bats:1-30`
**Apply to:** `tests/scripts/release-gate.bats`, `tests/scripts/pre-public-audit.bats`
`REPO_ROOT="$(cd "$BATS_TEST_DIRNAME/../.." && pwd)"` in `setup_file`; the usage/unknown-action test pair verbatim in shape; `run "$REPO_ROOT/scripts/<name>.sh" <action>` then `[ "$status" -eq N ]` and `[[ "$output" == *"..."* ]]` assertions.

### Action pinning and checkout hardening
**Source:** `.github/workflows/release.yml:21-24`, `.github/workflows/ci.yml:24-26` (and every other `uses:` step)
**Apply to:** any new step added to either workflow (none of this phase's new steps use a marketplace action, but if one is ever added it must match this shape)
```yaml
      - name: Checkout
        uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
        with:
          persist-credentials: false
```

### One mise task per CI job
**Source:** `.mise.toml` `[tasks.lint]`, `.github/workflows/ci.yml:31-32` (`run: mise run lint`)
**Apply to:** the gitleaks and zizmor-pedantic additions — both land inside `[tasks.lint]`'s existing `run` array, not as new CI steps or a new mise task, per `CLAUDE.md`'s "one mise task per CI job" rule.

## No Analog Found

| File | Role | Data Flow | Reason |
|------|------|-----------|--------|
| `.gitleaks.toml` | config | transform | No existing gitleaks config in the repo — this is the first use of the tool. Use RESEARCH.md's Pattern 2 (`[[allowlists]]` table, default `regexTarget`) and Code Example ("A minimal `.gitleaks.toml`") verbatim as the starting shape; extend with the two value regexes from D-07 (`0123456789abcdef`, `sentinel-api-key-`). |

## Metadata

**Analog search scope:** `scripts/`, `tests/scripts/`, `.github/workflows/`, `.mise.toml`, `README.md`, `.pre-commit-config.yaml` — the full set of files CONTEXT.md and RESEARCH.md name as in-scope for this phase.
**Files scanned:** `scripts/package.sh`, `tests/scripts/package.bats`, `.mise.toml`, `.github/workflows/release.yml`, `.github/workflows/ci.yml`, `README.md`, `.pre-commit-config.yaml` (7 files, each read in full — all ≤ 127 lines, single-pass reads, no re-reads).
**Pattern extraction date:** 2026-09-20
