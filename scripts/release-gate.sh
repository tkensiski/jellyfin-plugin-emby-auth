#!/usr/bin/env bash
set -euo pipefail

# Refuses a release unless the tagged commit carries exactly one passing
# ci-success check run.
#
# Usage:
#   scripts/release-gate.sh check SHA   Fail unless SHA has one completed/success ci-success check run.
#
# Environment:
#   GH_REPO    owner/repo, read by gh itself.
#   GH_TOKEN   GitHub token, read by gh itself.

usage() {
	echo "Usage: $0 check SHA" >&2
}

check() {
	local sha="$1" runs_json count status conclusion

	if [[ -z "${GH_REPO:-}" ]]; then
		echo "GH_REPO is not set; cannot query check runs for $sha." >&2
		return 1
	fi

	if ! runs_json="$(gh api "repos/$GH_REPO/commits/$sha/check-runs?check_name=ci-success")"; then
		echo "The GitHub API call to list check runs on $sha failed." >&2
		return 1
	fi

	count="$(jq '.check_runs | length' <<<"$runs_json")"
	if [[ "$count" -eq 0 ]]; then
		echo "No ci-success check run found on $sha." >&2
		return 1
	fi
	if [[ "$count" -ne 1 ]]; then
		echo "Found $count ci-success check runs on $sha; expected exactly one." >&2
		return 1
	fi

	status="$(jq -r '.check_runs[0].status' <<<"$runs_json")"
	conclusion="$(jq -r '.check_runs[0].conclusion' <<<"$runs_json")"
	if [[ "$status" != "completed" || "$conclusion" != "success" ]]; then
		echo "ci-success on $sha is $status/$conclusion, not completed/success." >&2
		return 1
	fi
}

main() {
	if [[ $# -ne 2 ]]; then
		usage
		exit 2
	fi

	case "$1" in
	check)
		check "$2"
		;;
	*)
		usage
		exit 2
		;;
	esac
}

main "$@"
