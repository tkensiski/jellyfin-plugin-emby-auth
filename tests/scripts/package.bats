#!/usr/bin/env bats
# Tests for scripts/package.sh. One build in setup_file, then checks on the output.

# write_changelog_fixture PATH VERSION BODY_LINE -> writes a minimal keepachangelog-shaped
# fixture at PATH with one dated section for VERSION, whose body is BODY_LINE. Building the
# fixture from the version read at run time, rather than a hard-coded string, keeps this file
# working across the 0.9.0.0 and 1.0.0.0 version changes later in the phase.
write_changelog_fixture() {
	local path="$1" version="$2" body_line="$3"
	cat >"$path" <<EOF
# Changelog

## [$version] - 2026-09-17

### Added

- $body_line
EOF
}

setup_file() {
	REPO_ROOT="$(cd "$BATS_TEST_DIRNAME/../.." && pwd)"
	PACKAGE_OUTPUT_DIR="$BATS_FILE_TMPDIR/release"
	export REPO_ROOT PACKAGE_OUTPUT_DIR
	export RELEASE_TIMESTAMP="2026-09-17T12:00:00Z"
	export RELEASE_URL_BASE="https://example.test/releases/download"

	VERSION_FROM_PROPS="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$REPO_ROOT/Directory.Build.props" | head -1)"
	OVERRIDE_VERSION="9.9.9.9"
	# shellcheck disable=SC2089,SC2090 # always expanded inside double quotes below, never as a command's argument list.
	CHANGELOG_BODY_QUOTE_AND_BACKSLASH='A line with a "quote" and a backslash \ in it.'
	CHANGELOG_FIXTURE="$BATS_FILE_TMPDIR/CHANGELOG.md"
	cat >"$CHANGELOG_FIXTURE" <<EOF
# Changelog

## [$OVERRIDE_VERSION] - 2026-09-18

### Added

- The override build's own changelog entry.

## [$VERSION_FROM_PROPS] - 2026-09-17

### Added

- $CHANGELOG_BODY_QUOTE_AND_BACKSLASH

Second paragraph after a blank line.
EOF
	# shellcheck disable=SC2090 # CHANGELOG_BODY_QUOTE_AND_BACKSLASH is always expanded inside double quotes, never as a command's argument list.
	export VERSION_FROM_PROPS OVERRIDE_VERSION CHANGELOG_BODY_QUOTE_AND_BACKSLASH CHANGELOG_FIXTURE
	CHANGELOG_PATH="$CHANGELOG_FIXTURE" "$REPO_ROOT/scripts/package.sh" build >"$BATS_FILE_TMPDIR/build.out"

	OVERRIDE_OUTPUT_DIR="$BATS_FILE_TMPDIR/release-override"
	export OVERRIDE_OUTPUT_DIR
	PACKAGE_VERSION="$OVERRIDE_VERSION" CHANGELOG_PATH="$CHANGELOG_FIXTURE" PACKAGE_OUTPUT_DIR="$OVERRIDE_OUTPUT_DIR" \
		"$REPO_ROOT/scripts/package.sh" build >"$BATS_FILE_TMPDIR/build-override.out"
}

setup() {
	VERSION="$VERSION_FROM_PROPS"
	ZIP="$PACKAGE_OUTPUT_DIR/jellyfin-plugin-emby-auth_$VERSION.zip"
	MANIFEST="$PACKAGE_OUTPUT_DIR/manifest.json"
	OVERRIDE_ZIP="$OVERRIDE_OUTPUT_DIR/jellyfin-plugin-emby-auth_$OVERRIDE_VERSION.zip"
	OVERRIDE_MANIFEST="$OVERRIDE_OUTPUT_DIR/manifest.json"
}

@test "package.sh without an action prints usage and fails" {
	run "$REPO_ROOT/scripts/package.sh"
	[ "$status" -eq 2 ]
	[[ "$output" == *"Usage:"* ]]
}

@test "package.sh with an unknown action prints usage and fails" {
	run "$REPO_ROOT/scripts/package.sh" publish
	[ "$status" -eq 2 ]
	[[ "$output" == *"Usage:"* ]]
}

@test "build prints the path of the zip" {
	[ "$(cat "$BATS_FILE_TMPDIR/build.out")" = "$ZIP" ]
}

@test "the zip contains only the plugin DLL and meta.json" {
	run unzip -Z1 "$ZIP"
	[ "$status" -eq 0 ]
	[ "$(sort <<<"$output" | tr '\n' ' ')" = "Jellyfin.Plugin.EmbyAuth.dll meta.json " ]
}

@test "meta.json describes the plugin for Jellyfin" {
	meta="$(unzip -p "$ZIP" meta.json)"
	[ "$(jq -r .guid <<<"$meta")" = "e973e09a-e8b4-40c1-9be2-8e51342de1f9" ]
	[ "$(jq -r .name <<<"$meta")" = "Emby Auth" ]
	[ "$(jq -r .version <<<"$meta")" = "$VERSION" ]
	[ "$(jq -r .targetAbi <<<"$meta")" = "12.1.0.0" ]
	[ "$(jq -r .status <<<"$meta")" = "Active" ]
	[ "$(jq -r .autoUpdate <<<"$meta")" = "false" ]
	[ "$(jq -r .timestamp <<<"$meta")" = "$RELEASE_TIMESTAMP" ]
	[ "$(jq -c .assemblies <<<"$meta")" = '["Jellyfin.Plugin.EmbyAuth.dll"]' ]
}

@test "manifest.json lists this version with the MD5 checksum and download URL of the zip" {
	checksum="$(openssl dgst -md5 -r "$ZIP" | cut -d' ' -f1)"

	[ "$(jq length "$MANIFEST")" = "1" ]
	[ "$(jq -r '.[0].guid' "$MANIFEST")" = "e973e09a-e8b4-40c1-9be2-8e51342de1f9" ]
	[ "$(jq -r '.[0].versions | length' "$MANIFEST")" = "1" ]
	[ "$(jq -r '.[0].versions[0].version' "$MANIFEST")" = "$VERSION" ]
	[ "$(jq -r '.[0].versions[0].targetAbi' "$MANIFEST")" = "12.1.0.0" ]
	[ "$(jq -r '.[0].versions[0].checksum' "$MANIFEST")" = "$checksum" ]
	[ "$(jq -r '.[0].versions[0].sourceUrl' "$MANIFEST")" = "$RELEASE_URL_BASE/v$VERSION/jellyfin-plugin-emby-auth_$VERSION.zip" ]
	[ "$(jq -r '.[0].versions[0].timestamp' "$MANIFEST")" = "$RELEASE_TIMESTAMP" ]
}

@test "check-tag accepts the tag of the plugin version" {
	run "$REPO_ROOT/scripts/package.sh" check-tag "v$VERSION"
	[ "$status" -eq 0 ]
}

@test "check-tag refuses a tag for another version" {
	run "$REPO_ROOT/scripts/package.sh" check-tag "v0.0.1"
	[ "$status" -eq 1 ]
	[[ "$output" == *"does not match"* ]]
}

# Test A: meta.json and the manifest carry the CHANGELOG.md section for this version verbatim,
# byte-identical, including the double-quote and backslash the fixture body deliberately holds.
@test "meta.json carries the CHANGELOG.md section verbatim" {
	meta="$(unzip -p "$ZIP" meta.json)"
	expected=$'\n### Added\n\n- '"$CHANGELOG_BODY_QUOTE_AND_BACKSLASH"$'\n\nSecond paragraph after a blank line.'
	[ "$(jq -r .changelog <<<"$meta")" = "$expected" ]
}

@test "manifest.json carries the same CHANGELOG.md section verbatim as meta.json" {
	expected=$'\n### Added\n\n- '"$CHANGELOG_BODY_QUOTE_AND_BACKSLASH"$'\n\nSecond paragraph after a blank line.'
	[ "$(jq -r '.[0].versions[0].changelog' "$MANIFEST")" = "$expected" ]
}

# Test B: no dated section for the version under test -> build refuses, names the version, and
# produces no zip.
@test "build refuses when the changelog has no dated section for the version" {
	local fixture="$BATS_TEST_TMPDIR/CHANGELOG.md" out="$BATS_TEST_TMPDIR/out"
	write_changelog_fixture "$fixture" "0.0.1.0" "Unrelated version's entry."
	mkdir -p "$out"
	run env CHANGELOG_PATH="$fixture" PACKAGE_OUTPUT_DIR="$out" "$REPO_ROOT/scripts/package.sh" build
	[ "$status" -eq 1 ]
	[[ "$output" == *"$VERSION"* ]]
	[[ "$output" == *"no dated section"* ]]
	[ -z "$(ls -A "$out")" ]
}

# Test C: the section exists and is dated, but its body is only blank lines -> refuses with a
# message distinct from Test B's.
@test "build refuses when the changelog section for the version is empty" {
	local fixture="$BATS_TEST_TMPDIR/CHANGELOG.md" out="$BATS_TEST_TMPDIR/out"
	cat >"$fixture" <<EOF
# Changelog

## [$VERSION] - 2026-09-17


## [0.0.1.0] - 2026-09-01

### Added

- Something else.
EOF
	mkdir -p "$out"
	run env CHANGELOG_PATH="$fixture" PACKAGE_OUTPUT_DIR="$out" "$REPO_ROOT/scripts/package.sh" build
	[ "$status" -eq 1 ]
	[[ "$output" == *"$VERSION"* ]]
	[[ "$output" == *"empty"* ]]
	[[ "$output" != *"no dated section"* ]]
}

# Test D: two dated sections for the same version -> refuses and names that version, with a
# message distinct from Test B's and Test C's.
@test "build refuses when the changelog has two dated sections for the same version" {
	local fixture="$BATS_TEST_TMPDIR/CHANGELOG.md" out="$BATS_TEST_TMPDIR/out"
	cat >"$fixture" <<EOF
# Changelog

## [$VERSION] - 2026-09-17

### Added

- First section.

## [$VERSION] - 2026-09-10

### Added

- Second, duplicate section.
EOF
	mkdir -p "$out"
	run env CHANGELOG_PATH="$fixture" PACKAGE_OUTPUT_DIR="$out" "$REPO_ROOT/scripts/package.sh" build
	[ "$status" -eq 1 ]
	[[ "$output" == *"$VERSION"* ]]
	[[ "$output" == *"more than one"* ]]
	[[ "$output" != *"no dated section"* ]]
	[[ "$output" != *"empty"* ]]
}

# Test E: the version under test is the second dated section in the file, not the first ->
# extraction does not depend on position, and the extracted body is byte-identical.
@test "changelog extraction does not depend on the section's position in the file" {
	local fixture="$BATS_TEST_TMPDIR/CHANGELOG.md" out="$BATS_TEST_TMPDIR/out"
	cat >"$fixture" <<EOF
# Changelog

## [0.0.1.0] - 2026-09-01

### Added

- An earlier, unrelated section.

## [$VERSION] - 2026-09-17

- The section under test, positioned second.
EOF
	mkdir -p "$out"
	CHANGELOG_PATH="$fixture" PACKAGE_OUTPUT_DIR="$out" "$REPO_ROOT/scripts/package.sh" build >/dev/null
	meta="$(unzip -p "$out/jellyfin-plugin-emby-auth_$VERSION.zip" meta.json)"
	expected=$'\n- The section under test, positioned second.'
	[ "$(jq -r .changelog <<<"$meta")" = "$expected" ]
}

# Test F: with PACKAGE_VERSION unset, the built zip's name, meta.json's version, and the
# manifest's version all equal the version in Directory.Build.props. This is the regression
# guard that the override does not change the default.
@test "with PACKAGE_VERSION unset, every version reported is the Directory.Build.props version" {
	[ -f "$ZIP" ]
	meta="$(unzip -p "$ZIP" meta.json)"
	[ "$(jq -r .version <<<"$meta")" = "$VERSION_FROM_PROPS" ]
	[ "$(jq -r '.[0].versions[0].version' "$MANIFEST")" = "$VERSION_FROM_PROPS" ]
}

# Test G: with PACKAGE_VERSION set to a value other than Directory.Build.props's, the zip name,
# meta.json version, manifest version, and the manifest's sourceUrl path segment all equal the
# override.
@test "PACKAGE_VERSION overrides the packaged version everywhere it is reported" {
	[ -f "$OVERRIDE_ZIP" ]
	meta="$(unzip -p "$OVERRIDE_ZIP" meta.json)"
	[ "$(jq -r .version <<<"$meta")" = "$OVERRIDE_VERSION" ]
	[ "$(jq -r '.[0].versions[0].version' "$OVERRIDE_MANIFEST")" = "$OVERRIDE_VERSION" ]
	[[ "$(jq -r '.[0].versions[0].sourceUrl' "$OVERRIDE_MANIFEST")" == *"/v$OVERRIDE_VERSION/"* ]]
}

# Test AA: the repository's own CHANGELOG.md -- not the fixture, the real file at the repository
# root -- holds exactly one dated section for the version in Directory.Build.props. Deliberately
# does not route through CHANGELOG_PATH, unlike every other test in this file: this is the single
# assertion about the real file, guarding against a version bump that forgets its changelog entry.
@test "the repository's own CHANGELOG.md has exactly one dated section for the current version" {
	local count
	count="$(awk -v ver="$VERSION_FROM_PROPS" '$0 ~ "^## \\[" ver "\\] - " { count++ } END { print count + 0 }' "$REPO_ROOT/CHANGELOG.md")"
	[ "$count" -eq 1 ]
}
