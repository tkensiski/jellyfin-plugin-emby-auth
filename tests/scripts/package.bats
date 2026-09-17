#!/usr/bin/env bats
# Tests for scripts/package.sh. One build in setup_file, then checks on the output.

setup_file() {
	REPO_ROOT="$(cd "$BATS_TEST_DIRNAME/../.." && pwd)"
	PACKAGE_OUTPUT_DIR="$BATS_FILE_TMPDIR/release"
	export REPO_ROOT PACKAGE_OUTPUT_DIR
	export RELEASE_TIMESTAMP="2026-09-17T12:00:00Z"
	export RELEASE_URL_BASE="https://example.test/releases/download"

	"$REPO_ROOT/scripts/package.sh" build >"$BATS_FILE_TMPDIR/build.out"
}

setup() {
	VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$REPO_ROOT/Directory.Build.props" | head -1)"
	ZIP="$PACKAGE_OUTPUT_DIR/jellyfin-plugin-emby-auth_$VERSION.zip"
	MANIFEST="$PACKAGE_OUTPUT_DIR/manifest.json"
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
