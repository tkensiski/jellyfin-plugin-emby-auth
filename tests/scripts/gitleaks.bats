#!/usr/bin/env bats
# Tests that .gitleaks.toml together with the `gitleaks dir` invocation below
# still detects a secret-shaped value. Task 2 of 05-03-PLAN.md proves the
# allowlist clears the repository's own fixture findings; it cannot prove the
# scanner still detects anything, because a scanner that reported nothing at
# all would pass that proof identically. These two tests close that gap.
#
# This behavior belongs to .gitleaks.toml and the scan `[tasks.lint]` runs,
# not to a script, so no file under scripts/ owns it. It lives here anyway
# because tests/scripts/ is already the repository's one non-e2e bats root;
# a dedicated lint-config test root would need five coordinated wiring edits
# (a `bats` entry in [tasks.test], the shellcheck/shfmt globs and the `test`
# hook's files pattern, and two docs mentions) for a single file.
#
# The detectable value is a 20-character string shaped like an AWS access key
# ID: `AKIA` followed by sixteen more characters. It was never issued by
# anyone, authenticates nothing, is in no secret store, and identifies no
# account. It is held as two halves in separate variables and joined only at
# run time, so the whole string never appears contiguously in this tracked
# file and never enters this repository's history. Neither half's variable
# name contains "key", "token", "secret", or "api", so the pair does not read
# as a key assignment to gitleaks' own scan of this file.
#
# AWS's own published example access key ID, ending in the word EXAMPLE, was
# measured against the pinned gitleaks 8.30.1 and produces zero findings and
# exit 0 under both the default rule set and this repository's config — using
# it here would make this test assert nothing while staying green. The value
# below was measured the other way: one finding, rule id `aws-access-token`,
# exit 1, under both configurations.

setup_file() {
	REPO_ROOT="$(cd "$BATS_TEST_DIRNAME/../.." && pwd)"
	export REPO_ROOT
}

# Scans $1 with the repository's own .gitleaks.toml passed explicitly.
# Auto-discovery looks beside the scanned path (fourth in gitleaks' config
# precedence), and the scanned path here is outside the repository, so the
# config would not otherwise be found.
scan_dir() {
	gitleaks dir --no-banner --no-color --redact -c "$REPO_ROOT/.gitleaks.toml" "$1"
}

@test "a detectable value in a directory outside the repository is reported" {
	[ "$BATS_TEST_TMPDIR" != "$REPO_ROOT" ]
	[[ "$BATS_TEST_TMPDIR" != "$REPO_ROOT"/* ]]

	local detect_head="AKIA"
	local detect_tail="TESTFIXTUREVALUE"
	printf '%s%s\n' "$detect_head" "$detect_tail" >"$BATS_TEST_TMPDIR/leaky.txt"

	run scan_dir "$BATS_TEST_TMPDIR"
	[ "$status" -eq 1 ]
	[[ "$output" =~ leaks\ found:\ [0-9]+ ]]
}

@test "a clean file in a directory outside the repository reports no leaks" {
	[ "$BATS_TEST_TMPDIR" != "$REPO_ROOT" ]
	[[ "$BATS_TEST_TMPDIR" != "$REPO_ROOT"/* ]]

	printf 'nothing interesting here\n' >"$BATS_TEST_TMPDIR/clean.txt"

	run scan_dir "$BATS_TEST_TMPDIR"
	[ "$status" -eq 0 ]
	[[ "$output" == *"no leaks found"* ]]
}
