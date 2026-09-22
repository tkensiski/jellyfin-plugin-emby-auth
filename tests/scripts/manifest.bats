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

setup() {
	DIR_A="$BATS_TEST_TMPDIR/a"
	DIR_B="$BATS_TEST_TMPDIR/b"
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
