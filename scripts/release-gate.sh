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
	local sha="$1" runs_json raw_count count status conclusion
	local default_branch compare_status

	if [[ -z "${GH_REPO:-}" ]]; then
		echo "GH_REPO is not set; cannot query check runs for $sha." >&2
		return 1
	fi

	if ! runs_json="$(gh api "repos/$GH_REPO/commits/$sha/check-runs?check_name=ci-success")"; then
		echo "The GitHub API call to list check runs on $sha failed." >&2
		return 1
	fi

	raw_count="$(jq '.check_runs | length' <<<"$runs_json")"
	if [[ "$raw_count" -eq 0 ]]; then
		echo "No ci-success check run found on $sha." >&2
		return 1
	fi

	# GitHub ignores query parameters it does not recognise, so the
	# `?check_name=ci-success` filter above is not trusted alone: filter
	# again here by the run's own name (WR-01), and name what was actually
	# found when nothing matches.
	count="$(jq '[.check_runs[] | select(.name == "ci-success")] | length' <<<"$runs_json")"
	if [[ "$count" -eq 0 ]]; then
		local names
		names="$(jq -r '[.check_runs[].name] | join(", ")' <<<"$runs_json")"
		echo "No check run named ci-success found on $sha; found: $names." >&2
		return 1
	fi
	if [[ "$count" -ne 1 ]]; then
		echo "Found $count ci-success check runs on $sha; expected exactly one." >&2
		return 1
	fi

	status="$(jq -r '[.check_runs[] | select(.name == "ci-success")][0].status' <<<"$runs_json")"
	conclusion="$(jq -r '[.check_runs[] | select(.name == "ci-success")][0].conclusion' <<<"$runs_json")"
	if [[ "$status" != "completed" || "$conclusion" != "success" ]]; then
		echo "ci-success on $sha is $status/$conclusion, not completed/success." >&2
		return 1
	fi

	# WR-02: a passing ci-success check run also appears on an open PR's
	# feature-branch head, because ci.yml triggers on pull_request. Require
	# the commit to actually be on the default branch before trusting it.
	if ! default_branch="$(gh api "repos/$GH_REPO" --jq .default_branch)"; then
		echo "The GitHub API call to read the default branch of $GH_REPO failed." >&2
		return 1
	fi
	if [[ -z "$default_branch" ]]; then
		echo "The default branch lookup for $GH_REPO returned nothing." >&2
		return 1
	fi

	if ! compare_status="$(gh api "repos/$GH_REPO/compare/$default_branch...$sha" --jq .status)"; then
		echo "The GitHub API call to compare $sha against $default_branch failed." >&2
		return 1
	fi
	case "$compare_status" in
	identical | behind) ;;
	*)
		echo "$sha is $compare_status relative to $default_branch, not identical or behind; a passing ci-success check run can come from the pull_request event on an unmerged feature-branch head, so it does not prove the commit is on $default_branch. Refusing." >&2
		return 1
		;;
	esac
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
