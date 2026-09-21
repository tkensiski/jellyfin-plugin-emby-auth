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
	printf '{"workflow_runs":[{"id":1,"name":"CI","conclusion":"success"}]}' >"$RUNS_FILE"
	printf '{"artifacts":[]}' >"$ARTIFACTS_FILE"
	printf '[]' >"$ISSUES_FILE"
	printf '[]' >"$PULLS_FILE"
	printf '[]' >"$RELEASES_FILE"

	cat >"$FAKE_BIN_DIR/mise" <<'FAKE_MISE'
#!/usr/bin/env bash
exit "$(cat "$MISE_EXIT_FILE")"
FAKE_MISE
	chmod +x "$FAKE_BIN_DIR/mise"

	cat >"$FAKE_BIN_DIR/gh" <<'FAKE_GH'
#!/usr/bin/env bash
printf '%s\n' "$@" >>"$GH_ARGV_FILE"
case "$1 $2" in
"repo view")
	cat "$VISIBILITY_FILE"
	;;
"api "*)
	case "$2" in
	*/actions/runs) cat "$RUNS_FILE" ;;
	*/actions/artifacts) cat "$ARTIFACTS_FILE" ;;
	*/issues*) cat "$ISSUES_FILE" ;;
	*/pulls*) cat "$PULLS_FILE" ;;
	*/releases) cat "$RELEASES_FILE" ;;
	esac
	;;
esac
FAKE_GH
	chmod +x "$FAKE_BIN_DIR/gh"

	PATH="$FAKE_BIN_DIR:$PATH"
	export PATH
	export GH_REPO="owner/repo"
	export GH_TOKEN="sentinel-api-key-audit"
}

@test "pre-public-audit.sh with no action prints usage and fails" {
	skip "GREEN pending: scripts/pre-public-audit.sh not yet implemented"
	run "$REPO_ROOT/scripts/pre-public-audit.sh"
	[ "$status" -eq 2 ]
	[[ "$output" == *"Usage:"* ]]
}

@test "pre-public-audit.sh with an unknown action prints usage and fails" {
	skip "GREEN pending: scripts/pre-public-audit.sh not yet implemented"
	run "$REPO_ROOT/scripts/pre-public-audit.sh" publish
	[ "$status" -eq 2 ]
	[[ "$output" == *"Usage:"* ]]
}

@test "a clean mise run lint passes the secret-scan item and the whole run" {
	skip "GREEN pending: scripts/pre-public-audit.sh not yet implemented"
	printf '0' >"$MISE_EXIT_FILE"
	run "$REPO_ROOT/scripts/pre-public-audit.sh" run
	[ "$status" -eq 0 ]
	[[ -n "$(grep '^PASS secret-scan' <<<"$output" || true)" ]]
}

@test "a failing mise run lint fails the secret-scan item and the whole run" {
	skip "GREEN pending: scripts/pre-public-audit.sh not yet implemented"
	printf '1' >"$MISE_EXIT_FILE"
	run "$REPO_ROOT/scripts/pre-public-audit.sh" run
	[ "$status" -eq 1 ]
	[[ -n "$(grep '^FAIL secret-scan' <<<"$output" || true)" ]]
}

@test "empty artifacts, issues, pull requests, and releases each print an explicit zero line" {
	skip "GREEN pending: scripts/pre-public-audit.sh not yet implemented"
	run "$REPO_ROOT/scripts/pre-public-audit.sh" run
	[ "$status" -eq 0 ]
	[[ "$output" == *"REVIEW artifacts: 0"* ]]
	[[ "$output" == *"REVIEW issues: 0"* ]]
	[[ "$output" == *"REVIEW pull-requests: 0"* ]]
	[[ "$output" == *"REVIEW releases: 0"* ]]
}

@test "two runs against the same fixtures print item labels in the same, documented order" {
	skip "GREEN pending: scripts/pre-public-audit.sh not yet implemented"
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
