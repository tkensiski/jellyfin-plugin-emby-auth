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
