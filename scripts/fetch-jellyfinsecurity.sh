#!/usr/bin/env bash
set -euo pipefail

# Fetches JellyfinSecurity v2.6.1's Jellyfin-12 build, checksum-verified, for the shared
# end-to-end stack's second real login method (e2e/compose.yaml).
#
# v2.6.0 split the release into a .NET 9 build for Jellyfin 10.11.x and a .NET 10 build for
# 12.x under one catalog entry (https://github.com/ZL154/JellyfinSecurity/issues/172); the
# "-jf12" asset is the .NET 10 build this stack needs. The plain zip is the wrong runtime.
#
# Usage:
#   scripts/fetch-jellyfinsecurity.sh fetch   Download, verify, and unpack into artifacts/jellyfinsecurity/.
#   scripts/fetch-jellyfinsecurity.sh clean   Remove the unpacked folder. Local developer use only; no
#                                              mise task or CI job calls this action.

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly REPO_ROOT
readonly RELEASE_REPO="ZL154/JellyfinSecurity"
readonly RELEASE_TAG="v2.6.1"
readonly ASSET_NAME="Jellyfin.Plugin.TwoFactorAuthv2.6.1.0-jf12.zip"
readonly DOWNLOAD_URL="https://github.com/$RELEASE_REPO/releases/download/$RELEASE_TAG/$ASSET_NAME"

# Taken 2026-09-19 from the published sidecar at
# https://github.com/ZL154/JellyfinSecurity/releases/download/v2.6.1/Jellyfin.Plugin.TwoFactorAuthv2.6.1.0-jf12.sha256
# and pinned here as a literal. The script never reads that sidecar at run time: a digest
# committed to this repository is a pin that a later change to the release breaks loudly,
# where a sidecar fetched from the same host as the archive would only prove the two agree.
readonly PINNED_SHA256="feef7f8fea3a646b5ff8b94401f54b572489b52b37a6c260ed0e08092aaece8c"

readonly ARTIFACTS_DIR="$REPO_ROOT/artifacts/jellyfinsecurity"
readonly DIGEST_MARKER="$ARTIFACTS_DIR/.fetched-sha256"

usage() {
	echo "Usage: $0 fetch | clean" >&2
}

compute_sha256() {
	if command -v sha256sum >/dev/null 2>&1; then
		sha256sum "$1" | awk '{print $1}'
	else
		shasum -a 256 "$1" | awk '{print $1}'
	fi
}

# meta_json_dir -> prints the directory holding the unpacked plugin's meta.json, or fails.
meta_json_dir() {
	local meta_path
	meta_path="$(find "$ARTIFACTS_DIR" -maxdepth 3 -name meta.json -print -quit)"
	if [[ -z "$meta_path" ]]; then
		echo "No meta.json found under $ARTIFACTS_DIR after unpacking $ASSET_NAME." >&2
		return 1
	fi
	dirname "$meta_path"
}

fetch() {
	if [[ -f "$DIGEST_MARKER" ]] && [[ "$(cat "$DIGEST_MARKER")" == "$PINNED_SHA256" ]]; then
		local existing_dir
		existing_dir="$(meta_json_dir)"
		echo "JellyfinSecurity already fetched at the pinned digest. Plugin folder: $existing_dir"
		return 0
	fi

	mkdir -p "$ARTIFACTS_DIR"
	local archive
	archive="$(mktemp)"

	echo "Downloading $ASSET_NAME from $RELEASE_REPO@$RELEASE_TAG..." >&2
	curl -sS -L -o "$archive" "$DOWNLOAD_URL"

	local actual_sha256
	actual_sha256="$(compute_sha256 "$archive")"
	if [[ "$actual_sha256" != "$PINNED_SHA256" ]]; then
		echo "Checksum mismatch for $ASSET_NAME: expected $PINNED_SHA256, got $actual_sha256." >&2
		rm -f "$archive"
		return 1
	fi

	unzip -o -q "$archive" -d "$ARTIFACTS_DIR"
	rm -f "$archive"

	local dir name version
	dir="$(meta_json_dir)"
	name="$(jq -r '.name' "$dir/meta.json")"
	version="$(jq -r '.version' "$dir/meta.json")"
	echo "Fetched $name $version. Plugin folder: $dir"

	echo "$PINNED_SHA256" >"$DIGEST_MARKER"
}

clean() {
	if [[ -d "$ARTIFACTS_DIR" ]]; then
		trash "$ARTIFACTS_DIR"
	fi
}

main() {
	case "${1:-}" in
	fetch)
		fetch
		;;
	clean)
		clean
		;;
	*)
		usage
		exit 2
		;;
	esac
}

main "$@"
