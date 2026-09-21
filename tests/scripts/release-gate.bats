#!/usr/bin/env bats
# Tests for scripts/release-gate.sh. A fake `gh` on PATH supplies fixture JSON for
# every state; one test calls the real `gh` to prove the request shape is not a 404.

bats_require_minimum_version 1.5.0

set_fixture_body() { printf '%s' "$1" >"$FIXTURE_BODY_FILE"; }
set_fixture_stderr() { printf '%s' "$1" >"$FIXTURE_STDERR_FILE"; }
set_fixture_exit() { printf '%s' "$1" >"$FIXTURE_EXIT_FILE"; }

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
	export GH_ARGV_FILE FIXTURE_BODY_FILE FIXTURE_STDERR_FILE FIXTURE_EXIT_FILE
	: >"$GH_ARGV_FILE"
	: >"$FIXTURE_BODY_FILE"
	: >"$FIXTURE_STDERR_FILE"
	printf '0' >"$FIXTURE_EXIT_FILE"

	cat >"$FAKE_BIN_DIR/gh" <<'FAKE_GH'
#!/usr/bin/env bash
printf '%s\n' "$@" >>"$GH_ARGV_FILE"
if [ -s "$FIXTURE_STDERR_FILE" ]; then
	cat "$FIXTURE_STDERR_FILE" >&2
fi
cat "$FIXTURE_BODY_FILE"
exit "$(cat "$FIXTURE_EXIT_FILE")"
FAKE_GH
	chmod +x "$FAKE_BIN_DIR/gh"
	PATH="$FAKE_BIN_DIR:$PATH"
	export PATH
	export GH_REPO="owner/repo"
	export GH_TOKEN="sentinel-api-key-gh"
}

@test "no matching check run refuses with a message naming the absence" {
	skip "GREEN pending: scripts/release-gate.sh not yet implemented"
	set_fixture_body '{"check_runs":[]}'
	run "$REPO_ROOT/scripts/release-gate.sh" check deadbee
	[ "$status" -eq 1 ]
	[[ "$output" == *"No ci-success check run found"* ]]
}

@test "one completed success check run passes with no stderr" {
	skip "GREEN pending: scripts/release-gate.sh not yet implemented"
	set_fixture_body '{"check_runs":[{"name":"ci-success","status":"completed","conclusion":"success"}]}'
	run --separate-stderr "$REPO_ROOT/scripts/release-gate.sh" check deadbee
	[ "$status" -eq 0 ]
	[ -z "$stderr" ]
}

@test "the request carries the filter in the query string and no field flag" {
	skip "GREEN pending: scripts/release-gate.sh not yet implemented"
	set_fixture_body '{"check_runs":[{"name":"ci-success","status":"completed","conclusion":"success"}]}'
	run "$REPO_ROOT/scripts/release-gate.sh" check deadbee
	[ "$status" -eq 0 ]

	grep -qx "api" "$GH_ARGV_FILE"
	grep -qx "repos/owner/repo/commits/deadbee/check-runs?check_name=ci-success" "$GH_ARGV_FILE"
	run ! grep -Eq -- '(^|/)(-f|--field|--raw-field)$' "$GH_ARGV_FILE"
	[ "$status" -eq 0 ]
}

@test "the real gh sends a GET for the query-string filter form, not a 404-triggering POST" {
	if [ -z "$REAL_GH" ]; then
		skip "gh is not installed"
	fi
	if ! "$REAL_GH" auth status >/dev/null 2>&1; then
		skip "gh is not authenticated"
	fi

	cd "$REPO_ROOT" || return 1
	unset GH_REPO
	unset GH_TOKEN

	sha="0123456789abcdef0123456789abcdef01234567"
	run "$REAL_GH" api "repos/{owner}/{repo}/commits/$sha/check-runs?check_name=ci-success"
	[ "$status" -ne 0 ]
	[[ "$output" != *"404"* ]]
}
