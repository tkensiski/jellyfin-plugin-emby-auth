#!/usr/bin/env bats
# Structural tests for the workflows and the README that a plain lint pass
# (shellcheck, actionlint, zizmor) cannot express: a trigger existing at all
# (WR-03), the shape of the ci-success job's own script (WR-12), the Pages job
# token scopes (T-06-03), and the single canonical manifest URL (T-06-04).

bats_require_minimum_version 1.5.0

setup_file() {
	REPO_ROOT="$(cd "$BATS_TEST_DIRNAME/../.." && pwd)"
	export REPO_ROOT
	CI_YML="$REPO_ROOT/.github/workflows/ci.yml"
	export CI_YML
	PAGES_YML="$REPO_ROOT/.github/workflows/pages.yml"
	export PAGES_YML
	README="$REPO_ROOT/README.md"
	export README
	# The one URL an administrator pastes into Jellyfin. A second, near-miss copy
	# is the spoofing risk T-06-04 names, so the assertion below counts it.
	MANIFEST_URL="https://tkensiski.github.io/jellyfin-plugin-emby-auth/manifest.json"
	export MANIFEST_URL
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

# WR-13: --offline (used by [tasks.lint] so a contributor with no GH_TOKEN
# can still lint locally) disables every zizmor audit that needs the GitHub
# API, so the standing gate never runs them. CI has a token, so the lint job
# should also run the online audits there. Pins the job-level permission and
# the wiring rather than the audit's own findings, which depend on live
# workflow content and are not this suite's concern.
@test "the lint job grants actions: read for the online zizmor audits" {
	run awk '/^  lint:$/{flag=1; next} flag && /^  [a-zA-Z]/{exit} flag' "$CI_YML"
	[ "$status" -eq 0 ]
	[[ "$output" == *"actions: read"* ]]
}

@test "the lint job runs the online zizmor audit task" {
	run grep -c "mise run lint-workflows-online" "$CI_YML"
	[ "$status" -eq 0 ]
	[ "$output" -eq 1 ]
}

@test "the online zizmor task does not pass --offline" {
	MISE_TOML="$REPO_ROOT/.mise.toml"
	run awk '/^\[tasks\.lint-workflows-online\]$/{flag=1; next} flag && /^\[/{exit} flag' "$MISE_TOML"
	[ "$status" -eq 0 ]
	[[ "$output" == *"zizmor"* ]]
	[[ "$output" != *"--offline"* ]]
}

# T-06-03: pages.yml publishes the manifest an administrator's server trusts. A
# `contents: write` anywhere in it would let a compromised workflow definition
# rewrite the repository the manifest is built from. Comments are stripped first
# so a comment naming the scope it withholds cannot fail the assertion; every
# `contents:` line in the file today carries exactly such a comment.
@test "pages.yml grants no write scope on repository contents" {
	run bash -c "sed 's/#.*//' \"$PAGES_YML\" | grep -c 'contents:[[:space:]]*write'"
	[ "$output" -eq 0 ]
}

# Guards the stripper itself: were it to drop the whole line rather than only the
# comment, the test above would pass on a file that does grant the scope.
@test "the pages.yml comment stripper keeps the scopes it is meant to read" {
	run bash -c "sed 's/#.*//' \"$PAGES_YML\" | grep -c 'contents:[[:space:]]*read'"
	[ "$output" -eq 3 ]
}

# T-06-04: the README names the manifest URL exactly once. A second copy differing
# by a character is the spoofing path — a reader pastes the wrong one into a server
# that will fetch and install whatever it names. Counts occurrences with `grep -o`,
# not lines: `grep -c` reports matching lines, so two copies on one line would read
# as one and the assertion would not hold.
@test "the README names the canonical manifest URL exactly once" {
	run bash -c "grep -o -F \"$MANIFEST_URL\" \"$README\" | wc -l | tr -d '[:space:]'"
	[ "$output" -eq 1 ]
}

# The URL must be HTTPS. GitHub Pages enforces it for *.github.io, but an http://
# copy in the README would still be the string a reader pastes.
@test "the README's manifest URL is https" {
	[[ "$MANIFEST_URL" == https://* ]]
	run grep -c -F "http://tkensiski.github.io" "$README"
	[ "$output" -eq 0 ]
}
