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

bats_require_minimum_version 1.5.0

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

# The four tests below pin the allowlist's scope rather than the scanner's
# ability to detect. The fixture-value allowlist is narrow in three directions
# at once, and each needs its own test because a config that got one wrong
# would still pass the other two:
#
#   - by path, so a fixture-shaped value committed to production source is
#     still reported rather than silently ignored repository-wide;
#   - by value, so any other secret inside tests/ or .planning/ is still
#     reported rather than those trees becoming blanket-exempt;
#   - by rule, so the fixture substring earns no exemption from a rule other
#     than the generic-api-key one it was allowlisted for.
#
# The second direction depends on `condition = "AND"` in .gitleaks.toml.
# gitleaks defaults an allowlist to "OR", where any one criterion suffices —
# under that default, adding `paths` would exempt every finding in those trees
# instead of narrowing anything.

# Builds a throwaway git repository at $1 from the remaining `path=content`
# arguments, then scans it with `gitleaks git` — the subcommand `mise run lint`
# runs, and the one that reports repository-relative paths. `gitleaks dir`
# reports absolute paths, so it cannot exercise repository-relative `paths`
# patterns and `scan_dir` above is no use for these three tests.
#
# Every step is checked and returns 2 — distinct from gitleaks' own 0 (clean)
# and 1 (leaks found) — because bats runs this under `run`, which disables
# errexit. A repository with no commit makes `gitleaks git` print "no leaks
# found" and exit 0, indistinguishable from a working allowlist, so an
# unchecked setup failure would let the "is ignored" test below pass while
# proving nothing.
scan_git_repo() {
	local repo="$1"
	shift

	# Override the invoking developer's global config: commit.gpgsign can fail
	# the fixture commit outright, and core.hooksPath can rewrite or reject it.
	local -a git_fixture=(
		git
		-c commit.gpgsign=false
		-c core.hooksPath=/dev/null
		-c user.email=test@example.invalid
		-c user.name=test
	)

	git init -q "$repo" || return 2

	local pair path content
	for pair in "$@"; do
		path="${pair%%=*}"
		content="${pair#*=}"
		mkdir -p "$repo/$(dirname "$path")" || return 2
		printf '%s\n' "$content" >"$repo/$path" || return 2
	done

	"${git_fixture[@]}" -C "$repo" add -A || return 2
	"${git_fixture[@]}" -C "$repo" commit -qm fixture || return 2
	[ "$("${git_fixture[@]}" -C "$repo" rev-list --count HEAD)" -eq 1 ] || return 2

	gitleaks git --no-banner --no-color --redact -c "$REPO_ROOT/.gitleaks.toml" "$repo"
}

# Held as two halves joined at run time, for the same reason the AKIA value
# above is: the whole string never appears contiguously in this tracked file,
# so it never becomes a finding in this repository's own history scan. Neither
# half's variable name contains "key", "token", "secret", or "api".
fixture_shaped_value() {
	local head="0123456789" tail="abcdef"
	printf '%s%s%s%s' "$head" "$tail" "$head" "$tail"
}

@test "a fixture value in production source is reported, not allowlisted repository-wide" {
	run scan_git_repo "$BATS_TEST_TMPDIR/outside" \
		"src/Leaky.cs=var apiKey = \"$(fixture_shaped_value)\";"
	[ "$status" -eq 1 ]
	[[ "$output" =~ leaks\ found:\ [0-9]+ ]]
}

@test "a non-fixture secret inside the fixture trees is reported" {
	local other_head="Zq7Z3mK9pLxW2nRvT8sY" other_tail="bHgJ4dFcQ1aE"

	run scan_git_repo "$BATS_TEST_TMPDIR/inside-other" \
		"tests/Other.cs=var apiKey = \"${other_head}${other_tail}\";"
	[ "$status" -eq 1 ]
	[[ "$output" =~ leaks\ found:\ [0-9]+ ]]
}

@test "a fixture value that trips another rule is still reported" {
	# The allowlist names generic-api-key in |targetRules|, so the fixture
	# substring must not buy an exemption from any other rule. This token
	# matches the allowlist regex and sits in tests/, yet trips github-pat, so
	# it must still be reported. Assembled at run time like the values above —
	# and unlike them, it would not be exempt if it appeared whole here.
	local pat_prefix="ghp_"

	run scan_git_repo "$BATS_TEST_TMPDIR/other-rule" \
		"tests/Pat.cs=var v = \"${pat_prefix}$(fixture_shaped_value)abcd\";"
	[ "$status" -eq 1 ]
	[[ "$output" =~ leaks\ found:\ [0-9]+ ]]
}

@test "a fixture value inside the fixture trees is ignored" {
	run scan_git_repo "$BATS_TEST_TMPDIR/inside-fixture" \
		"tests/Fixture.cs=var apiKey = \"$(fixture_shaped_value)\";"
	[ "$status" -eq 0 ]
	[[ "$output" == *"no leaks found"* ]]
	# Pins that a commit was actually scanned. Without this the assertions
	# above also describe an empty repository, which is what a silent setup
	# failure would produce.
	[[ "$output" == *"1 commits scanned"* ]]
}

# CR-01 pins that the CI job running `gitleaks git` clones full history rather
# than the `actions/checkout` default of depth 1, under which `gitleaks git`
# scans only the single commit in the clone and a secret committed and later
# removed is invisible to it. Extracts the named job's block from ci.yml with
# awk, rather than grepping the whole file for `fetch-depth: 0`, so the
# assertion cannot pass because some other job happens to carry it. A job
# boundary is a line at 2-space indent ending in a bare `key:`.
job_block() {
	local job="$1"
	awk -v job="  $job:" '
		$0 == job { flag = 1 }
		flag && /^  [a-z][a-zA-Z0-9-]*:$/ && $0 != job { exit }
		flag
	' "$REPO_ROOT/.github/workflows/ci.yml"
}

@test "CI's lint job checks out full history for the gitleaks history scan" {
	local block
	block="$(job_block lint)"
	[[ "$block" == *"fetch-depth: 0"* ]]
}

@test "CI's test and e2e jobs stay shallow" {
	local test_block e2e_block
	test_block="$(job_block test)"
	e2e_block="$(job_block e2e)"
	[[ "$test_block" != *"fetch-depth"* ]]
	[[ "$e2e_block" != *"fetch-depth"* ]]
}

# CR-02 pins that the secret scan runs on every commit, not only on commits
# that touch a file extension the `lint` hook's `files:` regex matches. A
# commit touching only .md or .planning/ files must still trigger it.
# Parses .pre-commit-config.yaml with node rather than grepping the whole
# file, so the assertion binds to one hook's own fields, not to any
# always_run/entry pair present anywhere in the file.
@test "a pre-commit hook runs the secret scan unconditionally, not gated by files:" {
	# shellcheck disable=SC2016 # single quotes are deliberate: the node
	# script's own template literals must reach node unexpanded by bash.
	run node -e '
		const fs = require("fs");
		const text = fs.readFileSync(process.argv[1], "utf8");
		const blocks = text.split(/\n(?=\s*-\s+id:)/);
		const secretHooks = blocks.filter((b) => /entry:\s*mise run secrets\b/.test(b));
		if (secretHooks.length !== 1) {
			console.error(`expected exactly one hook running mise run secrets, found ${secretHooks.length}`);
			process.exit(1);
		}
		const hook = secretHooks[0];
		if (!/^\s*always_run:\s*true\s*$/m.test(hook)) {
			console.error("the secrets hook is missing always_run: true");
			process.exit(1);
		}
		if (/^\s*files:/m.test(hook)) {
			console.error("the secrets hook must not carry a files: pattern");
			process.exit(1);
		}
	' "$REPO_ROOT/.pre-commit-config.yaml"
	[ "$status" -eq 0 ]
}
