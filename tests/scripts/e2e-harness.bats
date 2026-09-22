#!/usr/bin/env bats
# Guards the e2e harness against the hang bats-core documents as issue #419: a command that leaves
# a long-lived child behind keeps the suite's output stream open after the last test, because the
# child inherits the descriptors bats opened for it. bats waits for its formatter, the formatter
# waits for end of file, and `bats e2e` blocks until that child exits.
#
# `dotnet publish` is such a command. A real compile makes Roslyn start VBCSCompiler with a
# ten-minute keep-alive. Measured on the unfixed harness: the server held the suite's output pipe
# and `mise run e2e` took 603 seconds for 74 seconds of test work. Measured with `ls -l /dev/fd`
# inside setup_suite: a child there inherits that pipe on fd 3 AND on fd 4, so closing fd 3 alone
# does not help. The harness stops the server being started at all instead.
#
# Each test runs the real command line out of the harness file, so the assertion binds to the
# shipping file rather than to a copy kept in sync by hand.

bats_require_minimum_version 1.5.0

setup_file() {
	REPO_ROOT="$(cd "$BATS_TEST_DIRNAME/../.." && pwd)"
	export REPO_ROOT
}

# write_roslyn_stub PATH -> writes an executable that behaves like a publish: it starts a
# keep-alive server that outlives it, unless UseSharedCompilation disables shared compilation.
# The server redirects its own standard streams, as Roslyn does, so only an inherited descriptor
# can keep the caller's stream open.
write_roslyn_stub() {
	local path="$1"
	mkdir -p "$(dirname "$path")"
	cat >"$path" <<-'STUB'
		#!/usr/bin/env bash
		if [[ "${UseSharedCompilation:-true}" != "false" ]]; then
			( exec sleep "${STUB_SERVER_SECONDS:-15}" ) >/dev/null 2>&1 </dev/null &
		fi
		echo "stub invoked: $*"
	STUB
	chmod +x "$path"
}

# stream_state COMMAND -> runs COMMAND in a subshell whose fd 3 and fd 4 are a fifo, the layout
# bats gives a command in setup_suite, then prints "eof" if the fifo closed once COMMAND returned
# or "blocked" if a surviving child still holds it. "blocked" is the hang.
stream_state() {
	local cmd="$1"
	local fifo="$BATS_TEST_TMPDIR/stream.$BATS_SUITE_TEST_NUMBER"
	mkfifo "$fifo"
	cat <"$fifo" >"$fifo.log" 2>&1 &
	local reader=$!

	(
		exec 3>"$fifo" 4>"$fifo"
		eval "$cmd"
	) >/dev/null 2>&1

	local _
	for _ in $(seq 1 50); do
		if ! kill -0 "$reader" 2>/dev/null; then
			printf 'eof'
			return 0
		fi
		sleep 0.1
	done
	kill "$reader" 2>/dev/null
	printf 'blocked'
}

@test "setup_suite's plugin publish leaves no keep-alive server holding the output stream" {
	local line
	line="$(grep -E 'dotnet publish ' "$REPO_ROOT/e2e/setup_suite.bash")"
	[ -n "$line" ]

	local stub_dir="$BATS_TEST_TMPDIR/bin"
	write_roslyn_stub "$stub_dir/dotnet"

	E2E_DIR="$BATS_TEST_TMPDIR/e2e" PATH="$stub_dir:$PATH" run stream_state "$line"
	[ "$status" -eq 0 ]
	[ "$output" = "eof" ]
}

@test "the catalog test's package build leaves no keep-alive server holding the output stream" {
	local line
	line="$(grep -E 'package\.sh" build' "$REPO_ROOT/e2e/85-catalog-install.bats")"
	[ -n "$line" ]

	local fake_root="$BATS_TEST_TMPDIR/root"
	write_roslyn_stub "$fake_root/scripts/package.sh"

	REPO_ROOT="$fake_root" run stream_state "$line"
	[ "$status" -eq 0 ]
	[ "$output" = "eof" ]
}

# Proves the two tests above can fail: the same stub, run the way the harness used to run it,
# still holds the stream open. Without this, a stub that never starts a server would make both
# tests pass no matter what the harness does.
@test "the guard detects a publish that does leave a keep-alive server behind" {
	local stub_dir="$BATS_TEST_TMPDIR/bin"
	write_roslyn_stub "$stub_dir/dotnet"

	PATH="$stub_dir:$PATH" run stream_state 'dotnet publish /some/project.csproj -c Release >&3'
	[ "$status" -eq 0 ]
	[ "$output" = "blocked" ]
}

# Closing fd 3 was the obvious fix and is not enough, because bats opens fd 4 on the same stream.
# This pins that measurement, so nobody replaces the harness fix with `3>&-` and expects it to work.
@test "closing only fd 3 does not stop a keep-alive server holding the output stream" {
	local stub_dir="$BATS_TEST_TMPDIR/bin"
	write_roslyn_stub "$stub_dir/dotnet"

	PATH="$stub_dir:$PATH" run stream_state 'dotnet publish /some/project.csproj -c Release >&3 3>&-'
	[ "$status" -eq 0 ]
	[ "$output" = "blocked" ]
}
