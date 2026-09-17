#!/usr/bin/env bash
set -euo pipefail

# Builds the plugin release zip and a Jellyfin plugin repository manifest.
#
# Usage:
#   scripts/package.sh build           Build artifacts/release/jellyfin-plugin-emby-auth_<version>.zip and manifest.json.
#   scripts/package.sh check-tag TAG   Fail unless TAG is v<version>.
#
# Environment:
#   PACKAGE_OUTPUT_DIR  Output folder. Default: artifacts/release.
#   RELEASE_URL_BASE    Base of the zip download URL in the manifest. Default: this repository's GitHub releases.
#   RELEASE_TIMESTAMP   UTC timestamp in the metadata. Default: now.

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly REPO_ROOT
readonly PROJECT="$REPO_ROOT/src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj"
readonly ASSEMBLY="Jellyfin.Plugin.EmbyAuth.dll"
readonly PLUGIN_GUID="e973e09a-e8b4-40c1-9be2-8e51342de1f9"
readonly PLUGIN_NAME="Emby Auth"
readonly OUTPUT_DIR="${PACKAGE_OUTPUT_DIR:-$REPO_ROOT/artifacts/release}"
readonly STAGE_DIR="$REPO_ROOT/artifacts/package-stage"
readonly URL_BASE="${RELEASE_URL_BASE:-https://github.com/tkensiski/jellyfin-plugin-emby-auth/releases/download}"

usage() {
	echo "Usage: $0 build | check-tag TAG" >&2
}

plugin_version() {
	sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$REPO_ROOT/Directory.Build.props" | head -1
}

# The target ABI is the Jellyfin.Controller version that the plugin is built against, with a fourth part.
target_abi() {
	local controller
	controller="$(sed -n 's:.*Include="Jellyfin.Controller" Version="\([^"]*\)".*:\1:p' "$PROJECT" | head -1)"
	echo "$controller.0"
}

check_tag() {
	local tag="$1" version
	version="$(plugin_version)"
	if [[ "$tag" != "v$version" ]]; then
		echo "The tag $tag does not match the plugin version $version in Directory.Build.props. Tag the release v$version." >&2
		return 1
	fi
}

build() {
	local version abi timestamp zip_name checksum
	version="$(plugin_version)"
	abi="$(target_abi)"
	timestamp="${RELEASE_TIMESTAMP:-$(date -u +%Y-%m-%dT%H:%M:%SZ)}"
	zip_name="jellyfin-plugin-emby-auth_$version.zip"

	dotnet publish "$PROJECT" -c Release -o "$STAGE_DIR/publish" >&2

	mkdir -p "$STAGE_DIR/plugin" "$OUTPUT_DIR"
	cp "$STAGE_DIR/publish/$ASSEMBLY" "$STAGE_DIR/plugin/$ASSEMBLY"
	jq -n \
		--arg guid "$PLUGIN_GUID" --arg name "$PLUGIN_NAME" --arg version "$version" \
		--arg abi "$abi" --arg timestamp "$timestamp" --arg assembly "$ASSEMBLY" \
		'{
			category: "Authentication",
			changelog: ("Release " + $version),
			description: "Checks Jellyfin logins against an Emby server, saves the password in Jellyfin, and moves each user to the Default login method.",
			guid: $guid,
			name: $name,
			overview: "Move users from Emby to Jellyfin without a password reset.",
			owner: "tkensiski",
			targetAbi: $abi,
			timestamp: $timestamp,
			version: $version,
			status: "Active",
			autoUpdate: false,
			assemblies: [$assembly]
		}' >"$STAGE_DIR/plugin/meta.json"

	# Build the zip in the stage folder, then move it, so a leftover zip never keeps old entries.
	(cd "$STAGE_DIR/plugin" && zip -q -X "$STAGE_DIR/$zip_name.new" "$ASSEMBLY" meta.json)
	mv -f "$STAGE_DIR/$zip_name.new" "$OUTPUT_DIR/$zip_name"

	checksum="$(openssl dgst -md5 -r "$OUTPUT_DIR/$zip_name" | cut -d' ' -f1)"
	jq -n \
		--slurpfile meta "$STAGE_DIR/plugin/meta.json" \
		--arg checksum "$checksum" --arg url "$URL_BASE/v$version/$zip_name" \
		'[{
			guid: $meta[0].guid,
			name: $meta[0].name,
			description: $meta[0].description,
			overview: $meta[0].overview,
			owner: $meta[0].owner,
			category: $meta[0].category,
			versions: [{
				version: $meta[0].version,
				changelog: $meta[0].changelog,
				targetAbi: $meta[0].targetAbi,
				sourceUrl: $url,
				checksum: $checksum,
				timestamp: $meta[0].timestamp
			}]
		}]' >"$OUTPUT_DIR/manifest.json"

	echo "$OUTPUT_DIR/$zip_name"
}

main() {
	case "${1:-}" in
	build)
		build
		;;
	check-tag)
		if [[ $# -ne 2 ]]; then
			usage
			exit 2
		fi
		check_tag "$2"
		;;
	*)
		usage
		exit 2
		;;
	esac
}

main "$@"
