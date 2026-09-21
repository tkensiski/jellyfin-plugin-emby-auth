#!/usr/bin/env bats
# Tests for scripts/pre-public-audit.sh. Fake `gh` and `mise` binaries on PATH
# supply fixture data, so the script never reaches the real GitHub API or the
# real lint task during this suite.

bats_require_minimum_version 1.5.0

setup_file() {
	REPO_ROOT="$(cd "$BATS_TEST_DIRNAME/../.." && pwd)"
	export REPO_ROOT
}

setup() {
	FAKE_BIN_DIR="$BATS_TEST_TMPDIR/bin"
	mkdir -p "$FAKE_BIN_DIR"

	GH_ARGV_FILE="$BATS_TEST_TMPDIR/gh-argv"
	MISE_EXIT_FILE="$BATS_TEST_TMPDIR/mise-exit"
	VISIBILITY_FILE="$BATS_TEST_TMPDIR/visibility"
	RUNS_FILE="$BATS_TEST_TMPDIR/runs"
	ARTIFACTS_FILE="$BATS_TEST_TMPDIR/artifacts"
	ISSUES_FILE="$BATS_TEST_TMPDIR/issues"
	PULLS_FILE="$BATS_TEST_TMPDIR/pulls"
	RELEASES_FILE="$BATS_TEST_TMPDIR/releases"
	export GH_ARGV_FILE MISE_EXIT_FILE VISIBILITY_FILE RUNS_FILE ARTIFACTS_FILE ISSUES_FILE PULLS_FILE RELEASES_FILE

	: >"$GH_ARGV_FILE"
	printf '0' >"$MISE_EXIT_FILE"
	printf '{"visibility":"PRIVATE"}' >"$VISIBILITY_FILE"
	# Slurped shape: gh api --paginate --slurp wraps each page in an outer
	# array. actions/runs is a single-page fixture here; the two-page test
	# below overwrites RUNS_FILE with a second page to prove the script's jq
	# flattens across pages instead of reading only the first.
	printf '[{"workflow_runs":[{"id":1,"name":"CI","conclusion":"success"}]}]' >"$RUNS_FILE"
	printf '[{"artifacts":[]}]' >"$ARTIFACTS_FILE"
	printf '[[]]' >"$ISSUES_FILE"
	printf '[[]]' >"$PULLS_FILE"
	printf '[[]]' >"$RELEASES_FILE"

	cat >"$FAKE_BIN_DIR/mise" <<'FAKE_MISE'
#!/usr/bin/env bash
exit "$(cat "$MISE_EXIT_FILE")"
FAKE_MISE
	chmod +x "$FAKE_BIN_DIR/mise"

	# Records each invocation's argv as its own blank-line-separated paragraph,
	# so a test can inspect one call's flags without them bleeding into the
	# next call's. Endpoint dispatch scans every arg rather than assuming a
	# fixed position, because --paginate and --slurp sit between "api" and the
	# path.
	cat >"$FAKE_BIN_DIR/gh" <<'FAKE_GH'
#!/usr/bin/env bash
{
	printf '%s\n' "$@"
	printf '\n'
} >>"$GH_ARGV_FILE"
if [ "$1 $2" = "repo view" ]; then
	cat "$VISIBILITY_FILE"
	exit 0
fi
if [ "$1" = "api" ]; then
	for arg in "$@"; do
		case "$arg" in
		*/actions/runs)
			cat "$RUNS_FILE"
			exit 0
			;;
		*/actions/artifacts)
			cat "$ARTIFACTS_FILE"
			exit 0
			;;
		*/issues*)
			cat "$ISSUES_FILE"
			exit 0
			;;
		*/pulls*)
			cat "$PULLS_FILE"
			exit 0
			;;
		*/releases)
			cat "$RELEASES_FILE"
			exit 0
			;;
		esac
	done
fi
FAKE_GH
	chmod +x "$FAKE_BIN_DIR/gh"

	PATH="$FAKE_BIN_DIR:$PATH"
	export PATH
	export GH_REPO="owner/repo"
	export GH_TOKEN="sentinel-api-key-audit"
}

@test "pre-public-audit.sh with no action prints usage and fails" {
	run "$REPO_ROOT/scripts/pre-public-audit.sh"
	[ "$status" -eq 2 ]
	[[ "$output" == *"Usage:"* ]]
}

@test "pre-public-audit.sh with an unknown action prints usage and fails" {
	run "$REPO_ROOT/scripts/pre-public-audit.sh" publish
	[ "$status" -eq 2 ]
	[[ "$output" == *"Usage:"* ]]
}

@test "a clean mise run lint passes the secret-scan item and the whole run" {
	printf '0' >"$MISE_EXIT_FILE"
	run "$REPO_ROOT/scripts/pre-public-audit.sh" run
	[ "$status" -eq 0 ]
	[[ -n "$(grep '^PASS secret-scan' <<<"$output" || true)" ]]
}

@test "a failing mise run lint fails the secret-scan item and the whole run" {
	printf '1' >"$MISE_EXIT_FILE"
	run "$REPO_ROOT/scripts/pre-public-audit.sh" run
	[ "$status" -eq 1 ]
	[[ -n "$(grep '^FAIL secret-scan' <<<"$output" || true)" ]]
}

@test "empty artifacts, issues, pull requests, and releases each print an explicit zero line" {
	run "$REPO_ROOT/scripts/pre-public-audit.sh" run
	[ "$status" -eq 0 ]
	[[ "$output" == *"REVIEW artifacts: 0"* ]]
	[[ "$output" == *"REVIEW issues: 0"* ]]
	[[ "$output" == *"REVIEW pull-requests: 0"* ]]
	[[ "$output" == *"REVIEW releases: 0"* ]]
}

@test "two runs against the same fixtures print item labels in the same, documented order" {
	run "$REPO_ROOT/scripts/pre-public-audit.sh" run
	first_labels="$(grep -oE '^(PASS|FAIL|REVIEW) [a-z-]+' <<<"$output" | sed -E 's/^(PASS|FAIL|REVIEW) //')"

	run "$REPO_ROOT/scripts/pre-public-audit.sh" run
	second_labels="$(grep -oE '^(PASS|FAIL|REVIEW) [a-z-]+' <<<"$output" | sed -E 's/^(PASS|FAIL|REVIEW) //')"

	[ "$first_labels" = "$second_labels" ]

	expected="secret-scan
visibility
workflow-runs
artifacts
issues
pull-requests
releases"
	[ "$first_labels" = "$expected" ]
}

# CR-03 pins that every enumeration paginates rather than reading only the
# GitHub API's default first page of 30. Reads the argv fake gh records as
# blank-line-separated paragraphs, one per invocation, so each call's flags
# are checked on their own rather than across the whole file.
@test "every gh api enumeration passes --paginate and --slurp" {
	run "$REPO_ROOT/scripts/pre-public-audit.sh" run
	[ "$status" -eq 0 ]

	for endpoint in actions/runs actions/artifacts issues pulls releases; do
		run awk -v RS="" -v pat="$endpoint" '
			index($0, pat) {
				found = 1
				if (index($0, "--paginate") == 0) { print "missing --paginate for " pat; exit 1 }
				if (index($0, "--slurp") == 0) { print "missing --slurp for " pat; exit 1 }
			}
			END { if (!found) { print "no call matched " pat; exit 1 } }
		' "$GH_ARGV_FILE"
		[ "$status" -eq 0 ]
	done
}

# The argv assertion above cannot catch a wrong jq flatten — that only shows
# up in the reported count. A second page proves the count reflects both
# pages, not just the first.
@test "workflow-runs count and listing reflect a second page, not only the first" {
	printf '[{"workflow_runs":[{"id":1,"name":"CI","conclusion":"success"}]},{"workflow_runs":[{"id":2,"name":"CI","conclusion":"failure"}]}]' >"$RUNS_FILE"
	run "$REPO_ROOT/scripts/pre-public-audit.sh" run
	[ "$status" -eq 0 ]
	[[ "$output" == *"REVIEW workflow-runs: 2 runs"* ]]
	[[ "$output" == *"id=1 workflow=CI conclusion=success"* ]]
	[[ "$output" == *"id=2 workflow=CI conclusion=failure"* ]]
}
