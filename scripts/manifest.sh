#!/usr/bin/env bash
set -euo pipefail

# Merges one or more scripts/package.sh-shaped release directories into one checksum-verified
# Jellyfin plugin repository manifest, rebuilds that manifest from every release on GitHub, and
# verifies a published manifest against the zips it names.
#
# Usage:
#   scripts/manifest.sh merge DIR [DIR...]   Merge each DIR's manifest.json and zip into one
#                                             PackageInfo[] document on stdout.
#   scripts/manifest.sh rebuild              Enumerate every non-draft GitHub release, download
#                                             and checksum-verify each one, merge them through the
#                                             same path as merge, and write the result to
#                                             MANIFEST_OUTPUT_DIR/manifest.json.
#   scripts/manifest.sh verify URL           Fetch a published manifest and recompute the MD5 of
#                                             every version entry's sourceUrl against its recorded
#                                             checksum.
#
# Environment:
#   MANIFEST_OUTPUT_DIR   Output folder for rebuild. Default: artifacts/pages.
#   GH_REPO                owner/repo, read by gh itself.
#   GH_TOKEN                GitHub token, read by gh itself.

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly REPO_ROOT

usage() {
	echo "Usage: $0 merge DIR [DIR...] | rebuild | verify URL" >&2
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

# Both rebuild and verify stage downloads under this root. It lives in artifacts/, which is
# already gitignored, following the same reasoning scripts/package.sh's stage directory does;
# --clobber on every download makes a re-run overwrite rather than accumulate, so nothing here
# ever needs a recursive delete.
readonly SCRATCH_ROOT="$REPO_ROOT/artifacts/manifest-scratch"

# rebuild -> enumerates every non-draft GitHub release, downloads each one's manifest.json and
# zip into its own scratch directory, and folds them into one document through merge() — the
# same checksum-verifying path proven above, so rebuild never carries a second implementation of
# that check. Refuses, naming the reason, on a missing GH_REPO, a failed enumeration call, an
# empty release list, or a written entry count that does not match the enumerated release count.
rebuild() {
	local tags tag dir output_dir output_file count_written count_tags
	local dirs=()

	if [[ -z "${GH_REPO:-}" ]]; then
		echo "GH_REPO is not set; cannot enumerate releases." >&2
		return 1
	fi

	# gh release list defaults to a 30-item cap; gh api --paginate has no such cap. Never
	# replace this call with gh release list, even to simplify it — a repository past 30
	# releases would silently lose its oldest versions from the manifest (RESEARCH.md
	# Pitfall 1, D-01/D-10).
	if ! tags="$(gh api "repos/$GH_REPO/releases" --paginate --jq \
		'[.[] | select(.draft == false)] | sort_by(.tag_name) | .[].tag_name')"; then
		echo "The GitHub API call to list releases for $GH_REPO failed." >&2
		return 1
	fi

	if [[ -z "$tags" ]]; then
		echo "$GH_REPO has no releases; a manifest with no versions would advertise an empty catalog. Refusing to publish it." >&2
		return 1
	fi

	mkdir -p "$SCRATCH_ROOT"

	# Every release's manifest asset is literally named manifest.json (release.yml attaches it
	# under that exact name), so each tag gets its own directory — a shared one would leave
	# only the last tag's manifest.json on disk.
	while IFS= read -r tag; do
		dir="$SCRATCH_ROOT/$tag"
		mkdir -p "$dir"
		if ! gh release download "$tag" --dir "$dir" --pattern 'manifest.json' --clobber; then
			echo "Downloading manifest.json for release $tag failed." >&2
			return 1
		fi
		if ! gh release download "$tag" --dir "$dir" --pattern '*.zip' --clobber; then
			echo "Downloading the zip for release $tag failed." >&2
			return 1
		fi
		dirs+=("$dir")
	done <<<"$tags"

	output_dir="${MANIFEST_OUTPUT_DIR:-$REPO_ROOT/artifacts/pages}"
	mkdir -p "$output_dir"
	output_file="$output_dir/manifest.json"

	# Deliberately not `if ! merge ...; then`: an if-condition suspends errexit for the whole
	# command it tests, including every function merge() calls — so a failure inside
	# verify_checksum would print its message but let merge() run to completion and exit 0
	# anyway. Calling merge() as a plain statement keeps set -e live, so its failure aborts
	# this script immediately with merge()'s own message, exactly like the merge action above.
	merge "${dirs[@]}" >"$output_file"

	# The invariant D-01/D-10 require: the published manifest lists every release rebuild
	# found. A truncation here is an error, never a silent outcome.
	count_written="$(jq '[.[].versions[]] | length' "$output_file")"
	count_tags="${#dirs[@]}"
	if [[ "$count_written" -ne "$count_tags" ]]; then
		echo "rebuild enumerated $count_tags release(s) but wrote $count_written version entries; refusing to publish a manifest that drops a release." >&2
		rm -f "$output_file"
		return 1
	fi

	echo "$output_file"
}

# verify URL -> re-checks a published manifest against the zips its entries name: fetches URL,
# then for every version entry downloads its sourceUrl and recomputes the MD5 the same way
# merge() does, refusing on any mismatch (D-05). Refuses, distinctly, when the body does not
# parse as JSON or when it parses but holds zero version entries across all packages — the shape
# a half-finished deploy would leave, and indistinguishable from a working catalog to anyone who
# does not count.
verify() {
	local url="$1" body total checked

	mkdir -p "$SCRATCH_ROOT/verify"
	body="$SCRATCH_ROOT/verify/manifest.json"

	if ! curl -fsSL "$url" -o "$body"; then
		echo "Fetching $url failed." >&2
		return 1
	fi

	if ! jq -e . "$body" >/dev/null 2>&1; then
		echo "$url did not return a document that parses as JSON." >&2
		return 1
	fi

	total="$(jq '[.[].versions[]] | length' "$body")"
	if [[ "$total" -eq 0 ]]; then
		echo "$url holds zero version entries across all packages; refusing to treat an empty catalog as verified." >&2
		return 1
	fi

	checked=0
	while IFS=$'\t' read -r version source_url recorded; do
		local dest actual recorded_lower
		dest="$SCRATCH_ROOT/verify/$(tr '/' '_' <<<"$version").zip"
		recorded_lower="$(tr '[:upper:]' '[:lower:]' <<<"$recorded")"
		if ! curl -fsSL "$source_url" -o "$dest"; then
			echo "Fetching $source_url for version $version failed." >&2
			return 1
		fi
		actual="$(openssl dgst -md5 -r "$dest" | cut -d' ' -f1 | tr '[:upper:]' '[:lower:]')"
		if [[ "$actual" != "$recorded_lower" ]]; then
			echo "Version $version's zip at $source_url hashes to $actual but the manifest records $recorded." >&2
			return 1
		fi
		checked=$((checked + 1))
	done < <(jq -r '.[].versions[] | [.version, .sourceUrl, .checksum] | @tsv' "$body")

	echo "Verified $checked version entries against $url."
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
	rebuild)
		rebuild
		;;
	verify)
		if [[ $# -ne 2 ]]; then
			usage
			exit 2
		fi
		verify "$2"
		;;
	*)
		usage
		exit 2
		;;
	esac
}

main "$@"
