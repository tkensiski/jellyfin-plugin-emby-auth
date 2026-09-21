#!/usr/bin/env bats
# Tests for scripts/release-gate.sh. A fake `gh` on PATH supplies fixture JSON for
# every state; one test calls the real `gh` to prove the request shape is not a 404.

bats_require_minimum_version 1.5.0

set_fixture_body() { printf '%s' "$1" >"$FIXTURE_BODY_FILE"; }
set_fixture_stderr() { printf '%s' "$1" >"$FIXTURE_STDERR_FILE"; }
set_fixture_exit() { printf '%s' "$1" >"$FIXTURE_EXIT_FILE"; }

# The default-branch lookup (`gh api repos/$GH_REPO --jq .default_branch`)
# and the compare call (`gh api .../compare/$branch...$sha --jq .status`)
# both apply --jq themselves in the real gh, so their fake output is the
# already-extracted plain text (a branch name, a compare status word), not
# JSON, matching what the real command would print.
set_default_branch() { printf '%s' "$1" >"$DEFAULT_BRANCH_BODY_FILE"; }
set_default_branch_stderr() { printf '%s' "$1" >"$DEFAULT_BRANCH_STDERR_FILE"; }
set_default_branch_exit() { printf '%s' "$1" >"$DEFAULT_BRANCH_EXIT_FILE"; }
set_compare_status() { printf '%s' "$1" >"$COMPARE_BODY_FILE"; }
set_compare_stderr() { printf '%s' "$1" >"$COMPARE_STDERR_FILE"; }
set_compare_exit() { printf '%s' "$1" >"$COMPARE_EXIT_FILE"; }

setup_file() {
	REPO_ROOT="$(cd "$BATS_TEST_DIRNAME/../.." && pwd)"
	export REPO_ROOT
	REAL_GH="$(command -v gh || true)"
	export REAL_GH
}

setup() {
	FAKE_BIN_DIR="$BATS_TEST_TMPDIR/bin"
	mkdir -p "$FAKE_BIN_DIR"
	GH_ARGV_FILE="$BATS_TEST_TMPDIR/gh-argv"
	FIXTURE_BODY_FILE="$BATS_TEST_TMPDIR/fixture-body"
	FIXTURE_STDERR_FILE="$BATS_TEST_TMPDIR/fixture-stderr"
	FIXTURE_EXIT_FILE="$BATS_TEST_TMPDIR/fixture-exit"
	DEFAULT_BRANCH_BODY_FILE="$BATS_TEST_TMPDIR/default-branch-body"
	DEFAULT_BRANCH_STDERR_FILE="$BATS_TEST_TMPDIR/default-branch-stderr"
	DEFAULT_BRANCH_EXIT_FILE="$BATS_TEST_TMPDIR/default-branch-exit"
	COMPARE_BODY_FILE="$BATS_TEST_TMPDIR/compare-body"
	COMPARE_STDERR_FILE="$BATS_TEST_TMPDIR/compare-stderr"
	COMPARE_EXIT_FILE="$BATS_TEST_TMPDIR/compare-exit"
	export GH_ARGV_FILE FIXTURE_BODY_FILE FIXTURE_STDERR_FILE FIXTURE_EXIT_FILE
	export DEFAULT_BRANCH_BODY_FILE DEFAULT_BRANCH_STDERR_FILE DEFAULT_BRANCH_EXIT_FILE
	export COMPARE_BODY_FILE COMPARE_STDERR_FILE COMPARE_EXIT_FILE
	: >"$GH_ARGV_FILE"
	: >"$FIXTURE_STDERR_FILE"
	printf '0' >"$FIXTURE_EXIT_FILE"
	# Happy-path defaults, so a test exercising only one step (the check run,
	# the default-branch lookup, or the compare) does not also have to wire
	# up the other two.
	set_fixture_body '{"check_runs":[{"name":"ci-success","status":"completed","conclusion":"success"}]}'
	set_default_branch "main"
	set_default_branch_stderr ""
	set_default_branch_exit "0"
	set_compare_status "identical"
	set_compare_stderr ""
	set_compare_exit "0"

	# Dispatches on which endpoint was requested rather than returning one
	# fixture regardless of argv, so the check-runs, default-branch, and
	# compare calls can each be scripted independently (WR-01, WR-02).
	cat >"$FAKE_BIN_DIR/gh" <<'FAKE_GH'
#!/usr/bin/env bash
printf '%s\n' "$@" >>"$GH_ARGV_FILE"
for arg in "$@"; do
	case "$arg" in
	*/check-runs*)
		if [ -s "$FIXTURE_STDERR_FILE" ]; then
			cat "$FIXTURE_STDERR_FILE" >&2
		fi
		cat "$FIXTURE_BODY_FILE"
		exit "$(cat "$FIXTURE_EXIT_FILE")"
		;;
	*/compare/*)
		if [ -s "$COMPARE_STDERR_FILE" ]; then
			cat "$COMPARE_STDERR_FILE" >&2
		fi
		cat "$COMPARE_BODY_FILE"
		exit "$(cat "$COMPARE_EXIT_FILE")"
		;;
	esac
done
# Anything else is the default-branch lookup: `gh api repos/$GH_REPO --jq .default_branch`.
if [ -s "$DEFAULT_BRANCH_STDERR_FILE" ]; then
	cat "$DEFAULT_BRANCH_STDERR_FILE" >&2
fi
cat "$DEFAULT_BRANCH_BODY_FILE"
exit "$(cat "$DEFAULT_BRANCH_EXIT_FILE")"
FAKE_GH
	chmod +x "$FAKE_BIN_DIR/gh"
	PATH="$FAKE_BIN_DIR:$PATH"
	export PATH
	export GH_REPO="owner/repo"
	export GH_TOKEN="sentinel-api-key-gh"
}

@test "no matching check run refuses with a message naming the absence" {
	set_fixture_body '{"check_runs":[]}'
	run "$REPO_ROOT/scripts/release-gate.sh" check deadbee
	[ "$status" -eq 1 ]
	[[ "$output" == *"No ci-success check run found"* ]]
}

@test "one completed success check run passes with no stderr" {
	set_fixture_body '{"check_runs":[{"name":"ci-success","status":"completed","conclusion":"success"}]}'
	run --separate-stderr "$REPO_ROOT/scripts/release-gate.sh" check deadbee
	[ "$status" -eq 0 ]
	[ -z "$stderr" ]
}

@test "the request carries the filter in the query string and no field flag" {
	set_fixture_body '{"check_runs":[{"name":"ci-success","status":"completed","conclusion":"success"}]}'
	run "$REPO_ROOT/scripts/release-gate.sh" check deadbee
	[ "$status" -eq 0 ]

	grep -qx "api" "$GH_ARGV_FILE"
	grep -qx "repos/owner/repo/commits/deadbee/check-runs?check_name=ci-success" "$GH_ARGV_FILE"
	run ! grep -Eq -- '(^|/)(-f|--field|--raw-field)$' "$GH_ARGV_FILE"
}

@test "an in-progress check run refuses and names in_progress" {
	set_fixture_body '{"check_runs":[{"name":"ci-success","status":"in_progress","conclusion":null}]}'
	run "$REPO_ROOT/scripts/release-gate.sh" check deadbee
	[ "$status" -eq 1 ]
	[[ "$output" == *"in_progress"* ]]
}

@test "a cancelled check run refuses and names cancelled" {
	set_fixture_body '{"check_runs":[{"name":"ci-success","status":"completed","conclusion":"cancelled"}]}'
	run "$REPO_ROOT/scripts/release-gate.sh" check deadbee
	[ "$status" -eq 1 ]
	[[ "$output" == *"cancelled"* ]]
}

@test "a failure check run refuses" {
	set_fixture_body '{"check_runs":[{"name":"ci-success","status":"completed","conclusion":"failure"}]}'
	run "$REPO_ROOT/scripts/release-gate.sh" check deadbee
	[ "$status" -eq 1 ]
}

@test "a skipped check run refuses" {
	set_fixture_body '{"check_runs":[{"name":"ci-success","status":"completed","conclusion":"skipped"}]}'
	run "$REPO_ROOT/scripts/release-gate.sh" check deadbee
	[ "$status" -eq 1 ]
}

@test "a timed_out check run refuses" {
	set_fixture_body '{"check_runs":[{"name":"ci-success","status":"completed","conclusion":"timed_out"}]}'
	run "$REPO_ROOT/scripts/release-gate.sh" check deadbee
	[ "$status" -eq 1 ]
}

@test "an unenumerated conclusion refuses because the script matches success exactly" {
	set_fixture_body '{"check_runs":[{"name":"ci-success","status":"completed","conclusion":"neutral"}]}'
	run "$REPO_ROOT/scripts/release-gate.sh" check deadbee
	[ "$status" -eq 1 ]
}

@test "two ci-success check runs refuses and states the count found" {
	set_fixture_body '{"check_runs":[{"name":"ci-success","status":"completed","conclusion":"success"},{"name":"ci-success","status":"completed","conclusion":"success"}]}'
	run "$REPO_ROOT/scripts/release-gate.sh" check deadbee
	[ "$status" -eq 1 ]
	[[ "$output" == *"2"* ]]
}

@test "a failed gh api call (403) refuses distinctly from a missing check run" {
	set_fixture_stderr "HTTP 403: Resource not accessible by integration"
	set_fixture_exit 1
	run "$REPO_ROOT/scripts/release-gate.sh" check deadbee
	[ "$status" -eq 1 ]
	[[ "$output" == *"API call to list check runs"* ]]
	[[ "$output" != *"No ci-success check run found"* ]]
}

@test "a failed gh api call (422, unknown SHA) refuses distinctly from a missing check run" {
	set_fixture_stderr "HTTP 422: No commit found for SHA"
	set_fixture_exit 1
	run "$REPO_ROOT/scripts/release-gate.sh" check deadbee
	[ "$status" -eq 1 ]
	[[ "$output" == *"API call to list check runs"* ]]
	[[ "$output" != *"No ci-success check run found"* ]]
}

# WR-01: the query filter (`?check_name=ci-success`) is not trusted alone —
# GitHub ignores query parameters it does not recognise, so a typo'd or
# dropped filter would otherwise return whatever check runs exist. The
# `count != 1` guard absorbs most of that, except the one case where exactly
# one check run exists and it is not named ci-success.
@test "a single check run with a different name refuses and names it" {
	set_fixture_body '{"check_runs":[{"name":"lint","status":"completed","conclusion":"success"}]}'
	run "$REPO_ROOT/scripts/release-gate.sh" check deadbee
	[ "$status" -eq 1 ]
	[[ "$output" == *"lint"* ]]
	[[ "$output" != *"No ci-success check run found"* ]]
}

@test "a mix of named check runs still finds ci-success among them" {
	set_fixture_body '{"check_runs":[{"name":"lint","status":"completed","conclusion":"success"},{"name":"ci-success","status":"completed","conclusion":"success"},{"name":"test","status":"completed","conclusion":"success"}]}'
	run --separate-stderr "$REPO_ROOT/scripts/release-gate.sh" check deadbee
	[ "$status" -eq 0 ]
	[ -z "$stderr" ]
}

# WR-02: a passing ci-success check run alone does not prove the commit was
# merged — the same check run appears on an open PR's feature-branch head
# because ci.yml triggers on pull_request. These pin the ancestry check
# against the default branch (read from the API, not hardcoded) that closes
# that gap.
@test "a commit identical to the default branch passes" {
	set_compare_status "identical"
	run --separate-stderr "$REPO_ROOT/scripts/release-gate.sh" check deadbee
	[ "$status" -eq 0 ]
	[ -z "$stderr" ]
}

@test "a commit behind the default branch passes" {
	set_compare_status "behind"
	run --separate-stderr "$REPO_ROOT/scripts/release-gate.sh" check deadbee
	[ "$status" -eq 0 ]
	[ -z "$stderr" ]
}

@test "a commit ahead of the default branch refuses" {
	set_compare_status "ahead"
	run "$REPO_ROOT/scripts/release-gate.sh" check deadbee
	[ "$status" -eq 1 ]
	[[ "$output" == *"ahead"* ]]
}

@test "a commit diverged from the default branch refuses" {
	set_compare_status "diverged"
	run "$REPO_ROOT/scripts/release-gate.sh" check deadbee
	[ "$status" -eq 1 ]
	[[ "$output" == *"diverged"* ]]
}

@test "an unrecognised compare status refuses" {
	set_compare_status "unknown-status"
	run "$REPO_ROOT/scripts/release-gate.sh" check deadbee
	[ "$status" -eq 1 ]
	[[ "$output" == *"unknown-status"* ]]
}

@test "a failed compare call refuses" {
	set_compare_stderr "HTTP 404: No common ancestor between the branches"
	set_compare_exit 1
	run "$REPO_ROOT/scripts/release-gate.sh" check deadbee
	[ "$status" -eq 1 ]
	[[ "$output" == *"compare"* ]]
}

@test "a failed default-branch lookup refuses" {
	set_default_branch_stderr "HTTP 404: Not Found"
	set_default_branch_exit 1
	run "$REPO_ROOT/scripts/release-gate.sh" check deadbee
	[ "$status" -eq 1 ]
	[[ "$output" == *"default branch"* ]]
}

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

@test "release-gate.sh check with no SHA prints usage and fails" {
	run "$REPO_ROOT/scripts/release-gate.sh" check
	[ "$status" -eq 2 ]
	[[ "$output" == *"Usage:"* ]]
}

@test "the real gh sends a GET for the query-string filter form, not a 404-triggering POST" {
	if [ -z "$REAL_GH" ]; then
		skip "gh is not installed"
	fi

	cd "$REPO_ROOT" || return 1
	unset GH_REPO
	unset GH_TOKEN

	if ! "$REAL_GH" auth status >/dev/null 2>&1; then
		skip "gh is not authenticated"
	fi

	sha="0123456789abcdef0123456789abcdef01234567"
	run "$REAL_GH" api "repos/{owner}/{repo}/commits/$sha/check-runs?check_name=ci-success"
	[ "$status" -ne 0 ]
	[[ "$output" != *"404"* ]]
}
