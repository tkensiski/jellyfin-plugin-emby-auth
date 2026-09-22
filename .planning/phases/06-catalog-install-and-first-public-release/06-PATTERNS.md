# Phase 6: Catalog Install and First Public Release - Pattern Map

**Mapped:** 2026-09-21
**Files analyzed:** 12 (new and modified)
**Analogs found:** 12 / 12

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|--------------------|------|-----------|-----------------|----------------|
| `scripts/manifest.sh` (new) | utility/CLI script | batch (fetch every release, merge, verify) | `scripts/release-gate.sh` | exact (gh-api-driven script with required action argument) |
| `tests/scripts/manifest.bats` (new) | test | request-response (fake `gh` on PATH) | `tests/scripts/release-gate.bats` | exact |
| `.github/workflows/pages.yml` (new) | CI/CD workflow | event-driven (`workflow_run` + `workflow_dispatch`) | `.github/workflows/release.yml` | role-match (release.yml is tag-triggered, single-job; pages.yml needs two jobs + Pages permissions) |
| `e2e/NN-catalog-install.bats` (new) | test (e2e) | request-response / CRUD (install, then update) | `e2e/40-emby-outage.bats` (topic file with `teardown_file` restoring a stopped service) and `e2e/helpers.bash` | role-match |
| `e2e/compose.yaml` (modified — new services) | config | — | itself (existing `emby-proxy` nginx service) | exact (self-analog, add a service in the same shape) |
| `scripts/package.sh` (modified) | utility/CLI script | transform (read CHANGELOG section, add refusal, add version override) | itself (existing `plugin_version()`/`target_abi()`/`build()`) | exact (self-analog) |
| `tests/scripts/package.bats` (modified) | test | request-response | itself (existing test file) | exact (self-analog) |
| `CHANGELOG.md` (modified — restructure) | docs/config | — | itself | exact (self-analog; existing `## Unreleased` + `### Added`/`### Removed`/`### Changed` subsections) |
| `README.md` (modified) | docs | — | itself (`## Install`, `## Compatibility` sections) | exact (self-analog) |
| `CLAUDE.md` (modified — pin list) | docs | — | itself (`## Rules`, the pin-bump bullet at line ~50) | exact (self-analog) |
| `docs/development.md` (modified) | docs | — | itself (`## Releases` procedure, task table) | exact (self-analog) |
| `.mise.toml` (modified — new task) | config | — | itself (`[tasks.package]`, `[tasks.e2e]`) | exact (self-analog) |

## Pattern Assignments

### `scripts/manifest.sh` (utility, batch/CRUD-over-API)

**Analog:** `scripts/release-gate.sh` (whole-script shape) plus `scripts/package.sh` (jq/checksum conventions)

**Header/usage/action-dispatch pattern** (`scripts/release-gate.sh:1-17`, `:86-103`):
```bash
#!/usr/bin/env bash
set -euo pipefail

# <one-paragraph description of what the script refuses/builds>
#
# Usage:
#   scripts/release-gate.sh check SHA   Fail unless SHA has one completed/success ci-success check run.
#
# Environment:
#   GH_REPO    owner/repo, read by gh itself.
#   GH_TOKEN   GitHub token, read by gh itself.

usage() {
	echo "Usage: $0 check SHA" >&2
}

main() {
	if [[ $# -ne 2 ]]; then
		usage
		exit 2
	fi

	case "$1" in
	check)
		check "$2"
		;;
	*)
		usage
		exit 2
		;;
	esac
}

main "$@"
```
`scripts/manifest.sh` should copy this exact shape: a header comment naming every environment variable, `usage()` exits 2, `main()` dispatches on `$1` with a required action argument (e.g. `rebuild`, and optionally `verify` per Claude's Discretion), and an unknown/missing action falls through to `usage; exit 2`. `scripts/package.sh:107-124` is the second working example of the same `main()` shape (two actions: `build`, `check-tag`) — use whichever reads more naturally with two-plus actions.

**gh-api-driven fetch pattern, with explicit failure messages** (`scripts/release-gate.sh:18-30`, `:64-76`):
```bash
if [[ -z "${GH_REPO:-}" ]]; then
	echo "GH_REPO is not set; cannot query check runs for $sha." >&2
	return 1
fi

if ! runs_json="$(gh api "repos/$GH_REPO/commits/$sha/check-runs?check_name=ci-success")"; then
	echo "The GitHub API call to list check runs on $sha failed." >&2
	return 1
fi
```
Copy this `if ! var="$(gh api ...)"; then echo "... failed." >&2; return 1; fi` idiom for every `gh api`/`gh release download` call in `manifest.sh` (the enumeration call, each per-tag `gh release download`). Never let a failed `gh` call fall through silently — `set -euo pipefail` would abort the whole script with no context otherwise.

**Enumeration without the 30-item cap** (RESEARCH.md Pattern 1, verified this session via `gh --help`):
```bash
tags="$(gh api "repos/$GH_REPO/releases" --paginate --jq \
  '[.[] | select(.draft == false)] | sort_by(.tag_name) | .[].tag_name')"
```
Never use `gh release list` (defaults to `-L 30`). This mirrors `release-gate.sh`'s own preference for `gh api` over higher-level `gh` subcommands.

**Per-release scratch directories** (RESEARCH.md Pattern 2 — required because every release's asset is literally named `manifest.json`, from `.github/workflows/release.yml:64`):
```bash
for tag in $tags; do
	dir="$scratch/$tag"
	mkdir -p "$dir"
	gh release download "$tag" --dir "$dir" --pattern 'manifest.json' --clobber
	gh release download "$tag" --dir "$dir" --pattern '*.zip' --clobber
done
```

**Checksum re-verification, reusing `scripts/package.sh`'s exact invocation** (`scripts/package.sh:83`):
```bash
checksum="$(openssl dgst -md5 -r "$OUTPUT_DIR/$zip_name" | cut -d' ' -f1)"
```
`manifest.sh` recomputes this against each downloaded zip and compares to the recorded `checksum` field — fail loud (non-zero exit, message naming the tag) on mismatch, matching `release-gate.sh`'s pattern of naming exactly what was expected vs. found (e.g. `"Found $count ci-success check runs on $sha; expected exactly one."` at `release-gate.sh:50`).

**Merge into the existing multi-version `PackageInfo[]` shape** (`scripts/package.sh:84-102`, the manifest `jq -n` call): this is the schema `manifest.sh` must produce, just with `versions[]` grown to one element per release instead of one:
```bash
jq -n \
	--slurpfile meta "$STAGE_DIR/plugin/meta.json" \
	--arg checksum "$checksum" --arg url "$URL_BASE/v$version/$zip_name" \
	'[{
		guid: $meta[0].guid,
		name: $meta[0].name,
		description: $meta[0].description,
		overview: $meta[0].overview,
		owner: $meta[0].owner,
		category: $meta[0].category,
		versions: [{
			version: $meta[0].version,
			changelog: $meta[0].changelog,
			targetAbi: $meta[0].targetAbi,
			sourceUrl: $url,
			checksum: $checksum,
			timestamp: $meta[0].timestamp
		}]
	}]'
```
Every field goes through `jq --arg`/`--slurpfile`, never raw string interpolation into a JSON literal — this is the established V5-input-validation convention this repository already follows, and it matters more here because a changelog entry (copied verbatim per D-08) can contain `"` or backslash characters.

---

### `tests/scripts/manifest.bats` (test, fake-`gh`-on-PATH seam)

**Analog:** `tests/scripts/release-gate.bats` (whole file)

**The fake-`gh`-dispatches-on-argv seam** (`release-gate.bats:32-98`):
```bash
setup() {
	FAKE_BIN_DIR="$BATS_TEST_TMPDIR/bin"
	mkdir -p "$FAKE_BIN_DIR"
	GH_ARGV_FILE="$BATS_TEST_TMPDIR/gh-argv"
	# ... one file per fixture body/stderr/exit-code, one setter function per fixture ...
	cat >"$FAKE_BIN_DIR/gh" <<'FAKE_GH'
#!/usr/bin/env bash
printf '%s\n' "$@" >>"$GH_ARGV_FILE"
for arg in "$@"; do
	case "$arg" in
	*/check-runs*)
		# ... print the right fixture, exit the right code ...
		;;
	esac
done
# fall-through case for whatever endpoint is not matched above
FAKE_GH
	chmod +x "$FAKE_BIN_DIR/gh"
	PATH="$FAKE_BIN_DIR:$PATH"
	export PATH
	export GH_REPO="owner/repo"
	export GH_TOKEN="sentinel-api-key-gh"
}
```
`manifest.bats` needs the same shape: a fake `gh` that dispatches on argv substrings (`*/releases*` for the enumeration call, `release download*` for the per-tag downloads), records every invocation to a file so a test can assert the exact arguments used (no `-L`/`--limit`, `--paginate` present), and lets each test override one fixture independently via a setter function. `manifest.bats` additionally needs a way to stage a fake zip on disk for the `gh release download --pattern '*.zip'` fake to "download" (copy a pre-built fixture zip into `--dir`), since `openssl dgst` in `manifest.sh` needs real bytes to hash.

**Required-action / unknown-action tests** (`release-gate.bats:263-279`, mirrored in `package.bats:20-30`):
```bash
@test "release-gate.sh with no action prints usage and fails" {
	run "$REPO_ROOT/scripts/release-gate.sh"
	[ "$status" -eq 2 ]
	[[ "$output" == *"Usage:"* ]]
}

@test "release-gate.sh with an unknown action prints usage and fails" {
	run "$REPO_ROOT/scripts/release-gate.sh" publish deadbee
	[ "$status" -eq 2 ]
	[[ "$output" == *"Usage:"* ]]
}
```
Copy verbatim for `manifest.sh`'s action argument.

**Argument-shape assertions on the fake `gh`'s recorded argv** (`release-gate.bats:114-122`):
```bash
@test "the request carries the filter in the query string and no field flag" {
	set_fixture_body '{"check_runs":[...]}'
	run "$REPO_ROOT/scripts/release-gate.sh" check deadbee
	[ "$status" -eq 0 ]

	grep -qx "api" "$GH_ARGV_FILE"
	grep -qx "repos/owner/repo/commits/deadbee/check-runs?check_name=ci-success" "$GH_ARGV_FILE"
	run ! grep -Eq -- '(^|/)(-f|--field|--raw-field)$' "$GH_ARGV_FILE"
}
```
Use this pattern to assert `manifest.sh` calls `gh api repos/.../releases --paginate` (never `gh release list`, and never a bare `--limit` under 30 as a substitute for `--paginate`).

**Failure-distinctness tests** (`release-gate.bats:169-185`, a 403 vs. a 422 producing different messages than "no results found"): apply the same idea to a failed `gh api releases` call vs. zero releases existing vs. a checksum mismatch — each must produce a distinctly-grep-able message per the repository's "include context... suggested fix" error-handling rule.

---

### `.github/workflows/pages.yml` (CI/CD workflow, event-driven)

**Analog:** `.github/workflows/release.yml` (trigger/concurrency/permissions shape) plus RESEARCH.md's Pattern 5 (the three-actions Pages deploy contract, verified against GitHub's own docs this session)

**Header, trigger, concurrency shape to copy** (`release.yml:1-19`):
```yaml
name: Release

# Runs when a version tag is pushed. ...

on:
  push:
    tags:
      - "v*"

permissions:
  contents: read

concurrency:
  group: ${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: false
```
`pages.yml` copies the `name:`, top-level `permissions: contents: read`, and `concurrency:` block shape, but changes `on:` to:
```yaml
on:
  workflow_run:
    workflows: ["Release"]
    types: [completed]
  workflow_dispatch:
```
with a job-level `if: github.event_name == 'workflow_dispatch' || github.event.workflow_run.conclusion == 'success'` guard (D-03). `cancel-in-progress` should likely be `true` here (unlike `release.yml`) since a stale in-flight Pages rebuild superseded by a newer one is safe to cancel — no partial-upload concern the way `gh release create` has.

**Checkout + mise-action steps to copy verbatim** (`release.yml:29-39`, identical in `ci.yml:38-47`):
```yaml
- name: Checkout
  uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
  with:
    persist-credentials: false

- name: Install pinned tools
  uses: jdx/mise-action@c2a87611a18de5b3828c5652fe268e992400cb5c # v4.3.0
```
Every workflow in this repository uses this exact pinned-SHA-plus-version-comment form with `persist-credentials: false`; `pages.yml` must match, per `~/.claude/rules/github-actions.md` and Phase 5's zizmor-pedantic gate.

**Job-level named permissions with inline justification comments** (`release.yml:26-28`, `ci.yml:27-36`):
```yaml
permissions:
  contents: write # gh release create
  checks: read # scripts/release-gate.sh: read the ci-success check run
```
`pages.yml`'s deploy job needs the equivalent commented block:
```yaml
permissions:
  contents: read # actions/checkout
  pages: write # actions/deploy-pages
  id-token: write # actions/deploy-pages OIDC
```
plus whatever read scope the build job's `gh api`/`gh release download` calls need (`contents: read` is sufficient for public-repo release reads with the default token — confirm this does not need `actions: read` the way `ci.yml`'s zizmor job does).

**The three-actions Pages contract** (RESEARCH.md, verified via `gh api` this session — SHAs current as of 2026-09-21, re-verify at implementation time per this repository's own version-bump rule):
```yaml
jobs:
  build:
    permissions:
      contents: read
    steps:
      - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
        with:
          persist-credentials: false
      - uses: jdx/mise-action@c2a87611a18de5b3828c5652fe268e992400cb5c # v4.3.0
      - run: mise run manifest   # scripts/manifest.sh rebuild -> artifacts/.../manifest.json
        env:
          GH_TOKEN: ${{ github.token }}
          GH_REPO: ${{ github.repository }}
      - uses: actions/upload-pages-artifact@fc324d3547104276b827a68afc52ff2a11cc49c9 # v5.0.0
        with:
          path: <dir containing the rebuilt manifest.json>

  deploy:
    needs: build
    permissions:
      contents: read
      pages: write
      id-token: write
    environment:
      name: github-pages
      url: ${{ steps.deployment.outputs.page_url }}
    steps:
      - uses: actions/configure-pages@45bfe0192ca1faeb007ade9deae92b16b8254a0d # v6.0.0
      - id: deployment
        uses: actions/deploy-pages@368f82528645a54fb793d4d04e342629a3f51346 # v5.0.1
      - name: Verify the published manifest   # D-05 smoke check
        run: scripts/manifest.sh verify        # or an equivalent action name
```

**Anti-pattern, explicitly do not do this** (RESEARCH.md Pitfall 4, verified against GitHub's fine-grained-PAT permission docs this session): do not add a `gh api -X POST /repos/{owner}/{repo}/pages` step inside `pages.yml` using `${{ github.token }}` to switch the Pages build type — that endpoint needs the Administration permission, which is not grantable to a workflow's `GITHUB_TOKEN`. This is a manual, maintainer-performed, one-time setting, same shape as the two tag-push checkpoints.

---

### `scripts/package.sh` (modified — changelog reader, refusal, `PACKAGE_VERSION` override)

**Analog:** itself — the existing small single-purpose helper style

**Existing helper shape to copy for `changelog_entry()`** (`scripts/package.sh:29-38`):
```bash
plugin_version() {
	sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$REPO_ROOT/Directory.Build.props" | head -1
}

# The target ABI is the Jellyfin.Controller version that the plugin is built against, with a fourth part.
target_abi() {
	local controller
	controller="$(sed -n 's:.*Include="Jellyfin.Controller" Version="\([^"]*\)".*:\1:p' "$PROJECT" | head -1)"
	echo "$controller.0"
}
```
New helper follows the same one-comment-then-function shape (RESEARCH.md's illustrative version, adapted to this repository's style):
```bash
# changelog_entry VERSION -> prints the CHANGELOG.md section body for VERSION, verbatim.
# Refuses (message naming the missing version) when no dated section exists (D-09).
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

**Environment-variable override convention to extend** (`scripts/package.sh:10-13`, `:21`, `:23`):
```bash
# Environment:
#   PACKAGE_OUTPUT_DIR  Output folder. Default: artifacts/release.
#   RELEASE_URL_BASE    Base of the zip download URL in the manifest. Default: this repository's GitHub releases.
#   RELEASE_TIMESTAMP   UTC timestamp in the metadata. Default: now.
...
readonly OUTPUT_DIR="${PACKAGE_OUTPUT_DIR:-$REPO_ROOT/artifacts/release}"
```
Add two new entries to the header comment and two new `${VAR:-default}` reads inside `build()`:
```bash
#   PACKAGE_VERSION     Override the packaged version (test-only; the DLL's own AssemblyVersion still comes from Directory.Build.props). Default: the version in Directory.Build.props.
#   CHANGELOG_PATH      Path to CHANGELOG.md. Default: CHANGELOG.md at the repository root.
```
```bash
version="${PACKAGE_VERSION:-$(plugin_version)}"
```
Document `PACKAGE_VERSION` as test-only directly in the header comment (D-17's explicit requirement), matching how `RELEASE_TIMESTAMP` and `RELEASE_URL_BASE` are already documented.

**Where the placeholder is replaced** (`scripts/package.sh:60-77`, specifically line 65): `changelog: ("Release " + $version)` becomes `changelog: $changelog` with a new `--arg changelog "$(changelog_entry "$version")"` added to the existing `jq -n` invocation's argument list — same `jq --arg` convention already used for every other field in that call.

**Refusal placement** (D-09: the earliest possible point, before `dotnet publish` even runs, so a broken build never gets attempted): call `changelog_entry "$version"` and `return 1` (propagating via `set -e`) at the top of `build()`, before the `dotnet publish` line at `:56` — mirroring how `check_tag()` (`:40-47`) refuses immediately with no side effects.

---

### `tests/scripts/package.bats` (modified — new cases)

**Analog:** itself

**`setup_file` environment-override pattern to extend** (`package.bats:4-12`):
```bash
setup_file() {
	REPO_ROOT="$(cd "$BATS_TEST_DIRNAME/../.." && pwd)"
	PACKAGE_OUTPUT_DIR="$BATS_FILE_TMPDIR/release"
	export REPO_ROOT PACKAGE_OUTPUT_DIR
	export RELEASE_TIMESTAMP="2026-09-17T12:00:00Z"
	export RELEASE_URL_BASE="https://example.test/releases/download"

	"$REPO_ROOT/scripts/package.sh" build >"$BATS_FILE_TMPDIR/build.out"
}
```
Add `export CHANGELOG_PATH="$BATS_FILE_TMPDIR/CHANGELOG.md"` with a fixture file written by `setup_file` before the `build` call (so the suite is not hostage to whatever `CHANGELOG.md` on disk currently says, though D-09's note says production `mise run test` still depends on the repo's real `CHANGELOG.md` having a dated section for the current `Directory.Build.props` version — the override lets `package.bats` test *both* paths independently).

**Assertion-on-generated-artifact pattern** (`package.bats:54-65`, the manifest test): add a parallel assertion:
```bash
@test "manifest.json carries the CHANGELOG.md section for this version verbatim" {
	expected="$(changelog fixture contents for $VERSION)"
	[ "$(jq -r '.[0].versions[0].changelog' "$MANIFEST")" = "$expected" ]
}
```
And a `meta.json` counterpart matching the existing `@test "meta.json describes the plugin for Jellyfin"` shape at `:42-52`.

**Refusal-test pattern to copy** (`package.bats:72-76`, `check-tag refuses a tag for another version`):
```bash
@test "check-tag refuses a tag for another version" {
	run "$REPO_ROOT/scripts/package.sh" check-tag "v0.0.1"
	[ "$status" -eq 1 ]
	[[ "$output" == *"does not match"* ]]
}
```
Same shape for D-09's refusal: build with a `CHANGELOG_PATH` fixture that has no dated section for the version under test, assert `status -eq 1` and the output names the missing version.

**`PACKAGE_VERSION` override test:** assert an unset override still reads `Directory.Build.props` (D-17's explicit ask), and a set override produces a zip/`meta.json`/manifest all agreeing on the overridden version — same `jq -r` assertion style as the existing manifest test.

---

## Shared Patterns

### Script structure (required action argument, usage exits 2, `main()` dispatch)
**Source:** `scripts/release-gate.sh:1-17`, `:86-103`; `scripts/package.sh:25-27`, `:107-126`
**Apply to:** `scripts/manifest.sh` (new)
```bash
usage() {
	echo "Usage: $0 build | check-tag TAG" >&2
}

main() {
	case "${1:-}" in
	build)
		build
		;;
	*)
		usage
		exit 2
		;;
	esac
}

main "$@"
```
Every script in `scripts/` follows this; never default to a mutating action (repository-wide rule, `~/projects/CLAUDE.md`).

### `gh api` over higher-level `gh` subcommands for anything exhaustive
**Source:** `scripts/release-gate.sh:27`, `:64`, `:73` (all `gh api`, none use `gh pr`/`gh release` convenience subcommands)
**Apply to:** `scripts/manifest.sh`'s release enumeration — never `gh release list` (30-item default cap, RESEARCH.md Pitfall 1)

### `jq --arg`/`--slurpfile`, never raw string interpolation into JSON
**Source:** `scripts/package.sh:60-77`, `:84-102`
**Apply to:** every JSON-writing step in `scripts/manifest.sh` — this matters more here than in `package.sh` because a changelog entry (copied verbatim) can contain characters that would corrupt a hand-built JSON string

### GitHub Actions pin-to-SHA-with-version-comment, `persist-credentials: false`, named `permissions:` with inline justification comments
**Source:** `.github/workflows/release.yml:26-39`; `.github/workflows/ci.yml:27-47`
**Apply to:** `.github/workflows/pages.yml` (new) — must also be `zizmor --persona=pedantic` clean per Phase 5's D-08 standing gate (named job, documented `permissions:`, a `concurrency:` block)

### Fake external-command-on-PATH bats seam
**Source:** `tests/scripts/release-gate.bats:32-98`
**Apply to:** `tests/scripts/manifest.bats` (new) — a fake `gh` dispatching on argv substrings, one fixture-setter function per distinct call site, an argv-recording file for shape assertions

### `awk`-based verbatim section extraction
**Source:** RESEARCH.md Pattern 4 (illustrative; no existing analog in this repository's shell scripts uses `awk` for section extraction, but the small-single-purpose-helper style of `plugin_version()`/`target_abi()` in `scripts/package.sh:29-38` is the shape to fit it into)
**Apply to:** `scripts/package.sh`'s new `changelog_entry()` helper

### e2e file independence, own Jellyfin users in `setup_file`, `reset_plugin_config`
**Source:** `.claude/rules/e2e.md`; `e2e/40-emby-outage.bats` (topic file that stops/starts a service, needs its own `teardown_file`)
**Apply to:** `e2e/NN-catalog-install.bats` (new) — per D-18's constraint, this file needs its own clean Jellyfin service (not the shared one from `setup_suite.bash`, since a catalog install writes into `/config/plugins/` which collides with the existing bind mount at `e2e/compose.yaml:24-28`) plus a manifest-serving nginx service, added to `e2e/compose.yaml` following the existing `emby-proxy` service's shape (`nginx:1.30.5-alpine`, already pinned, no new image dependency)

### `wait_until`/polling helpers for async operations
**Source:** `e2e/helpers.bash:58-70` (`wait_until`), `:244-267` (`run_migration_task`, which polls for task completion rather than assuming synchronous completion)
**Apply to:** the catalog install/update assertions in the new e2e file — `POST /Packages/Installed/{name}` returns 204 and installs asynchronously (RESEARCH.md, verified against `PackageController.cs`), so the test must poll `GET /Packages/Installed` or check the plugin's `meta.json` on disk rather than assume the install finished synchronously

### CHANGELOG.md keepachangelog structure
**Source:** `CHANGELOG.md:1-26` (existing `## Unreleased` + `### Added`/`### Removed`/`### Changed`/`### Upgrade note` subsections)
**Apply to:** the restructure — rename `## Unreleased` to `## [0.9.0.0] - <date>` and add a fresh empty `## Unreleased` above it (D-07); D-11 requires the `0.9.0.0` section body itself to be rewritten as first-release language, not carried over verbatim from the current delta-against-nothing text

## No Analog Found

None. Every file in this phase's scope has either a direct existing analog (`release-gate.sh`/`release-gate.bats` for the two new `manifest.*` files, `release.yml` + RESEARCH.md's verified Pages-actions contract for `pages.yml`) or is a modification of itself (every other file listed).

## Metadata

**Analog search scope:** `scripts/`, `tests/scripts/`, `.github/workflows/`, `e2e/`, plus the repository-root docs (`README.md`, `CLAUDE.md`, `docs/development.md`, `CHANGELOG.md`, `.mise.toml`)
**Files scanned:** `scripts/package.sh`, `scripts/release-gate.sh`, `tests/scripts/package.bats`, `tests/scripts/release-gate.bats`, `tests/scripts/workflows.bats`, `.github/workflows/release.yml`, `.github/workflows/ci.yml`, `.mise.toml`, `e2e/compose.yaml`, `e2e/helpers.bash`, `e2e/setup_suite.bash`, `README.md`, `CLAUDE.md`, `docs/development.md`, `CHANGELOG.md`
**Pattern extraction date:** 2026-09-21
