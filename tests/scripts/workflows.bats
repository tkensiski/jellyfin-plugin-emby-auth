#!/usr/bin/env bats
# Structural tests for .github/workflows/ci.yml that a plain shellcheck/actionlint
# pass cannot express: a trigger existing at all (WR-03), and the shape of the
# ci-success job's own script (WR-12).

bats_require_minimum_version 1.5.0

setup_file() {
	REPO_ROOT="$(cd "$BATS_TEST_DIRNAME/../.." && pwd)"
	export REPO_ROOT
	CI_YML="$REPO_ROOT/.github/workflows/ci.yml"
	export CI_YML
}

# WR-03: a commit that never got a check suite (ci.yml triggers only on
# pull_request and push-to-main, so a non-head commit on a push gets nothing)
# has no way to earn a ci-success run without a manual trigger. Extracts the
# top-level `on:` block up to the next top-level key, so the assertion binds
# to the trigger list rather than to `workflow_dispatch:` appearing anywhere
# in the file.
@test "ci.yml can be triggered manually so an ungated commit can get a ci-success run" {
	run awk '/^on:/{flag=1; next} flag && /^[a-zA-Z]/{exit} flag' "$CI_YML"
	[ "$status" -eq 0 ]
	[[ "$output" == *"workflow_dispatch:"* ]]
}

# WR-12: the ci-success step's loop passes when RESULTS is empty (zero
# iterations, exit 0) and does not check how many results actually arrived,
# so a job added to `jobs:` and left out of `needs:` cannot be told apart
# from a job that ran and succeeded. Extracts the step's own embedded script
# and runs it standalone, so the assertion binds to the script ci.yml runs
# rather than to a copy kept in sync by hand.
extract_ci_success_script() {
	awk '
		/^      - name: Require every job to succeed$/ { in_step = 1 }
		in_step && /^        run: \|$/ { in_run = 1; next }
		in_run && /^          / { print substr($0, 11); next }
		in_run { exit }
	' "$CI_YML"
}

@test "the ci-success script passes when every expected result is success" {
	extract_ci_success_script >"$BATS_TEST_TMPDIR/ci-success.sh"
	RESULTS="success success success" run bash "$BATS_TEST_TMPDIR/ci-success.sh"
	[ "$status" -eq 0 ]
}

@test "the ci-success script fails when a result is not success" {
	extract_ci_success_script >"$BATS_TEST_TMPDIR/ci-success.sh"
	RESULTS="success failure success" run bash "$BATS_TEST_TMPDIR/ci-success.sh"
	[ "$status" -eq 1 ]
}

@test "the ci-success script fails when RESULTS is empty" {
	extract_ci_success_script >"$BATS_TEST_TMPDIR/ci-success.sh"
	RESULTS="" run bash "$BATS_TEST_TMPDIR/ci-success.sh"
	[ "$status" -eq 1 ]
}

@test "the ci-success script fails when fewer results arrive than expected" {
	extract_ci_success_script >"$BATS_TEST_TMPDIR/ci-success.sh"
	RESULTS="success success" run bash "$BATS_TEST_TMPDIR/ci-success.sh"
	[ "$status" -eq 1 ]
}
