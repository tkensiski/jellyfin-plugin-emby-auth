#!/usr/bin/env bash
set -euo pipefail

# Reports the evidence a human needs before the repository can be made
# public: the mise run lint secret-scan verdict, the repository's current
# visibility, and the workflow run, artifact, issue, pull request, and
# release surface, each printed for review. Every `gh` call here is a
# read — `gh api` for the enumerations, `gh repo view` for the visibility
# line — and nothing in this script can change the repository's visibility.
#
# Usage:
#   scripts/pre-public-audit.sh run   Print the audit report.
#
# Environment:
#   GH_REPO    owner/repo, used by gh's own commands.
#   GH_TOKEN   GitHub token, used by gh's own commands.

usage() {
	echo "Usage: $0 run" >&2
}

secret_scan() {
	# Reuses mise run lint's own history scan; this script defines no
	# separate scanner, so the pre-commit hook, CI, and this audit can
	# never disagree.
	if mise run lint >/dev/null 2>&1; then
		echo "PASS secret-scan: mise run lint reported no findings"
		return 0
	fi
	echo "FAIL secret-scan: mise run lint reported a finding; run 'mise run lint' yourself to see it" >&2
	return 1
}

visibility() {
	local json vis
	json="$(gh repo view --json visibility)"
	vis="$(jq -r '.visibility' <<<"$json")"
	echo "REVIEW visibility: $vis"
}

workflow_runs() {
	local json count
	json="$(gh api --paginate --slurp "repos/$GH_REPO/actions/runs")"
	count="$(jq '[.[].workflow_runs[]] | length' <<<"$json")"
	echo "REVIEW workflow-runs: $count runs"
	if [[ "$count" -gt 0 ]]; then
		jq -r '.[].workflow_runs[] | "  id=\(.id) workflow=\(.name) conclusion=\(.conclusion)"' <<<"$json"
	fi
}

artifacts() {
	local json count
	json="$(gh api --paginate --slurp "repos/$GH_REPO/actions/artifacts")"
	count="$(jq '[.[].artifacts[]] | length' <<<"$json")"
	echo "REVIEW artifacts: $count artifacts"
	if [[ "$count" -gt 0 ]]; then
		jq -r '.[].artifacts[] | "  \(.name)"' <<<"$json"
	fi
}

issues() {
	local json count
	json="$(gh api --paginate --slurp "repos/$GH_REPO/issues?state=all")"
	# This endpoint also returns pull requests; drop anything carrying a
	# pull_request key before counting or listing.
	count="$(jq '[.[][] | select(has("pull_request") | not)] | length' <<<"$json")"
	echo "REVIEW issues: $count issues"
	if [[ "$count" -gt 0 ]]; then
		jq -r '.[][] | select(has("pull_request") | not) | "  #\(.number) \(.title)"' <<<"$json"
	fi
}

pull_requests() {
	local json count
	json="$(gh api --paginate --slurp "repos/$GH_REPO/pulls?state=all")"
	count="$(jq '[.[][]] | length' <<<"$json")"
	echo "REVIEW pull-requests: $count pull requests"
	if [[ "$count" -gt 0 ]]; then
		jq -r '.[][] | "  #\(.number) \(.title)"' <<<"$json"
	fi
}

releases() {
	local json count
	json="$(gh api --paginate --slurp "repos/$GH_REPO/releases")"
	count="$(jq '[.[][]] | length' <<<"$json")"
	echo "REVIEW releases: $count releases"
	if [[ "$count" -gt 0 ]]; then
		jq -r '.[][] | "  \(.tag_name) \(.name)"' <<<"$json"
	fi
}

run() {
	local secret_status=0

	if [[ -z "${GH_REPO:-}" ]]; then
		echo "GH_REPO is not set; cannot query the GitHub API." >&2
		return 1
	fi

	if ! secret_scan; then
		secret_status=1
	fi

	visibility
	workflow_runs
	artifacts
	issues
	pull_requests
	releases

	if [[ "$secret_status" -eq 0 ]]; then
		echo "Summary: PASS overall — only the secret-scan item is machine-checked; read every REVIEW line above before deciding."
	else
		echo "Summary: FAIL overall — the secret-scan item failed; fix it before reading the REVIEW lines above."
	fi

	return "$secret_status"
}

main() {
	if [[ $# -ne 1 ]]; then
		usage
		exit 2
	fi

	case "$1" in
	run)
		run
		;;
	*)
		usage
		exit 2
		;;
	esac
}

main "$@"
