#!/usr/bin/env bash
set -euo pipefail

# Merges one or more scripts/package.sh-shaped release directories into one checksum-verified
# Jellyfin plugin repository manifest.
#
# Usage:
#   scripts/manifest.sh merge DIR [DIR...]   Merge each DIR's manifest.json and zip into one
#                                             PackageInfo[] document on stdout.
#
# Environment:
#   (none yet — merge reads only the directories given on the command line.)

usage() {
	echo "Usage: $0 merge DIR [DIR...]" >&2
}

# validate_release_dir DIR -> prints DIR's zip path on success. Refuses, naming DIR, when
# manifest.json is absent, when the directory does not hold exactly one zip file, or when the
# manifest is not a single package with a single version. Each source directory is expected to
# hold exactly one package with exactly one version, because that is what scripts/package.sh
# build writes; a repository shipping more than one plugin would need this grouping widened
# rather than the assumption relaxed.
validate_release_dir() {
	local dir="$1" manifest="$1/manifest.json" zip_count zip_path

	if [[ ! -f "$manifest" ]]; then
		echo "$dir has no manifest.json." >&2
		return 1
	fi

	zip_count="$(find "$dir" -maxdepth 1 -name '*.zip' | wc -l | tr -d ' ')"
	if [[ "$zip_count" -ne 1 ]]; then
		echo "$dir does not hold exactly one zip file; found $zip_count." >&2
		return 1
	fi
	zip_path="$(find "$dir" -maxdepth 1 -name '*.zip' -print -quit)"

	if ! jq -e '
		type == "array" and length == 1
		and (.[0].versions | type == "array") and (.[0].versions | length == 1)
	' "$manifest" >/dev/null; then
		echo "$dir's manifest.json is not exactly one package with exactly one version." >&2
		return 1
	fi

	printf '%s' "$zip_path"
}

# verify_checksum DIR ZIP -> refuses, naming DIR and both hashes, when the recomputed MD5 of ZIP
# does not match the checksum recorded in DIR/manifest.json. Both hashes are lower-cased before
# comparing, matching Jellyfin's own case-insensitive comparison.
verify_checksum() {
	local dir="$1" zip="$2" manifest="$1/manifest.json" recorded actual

	recorded="$(jq -n --slurpfile doc "$manifest" -r '$doc[0][0].versions[0].checksum' | tr '[:upper:]' '[:lower:]')"
	actual="$(openssl dgst -md5 -r "$zip" | cut -d' ' -f1 | tr '[:upper:]' '[:lower:]')"
	if [[ "$actual" != "$recorded" ]]; then
		echo "$dir's zip hashes to $actual but its manifest.json records $recorded." >&2
		return 1
	fi
}

merge() {
	local dir zip manifest_paths=() combined duplicates

	for dir in "$@"; do
		zip="$(validate_release_dir "$dir")"
		verify_checksum "$dir" "$zip"
		manifest_paths+=("$dir/manifest.json")
	done

	# Group by guid, sort each group ascending by the numeric components of its one version, take
	# the highest version's package-level fields as the base, and replace its versions with the
	# sorted list's own version objects, collected in the same order.
	combined="$(jq -s '
		map(.[0])
		| group_by(.guid)
		| map(
			(sort_by(.versions[0].version | split(".") | map(tonumber))) as $sorted
			| ($sorted | last) as $base
			| $base
			| .versions = ($sorted | map(.versions[0]))
		)
		| sort_by(.guid)
	' "${manifest_paths[@]}")"

	duplicates="$(jq -r '
		[.[] | .versions | group_by(.version) | map(select(length > 1)) | .[] | .[0].version]
		| unique | join(", ")
	' <<<"$combined")"
	if [[ -n "$duplicates" ]]; then
		echo "Two source directories declare the same version: $duplicates." >&2
		return 1
	fi

	printf '%s\n' "$combined"
}

main() {
	case "${1:-}" in
	merge)
		if [[ $# -lt 2 ]]; then
			usage
			exit 2
		fi
		shift
		merge "$@"
		;;
	*)
		usage
		exit 2
		;;
	esac
}

main "$@"
