#!/usr/bin/env bats
# Tests for scripts/manifest.sh's merge action. Fixtures are directories shaped exactly like
# scripts/package.sh build's output: one manifest.json (a length-1 PackageInfo[] array holding
# one version) and one zip named jellyfin-plugin-emby-auth_<version>.zip. merge reads local
# directories only, so these tests need no fake gh and no network.

bats_require_minimum_version 1.5.0

setup_file() {
	REPO_ROOT="$(cd "$BATS_TEST_DIRNAME/../.." && pwd)"
	export REPO_ROOT
}

# build_release_dir DIR VERSION GUID CHANGELOG [NAME] [DESCRIPTION] [OVERVIEW] [OWNER] [CATEGORY]
# -> creates DIR with a zip and a manifest.json shaped like scripts/package.sh build's output.
# The zip's bytes do not have to form a real archive; merge only hashes them, never opens them.
build_release_dir() {
	local dir="$1" version="$2" guid="$3" changelog="$4"
	local name="${5:-Emby Auth}" description="${6:-A description.}" overview="${7:-An overview.}"
	local owner="${8:-tkensiski}" category="${9:-Authentication}"
	local zip="$dir/jellyfin-plugin-emby-auth_$version.zip" checksum

	mkdir -p "$dir"
	printf 'fixture bytes for %s %s\n' "$guid" "$version" >"$zip"
	checksum="$(openssl dgst -md5 -r "$zip" | cut -d' ' -f1)"

	jq -n \
		--arg guid "$guid" --arg name "$name" --arg description "$description" \
		--arg overview "$overview" --arg owner "$owner" --arg category "$category" \
		--arg version "$version" --arg changelog "$changelog" --arg checksum "$checksum" \
		--arg url "https://example.test/v$version/jellyfin-plugin-emby-auth_$version.zip" \
		--arg timestamp "2026-09-17T12:00:00Z" \
		'[{
			guid: $guid, name: $name, description: $description, overview: $overview,
			owner: $owner, category: $category,
			versions: [{
				version: $version, changelog: $changelog, targetAbi: "12.1.0.0",
				sourceUrl: $url, checksum: $checksum, timestamp: $timestamp
			}]
		}]' >"$dir/manifest.json"
}

set_releases_body() { printf '%s' "$1" >"$RELEASES_BODY_FILE"; }
set_releases_stderr() { printf '%s' "$1" >"$RELEASES_STDERR_FILE"; }
set_releases_exit() { printf '%s' "$1" >"$RELEASES_EXIT_FILE"; }

# stage_releases TAG VERSION [TAG VERSION ...] -> builds a real, checksum-consistent
# manifest.json+zip fixture for each release under GH_RELEASE_FIXTURE_DIR with build_release_dir
# (the same helper the merge tests above use), and points the fake gh's release-enumeration
# fixture at exactly those tags, in the given order.
stage_releases() {
	local tags=() tag version
	while [[ $# -gt 0 ]]; do
		tag="$1"
		version="$2"
		shift 2
		build_release_dir "$GH_RELEASE_FIXTURE_DIR/$tag" "$version" "guid-1" "Changelog for $version."
		tags+=("$tag")
	done
	set_releases_body "$(printf '%s\n' "${tags[@]}")"
}

setup() {
	DIR_A="$BATS_TEST_TMPDIR/a"
	DIR_B="$BATS_TEST_TMPDIR/b"

	# rebuild/verify's fake-gh-on-PATH seam, the same shape release-gate.bats uses: an argv
	# file for shape assertions, a fixture-controlled release-enumeration response, a staging
	# directory the fake gh's release-download branch copies real fixture files out of (merge
	# hashes bytes, so a fake that printed nothing would fail every rebuild test for the wrong
	# reason), and a separate file recording each release-download invocation's tag and --dir
	# value so a test can assert two tags land in two distinct directories without parsing the
	# raw argv file by hand.
	FAKE_BIN_DIR="$BATS_TEST_TMPDIR/bin"
	mkdir -p "$FAKE_BIN_DIR"
	GH_ARGV_FILE="$BATS_TEST_TMPDIR/gh-argv"
	DOWNLOAD_DIR_FILE="$BATS_TEST_TMPDIR/gh-download-dirs"
	GH_RELEASE_FIXTURE_DIR="$BATS_TEST_TMPDIR/gh-fixtures"
	RELEASES_BODY_FILE="$BATS_TEST_TMPDIR/releases-body"
	RELEASES_STDERR_FILE="$BATS_TEST_TMPDIR/releases-stderr"
	RELEASES_EXIT_FILE="$BATS_TEST_TMPDIR/releases-exit"
	export FAKE_BIN_DIR GH_ARGV_FILE DOWNLOAD_DIR_FILE GH_RELEASE_FIXTURE_DIR
	export RELEASES_BODY_FILE RELEASES_STDERR_FILE RELEASES_EXIT_FILE
	mkdir -p "$GH_RELEASE_FIXTURE_DIR"
	: >"$GH_ARGV_FILE"
	: >"$DOWNLOAD_DIR_FILE"
	: >"$RELEASES_BODY_FILE"
	: >"$RELEASES_STDERR_FILE"
	printf '0' >"$RELEASES_EXIT_FILE"

	cat >"$FAKE_BIN_DIR/gh" <<'FAKE_GH'
#!/usr/bin/env bash
printf '%s\n' "$@" >>"$GH_ARGV_FILE"

for arg in "$@"; do
	case "$arg" in
	*/releases)
		if [ -s "$RELEASES_STDERR_FILE" ]; then
			cat "$RELEASES_STDERR_FILE" >&2
		fi
		cat "$RELEASES_BODY_FILE"
		exit "$(cat "$RELEASES_EXIT_FILE")"
		;;
	esac
done

if [ "${1:-}" = "release" ] && [ "${2:-}" = "download" ]; then
	tag="$3"
	dir=""
	pattern=""
	shift 3
	while [ $# -gt 0 ]; do
		case "$1" in
		--dir)
			dir="$2"
			shift 2
			;;
		--pattern)
			pattern="$2"
			shift 2
			;;
		*)
			shift
			;;
		esac
	done
	printf '%s\t%s\n' "$tag" "$dir" >>"$DOWNLOAD_DIR_FILE"

	src=""
	case "$pattern" in
	manifest.json)
		src="$GH_RELEASE_FIXTURE_DIR/$tag/manifest.json"
		;;
	'*.zip')
		src="$(find "$GH_RELEASE_FIXTURE_DIR/$tag" -maxdepth 1 -name '*.zip' -print -quit)"
		;;
	esac

	if [ -n "$src" ] && [ -f "$src" ]; then
		mkdir -p "$dir"
		cp "$src" "$dir/"
		exit 0
	fi
	echo "fake gh: no staged fixture for $tag matching $pattern" >&2
	exit 1
fi

echo "fake gh: unhandled invocation: $*" >&2
exit 1
FAKE_GH
	chmod +x "$FAKE_BIN_DIR/gh"
	PATH="$FAKE_BIN_DIR:$PATH"
	export PATH
	export GH_REPO="owner/repo"
	export GH_TOKEN="sentinel-api-key-gh"
}

# Test H
@test "manifest.sh with no action prints usage and fails" {
	run "$REPO_ROOT/scripts/manifest.sh"
	[ "$status" -eq 2 ]
	[[ "$output" == *"Usage:"* ]]
}

@test "manifest.sh with an unknown action prints usage and fails" {
	run "$REPO_ROOT/scripts/manifest.sh" publish
	[ "$status" -eq 2 ]
	[[ "$output" == *"Usage:"* ]]
}

@test "manifest.sh merge with no directory argument prints usage and fails" {
	run "$REPO_ROOT/scripts/manifest.sh" merge
	[ "$status" -eq 2 ]
	[[ "$output" == *"Usage:"* ]]
}

# Test I: the element count equals the number of source directories, read from the test's own
# directory list rather than a literal, so a third fixture directory cannot leave this stale.
@test "merge over two directories prints one entry whose versions length equals the number of source directories" {
	local dirs=("$DIR_A" "$DIR_B")
	build_release_dir "$DIR_A" "0.9.0.0" "guid-1" "First changelog."
	build_release_dir "$DIR_B" "0.10.0.0" "guid-1" "Second changelog."
	run "$REPO_ROOT/scripts/manifest.sh" merge "${dirs[@]}"
	[ "$status" -eq 0 ]
	[ "$(jq 'length' <<<"$output")" = "1" ]
	[ "$(jq '.[0].versions | length' <<<"$output")" = "${#dirs[@]}" ]
}

# Test J: 0.9.0.0 and 0.10.0.0 sort in the wrong order lexicographically, so this catches a sort
# that compares version strings rather than their numeric components.
@test "merge orders versions ascending numerically, not lexicographically" {
	build_release_dir "$DIR_A" "0.10.0.0" "guid-1" "Ten."
	build_release_dir "$DIR_B" "0.9.0.0" "guid-1" "Nine."
	run "$REPO_ROOT/scripts/manifest.sh" merge "$DIR_A" "$DIR_B"
	[ "$status" -eq 0 ]
	[ "$(jq -r '.[0].versions[0].version' <<<"$output")" = "0.9.0.0" ]
	[ "$(jq -r '.[0].versions[1].version' <<<"$output")" = "0.10.0.0" ]
}

# Test K: package-level fields come from the higher-versioned source; each version keeps its own
# fields; a changelog body with a quote and a backslash survives byte-identically.
@test "merge takes package fields from the highest version and keeps each version's own fields" {
	build_release_dir "$DIR_A" "0.9.0.0" "guid-1" \
		$'A line with a "quote" and a backslash \\ in it.' \
		"Emby Auth" "Old description." "Old overview." "tkensiski" "Authentication"
	build_release_dir "$DIR_B" "0.10.0.0" "guid-1" "Second changelog." \
		"Emby Auth" "New description." "New overview." "tkensiski" "Authentication"
	run "$REPO_ROOT/scripts/manifest.sh" merge "$DIR_A" "$DIR_B"
	[ "$status" -eq 0 ]
	[ "$(jq -r '.[0].description' <<<"$output")" = "New description." ]
	[ "$(jq -r '.[0].overview' <<<"$output")" = "New overview." ]
	[ "$(jq -r '.[0].versions[0].version' <<<"$output")" = "0.9.0.0" ]
	[ "$(jq -r '.[0].versions[0].changelog' <<<"$output")" = $'A line with a "quote" and a backslash \\ in it.' ]
	[ "$(jq -r '.[0].versions[1].version' <<<"$output")" = "0.10.0.0" ]
	[ "$(jq -r '.[0].versions[1].changelog' <<<"$output")" = "Second changelog." ]
}

# Test L: a mismatch names the directory and both hashes, and writes nothing to stdout.
@test "a checksum mismatch refuses, names the directory and both hashes, and prints nothing to stdout" {
	build_release_dir "$DIR_A" "0.9.0.0" "guid-1" "Fine."
	local recorded recomputed
	recorded="$(jq -r '.[0].versions[0].checksum' "$DIR_A/manifest.json")"
	printf 'tampered\n' >>"$DIR_A/jellyfin-plugin-emby-auth_0.9.0.0.zip"
	recomputed="$(openssl dgst -md5 -r "$DIR_A/jellyfin-plugin-emby-auth_0.9.0.0.zip" | cut -d' ' -f1)"

	run --separate-stderr "$REPO_ROOT/scripts/manifest.sh" merge "$DIR_A"
	[ "$status" -ne 0 ]
	[[ "$stderr" == *"$DIR_A"* ]]
	[[ "$stderr" == *"$recomputed"* ]]
	[[ "$stderr" == *"$recorded"* ]]
	[ -z "$output" ]
}

# Test M
@test "two directories declaring the same guid and version refuses and names the duplicated version" {
	build_release_dir "$DIR_A" "0.9.0.0" "guid-1" "First."
	build_release_dir "$DIR_B" "0.9.0.0" "guid-1" "Second."
	run "$REPO_ROOT/scripts/manifest.sh" merge "$DIR_A" "$DIR_B"
	[ "$status" -ne 0 ]
	[[ "$output" == *"0.9.0.0"* ]]
}

# Test N
@test "merge over a single directory prints an array with one entry and one version" {
	build_release_dir "$DIR_A" "0.9.0.0" "guid-1" "Solo."
	run "$REPO_ROOT/scripts/manifest.sh" merge "$DIR_A"
	[ "$status" -eq 0 ]
	[ "$(jq 'length' <<<"$output")" = "1" ]
	[ "$(jq '.[0].versions | length' <<<"$output")" = "1" ]
}

# Test O: each malformed-directory case refuses and names the offending directory.
@test "merge refuses, naming the directory, when manifest.json is absent" {
	mkdir -p "$DIR_A"
	printf 'zip bytes\n' >"$DIR_A/jellyfin-plugin-emby-auth_0.9.0.0.zip"
	run "$REPO_ROOT/scripts/manifest.sh" merge "$DIR_A"
	[ "$status" -ne 0 ]
	[[ "$output" == *"$DIR_A"* ]]
}

@test "merge refuses, naming the directory, when it holds no zip" {
	build_release_dir "$DIR_A" "0.9.0.0" "guid-1" "No zip."
	rm "$DIR_A/jellyfin-plugin-emby-auth_0.9.0.0.zip"
	run "$REPO_ROOT/scripts/manifest.sh" merge "$DIR_A"
	[ "$status" -ne 0 ]
	[[ "$output" == *"$DIR_A"* ]]
}

@test "merge refuses, naming the directory, when it holds more than one zip" {
	build_release_dir "$DIR_A" "0.9.0.0" "guid-1" "Extra zip."
	printf 'another zip\n' >"$DIR_A/second.zip"
	run "$REPO_ROOT/scripts/manifest.sh" merge "$DIR_A"
	[ "$status" -ne 0 ]
	[[ "$output" == *"$DIR_A"* ]]
}

@test "merge refuses, naming the directory, when the manifest holds more than one package" {
	build_release_dir "$DIR_A" "0.9.0.0" "guid-1" "Two packages."
	jq '. + [.[0]]' "$DIR_A/manifest.json" >"$DIR_A/manifest.json.tmp"
	mv "$DIR_A/manifest.json.tmp" "$DIR_A/manifest.json"
	run "$REPO_ROOT/scripts/manifest.sh" merge "$DIR_A"
	[ "$status" -ne 0 ]
	[[ "$output" == *"$DIR_A"* ]]
}

@test "merge refuses, naming the directory, when the manifest's package holds more than one version" {
	build_release_dir "$DIR_A" "0.9.0.0" "guid-1" "Two versions."
	jq '.[0].versions += [.[0].versions[0]]' "$DIR_A/manifest.json" >"$DIR_A/manifest.json.tmp"
	mv "$DIR_A/manifest.json.tmp" "$DIR_A/manifest.json"
	run "$REPO_ROOT/scripts/manifest.sh" merge "$DIR_A"
	[ "$status" -ne 0 ]
	[[ "$output" == *"$DIR_A"* ]]
}

# Test P
@test "merging the same two directories twice produces byte-identical output" {
	build_release_dir "$DIR_A" "0.9.0.0" "guid-1" "First."
	build_release_dir "$DIR_B" "0.10.0.0" "guid-1" "Second."

	run "$REPO_ROOT/scripts/manifest.sh" merge "$DIR_A" "$DIR_B"
	[ "$status" -eq 0 ]
	first_output="$output"

	run "$REPO_ROOT/scripts/manifest.sh" merge "$DIR_A" "$DIR_B"
	[ "$status" -eq 0 ]
	[ "$output" = "$first_output" ]
}

# Test Q
@test "rebuild with two releases writes one entry whose versions array has two elements and prints the path" {
	stage_releases "v0.9.0.0" "0.9.0.0" "v0.10.0.0" "0.10.0.0"
	MANIFEST_OUTPUT_DIR="$BATS_TEST_TMPDIR/pages"
	export MANIFEST_OUTPUT_DIR

	run "$REPO_ROOT/scripts/manifest.sh" rebuild
	[ "$status" -eq 0 ]
	[ "$output" = "$MANIFEST_OUTPUT_DIR/manifest.json" ]
	[ "$(jq 'length' "$output")" = "1" ]
	[ "$(jq '.[0].versions | length' "$output")" = "2" ]
}

# Test R: proves the enumeration is exhaustive (--paginate present) rather than capped, and that
# it hits the releases endpoint rather than some other one.
@test "rebuild's release enumeration call is paginated against the releases endpoint" {
	stage_releases "v0.9.0.0" "0.9.0.0" "v0.10.0.0" "0.10.0.0"
	MANIFEST_OUTPUT_DIR="$BATS_TEST_TMPDIR/pages"
	export MANIFEST_OUTPUT_DIR

	run "$REPO_ROOT/scripts/manifest.sh" rebuild
	[ "$status" -eq 0 ]

	grep -qx "api" "$GH_ARGV_FILE"
	grep -qx "repos/owner/repo/releases" "$GH_ARGV_FILE"
	grep -qx -- "--paginate" "$GH_ARGV_FILE"
}

# Test S: two tags cannot overwrite each other's manifest.json because each downloads into its
# own directory.
@test "rebuild downloads each tag into its own distinct directory" {
	stage_releases "v0.9.0.0" "0.9.0.0" "v0.10.0.0" "0.10.0.0"
	MANIFEST_OUTPUT_DIR="$BATS_TEST_TMPDIR/pages"
	export MANIFEST_OUTPUT_DIR

	run "$REPO_ROOT/scripts/manifest.sh" rebuild
	[ "$status" -eq 0 ]

	dir_a="$(awk -F'\t' '$1 == "v0.9.0.0" { print $2; exit }' "$DOWNLOAD_DIR_FILE")"
	dir_b="$(awk -F'\t' '$1 == "v0.10.0.0" { print $2; exit }' "$DOWNLOAD_DIR_FILE")"
	[ -n "$dir_a" ]
	[ -n "$dir_b" ]
	[ "$dir_a" != "$dir_b" ]
}

# Test T: a failed API call must never be read as a clean absence, so this message is distinct
# from Test U's.
@test "rebuild refuses and writes no manifest when the repository has no releases" {
	set_releases_body ""
	MANIFEST_OUTPUT_DIR="$BATS_TEST_TMPDIR/pages"
	export MANIFEST_OUTPUT_DIR

	run "$REPO_ROOT/scripts/manifest.sh" rebuild
	[ "$status" -ne 0 ]
	[[ "$output" == *"has no releases"* ]]
	[ ! -e "$MANIFEST_OUTPUT_DIR/manifest.json" ]
}

# Test U: distinct from Test T's empty-list message.
@test "rebuild refuses distinctly when the release enumeration call fails" {
	set_releases_exit 1
	set_releases_stderr "HTTP 403: Resource not accessible by integration"
	MANIFEST_OUTPUT_DIR="$BATS_TEST_TMPDIR/pages"
	export MANIFEST_OUTPUT_DIR

	run "$REPO_ROOT/scripts/manifest.sh" rebuild
	[ "$status" -ne 0 ]
	[[ "$output" == *"API call to list releases"* ]]
	[[ "$output" != *"has no releases"* ]]
}

# Test V: refuses before any request, so an empty owner-and-repo never reaches gh.
@test "rebuild refuses before any request when GH_REPO is unset" {
	unset GH_REPO
	: >"$GH_ARGV_FILE"

	run "$REPO_ROOT/scripts/manifest.sh" rebuild
	[ "$status" -ne 0 ]
	[[ "$output" == *"GH_REPO is not set"* ]]
	[ ! -s "$GH_ARGV_FILE" ]
}

# Test W: the message is the one merge() produces, proving rebuild routes through it rather than
# carrying a second checksum implementation.
@test "rebuild refuses and names the tag when a release's zip does not match its recorded checksum" {
	stage_releases "v0.9.0.0" "0.9.0.0" "v0.10.0.0" "0.10.0.0"
	printf 'tampered\n' >>"$GH_RELEASE_FIXTURE_DIR/v0.10.0.0/jellyfin-plugin-emby-auth_0.10.0.0.zip"
	MANIFEST_OUTPUT_DIR="$BATS_TEST_TMPDIR/pages"
	export MANIFEST_OUTPUT_DIR

	run --separate-stderr "$REPO_ROOT/scripts/manifest.sh" rebuild
	[ "$status" -ne 0 ]
	[[ "$stderr" == *"v0.10.0.0"* ]]
}

# Test X
@test "verify requires a URL argument, and against a matching file URL exits 0 reporting the count checked" {
	run "$REPO_ROOT/scripts/manifest.sh" verify
	[ "$status" -eq 2 ]
	[[ "$output" == *"Usage:"* ]]

	published="$BATS_TEST_TMPDIR/published"
	build_release_dir "$published" "0.9.0.0" "guid-1" "Fine."
	zip_name="$(basename "$(find "$published" -maxdepth 1 -name '*.zip' -print -quit)")"
	jq --arg url "file://$published/$zip_name" '.[0].versions[0].sourceUrl = $url' \
		"$published/manifest.json" >"$published/manifest.json.tmp"
	mv "$published/manifest.json.tmp" "$published/manifest.json"

	run "$REPO_ROOT/scripts/manifest.sh" verify "file://$published/manifest.json"
	[ "$status" -eq 0 ]
	[[ "$output" == *"1"* ]]
}

# Test Y
@test "verify refuses and names the version and both checksums when a zip does not match its recorded checksum" {
	published="$BATS_TEST_TMPDIR/published"
	build_release_dir "$published" "0.9.0.0" "guid-1" "Fine."
	zip_name="$(basename "$(find "$published" -maxdepth 1 -name '*.zip' -print -quit)")"
	recorded="$(jq -r '.[0].versions[0].checksum' "$published/manifest.json")"
	jq --arg url "file://$published/$zip_name" '.[0].versions[0].sourceUrl = $url' \
		"$published/manifest.json" >"$published/manifest.json.tmp"
	mv "$published/manifest.json.tmp" "$published/manifest.json"
	printf 'tampered\n' >>"$published/$zip_name"
	recomputed="$(openssl dgst -md5 -r "$published/$zip_name" | cut -d' ' -f1)"

	run --separate-stderr "$REPO_ROOT/scripts/manifest.sh" verify "file://$published/manifest.json"
	[ "$status" -ne 0 ]
	[[ "$stderr" == *"0.9.0.0"* ]]
	[[ "$stderr" == *"$recomputed"* ]]
	[[ "$stderr" == *"$recorded"* ]]
}

# Test Z: a document that fails to parse and a document that parses but is empty get distinct
# messages — an empty catalog is indistinguishable from a working one to anyone who does not
# count, so it needs its own message, not the parse failure's.
@test "verify refuses distinctly for a non-JSON body and for a manifest with zero version entries" {
	not_json="$BATS_TEST_TMPDIR/not-json.txt"
	printf 'this is not json\n' >"$not_json"
	run "$REPO_ROOT/scripts/manifest.sh" verify "file://$not_json"
	[ "$status" -ne 0 ]
	[[ "$output" == *"did not return a document that parses as JSON"* ]]

	empty="$BATS_TEST_TMPDIR/empty-manifest.json"
	printf '[]\n' >"$empty"
	run "$REPO_ROOT/scripts/manifest.sh" verify "file://$empty"
	[ "$status" -ne 0 ]
	[[ "$output" == *"zero version entries"* ]]
	[[ "$output" != *"did not return a document that parses as JSON"* ]]
}
