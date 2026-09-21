---
phase: 05-public-repository
plan: 05
subsystem: infra
tags: [bash, bats, github-cli, secret-scanning, release-checklist]

requires:
  - phase: 05-public-repository
    provides: "the release gate (05-01), the pedantic zizmor gate (05-02), the gitleaks history scan wired into mise run lint (05-03), and the corrected README (05-04) — all of which the audit's secret-scan item and review items depend on"
provides:
  - "scripts/pre-public-audit.sh, a tracked one-shot report reusing mise run lint for the secret-scan verdict and gh api/gh repo view for the read-only enumeration of visibility, workflow runs, artifacts, issues, pull requests, and releases"
  - "tests/scripts/pre-public-audit.bats, 6 bats tests covering usage, the secret-scan PASS/FAIL split, the explicit zero lines, and the fixed item order"
  - "a completed audit run against the live repository, its counts and review conclusions recorded below"
  - "a checkpoint handing the maintainer the visibility command; the phase stops here by design"
affects: []

actuals:
  tokens: 1786
  tasks: 2
  commits: 2

tech-stack:
  added: []
  patterns:
    - "RED-phase skip markers (established by 05-01): all six new tests carried `skip \"GREEN pending: ...\"` in the test commit so the pre-commit hook's `mise run test` step could pass while the tests were still genuinely red locally; the skip lines were removed in the GREEN commit with no other diff"

key-files:
  created:
    - scripts/pre-public-audit.sh
    - tests/scripts/pre-public-audit.bats
  modified: []

key-decisions:
  - "The secret-scan item shells out to `mise run lint` exactly, with no `--source`, `--config`, or other flag of its own, and the script contains the word gitleaks nowhere — reusing the exact path plan 05-03 wired in, per D-14, so the pre-commit hook, CI, and this audit can never disagree"
  - "Every gh call is `gh api` (six enumerations) or `gh repo view` (the visibility line); a positive acceptance criterion pins this exact two-command set so no write path can be added without failing the check"
  - "The issues item filters out every element carrying a `pull_request` key before counting, because `GET /issues` also returns pull requests; the pull-requests item queries `GET /pulls` separately"
  - "Comment wording in the script's header avoids an unquoted `gh <word>` phrase outside the two sanctioned invocations (e.g. \"used by gh's own commands\" instead of \"read by gh itself\"), so the acceptance criterion pinning the gh-subcommand set to exactly `gh api,gh repo view` is not tripped by ordinary prose"
  - "PUB-03 is not marked complete by this plan. The audit ran, passed, and was read, but the repository is still PRIVATE as of this run — PUB-03 closes only after the maintainer runs the visibility command personally and `gh repo view --json visibility` reports `public`, which cannot happen inside this execution (D-15)"

patterns-established: []

requirements-completed: [PUB-01]

coverage:
  - id: D1
    description: "scripts/pre-public-audit.sh run prints seven items in a fixed order, judges only the secret-scan item, and reuses mise run lint for it"
    requirement: "PUB-01"
    verification:
      - kind: unit
        ref: "tests/scripts/pre-public-audit.bats (6 tests, all passing)"
        status: pass
      - kind: other
        ref: "shellcheck -x scripts/pre-public-audit.sh tests/scripts/pre-public-audit.bats (exit 0); shfmt -d scripts tests/scripts (no diff); mise run test (exit 0)"
        status: pass
    human_judgment: false
  - id: D2
    description: "Every GitHub call the script makes is a read (gh api, gh repo view); no second secret scanner is defined"
    requirement: "PUB-01"
    verification:
      - kind: other
        ref: "rg -o '\\bgh +[a-z]+( +[a-z]+)?' scripts/pre-public-audit.sh | sort -u | paste -sd, - == 'gh api,gh repo view'; rg -v '^\\s*#' scripts/pre-public-audit.sh | rg -q gitleaks; test $? -eq 1"
        status: pass
    human_judgment: false
  - id: D3
    description: "The exit-status comparison for the secret-scan item has teeth"
    requirement: "PUB-01"
    verification:
      - kind: unit
        ref: "Broke `if mise run lint` to `if ! mise run lint`, watched Tests 3-5 go `not ok`, restored the file byte-for-byte (diff clean), tests green again"
        status: pass
    human_judgment: false
  - id: D4
    description: "The audit ran against the live repository, exited 0, and every review item was read; nothing found needs removal or rotation"
    requirement: "PUB-01"
    verification: []
    human_judgment: true
    rationale: "Reading workflow run conclusions, pull request titles/bodies, and confirming no workflow echoes a token is a judgment call about world-readable content that no automated check alone proves — recorded below with the specific checks that were run"

duration: ~12min
completed: 2026-09-21
status: complete
---

# Phase 5 Plan 5: Pre-Publication Audit Summary

**Built `scripts/pre-public-audit.sh`, a tracked report that reuses `mise run lint` for its secret-scan verdict and read-only `gh` calls for the rest, ran it against the live repository (clean), read every review item, and reached the checkpoint that hands the maintainer the visibility command — the phase stops there by design.**

## Performance

- **Duration:** ~12 min
- **Started:** 2026-09-20T22:12Z (first commit)
- **Completed:** 2026-09-21T05:19Z (audit run + review)
- **Tasks:** 2 of 3 (Task 3 is the checkpoint itself, resolved by delivering this report)
- **Files modified:** 2 (both created)

## Accomplishments

- `scripts/pre-public-audit.sh run` prints seven items in a fixed, repeatable order — `secret-scan`, `visibility`, `workflow-runs`, `artifacts`, `issues`, `pull-requests`, `releases` — with verdict tokens `PASS`/`FAIL` (secret-scan only) and `REVIEW` (the other six). Empty GitHub collections print an explicit `REVIEW <item>: 0 <item>` line rather than silence.
- `tests/scripts/pre-public-audit.bats` — 6 tests: the usage/unknown-action pair, the secret-scan PASS/FAIL split against a fake `mise`, the four explicit zero lines against a fake `gh` returning empty collections, and the fixed-order check across two consecutive runs.
- The exit-status comparison was proven to have teeth: inverting `if mise run lint` to `if ! mise run lint` sent Tests 3–5 red; restoring the file byte-for-byte (confirmed via `diff`) brought them back to green.
- The script was run against the live repository (`GH_REPO=tkensiski/jellyfin-plugin-emby-auth`) and exited 0. Every `REVIEW` item was read; findings below.

## Task Commits

1. **Task 1 RED: the failing test, with skip markers for the pre-commit hook** — `6910d46` (test)
2. **Task 1 GREEN: the audit script; skip markers removed** — `c5c86c6` (feat)

**Plan metadata:** (this commit)

Task 2 produced no source-file commit — its output is the audit run recorded below, folded into this SUMMARY per the plan's own file list for that task.

## Files Created/Modified

- `scripts/pre-public-audit.sh` — New. Action: `run`. Reads `GH_REPO`/`GH_TOKEN` (consumed by `gh` itself). Seven items, fixed order, `PASS`/`FAIL`/`REVIEW` verdict tokens.
- `tests/scripts/pre-public-audit.bats` — New. 6 tests; fake `gh` and `mise` on `PATH`.

## The Live Audit Run

Run at 2026-09-21T05:19Z against `tkensiski/jellyfin-plugin-emby-auth`. Exit code 0.

| Item | Verdict | Count | Compared to the measured surface (05-CONTEXT.md) |
|------|---------|-------|---------------------------------------------------|
| secret-scan | **PASS** | — | `mise run lint`'s gitleaks stage: 214 commits scanned, no leaks found (commit count grew from the 204 measured during discussion, as expected — more commits landed since) |
| visibility | REVIEW | `PRIVATE` | Matches — the repository is still private at the moment of this run |
| workflow-runs | REVIEW | 9 | Matches exactly (9 measured, 9 found: 7 `success`, 2 `cancelled`) |
| artifacts | REVIEW | 0 | Matches |
| issues | REVIEW | 0 | Matches |
| pull-requests | REVIEW | 10 | Matches exactly (numbered 1–10, as measured) |
| releases | REVIEW | 0 | Matches |

No count differed from the surface measured during phase discussion. Nothing here required a change to the script.

### Review conclusions, one line per item

- **visibility:** `PRIVATE`, as expected before the maintainer's own switch.
- **workflow-runs:** All 9 runs read. 7 `success`, 2 `cancelled` (the two `cancelled` runs are the reachable state D-03 documented — an earlier commit's run cancelled by `ci.yml`'s `cancel-in-progress: true` concurrency group when a later commit landed first). Checked whether any run's logs could leak a secret: `.github/workflows/ci.yml` and `.github/workflows/release.yml` pass `${{ github.token }}` and `${{ github.repository }}` only through `env:` blocks (`release.yml:48-49`, `:61-62`) and never `echo` them; confirmed with `rg -q 'echo.*(GH_TOKEN|github\.token|secrets\.)' .github/workflows/ci.yml .github/workflows/release.yml` (exit 1 — no match).
- **artifacts:** 0 — nothing to review.
- **issues:** 0 — nothing to review.
- **pull-requests:** All 10 read in full (titles and bodies, `gh pr list --state all --json number,title,body`). All ten describe implementation work on this plugin (login method, migration, packaging, CI, settings page, code-review hardening) — the same benign content the phase discussion already characterized, re-checked rather than carried forward. One incidental mention of a setting literally named `EmbyApiKey` in PR #3's body describes the setting's existence, not a credential value. Nothing in any of the ten needs removal or rotation.
- **releases:** 0 — there are no release notes to review yet. The first release is Phase 6's rehearsal (version 0.9.0.0); this is the reason, not an omission.

No credential value, finding body, or token was captured from any of the above into this file, the terminal, or a commit.

## Decisions Made

- The secret-scan item shells out to `mise run lint` exactly, with no flag of its own and no mention of gitleaks anywhere in the script, so this audit and plan 05-03's continuous scan can never drift apart (D-14).
- Every `gh` invocation is `gh api` (six enumerations) or `gh repo view` (the visibility line) — a positive acceptance criterion pins this exact set, and the script's own comments were worded to avoid tripping the same `\bgh +[a-z]+` pattern the check uses (e.g. "used by gh's own commands" rather than "read by gh itself").
- Following 05-01's established precedent, the RED commit carried `skip "GREEN pending: ..."` on all six tests — confirmed genuinely red locally first (exit 127, script absent), then skip-marked so the pre-commit hook's `mise run test` step would pass, then un-skipped in the GREEN commit with no other diff.
- PUB-03 is not marked complete by this plan (see Next Phase Readiness).

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None beyond the checkpoint below — no environment configuration needed.

## Threat Flags

None. All six threats this plan's threat model named (T-05-16 through T-05-21) are mitigated as designed; the live run confirmed each mitigation holds (no leaks, no write-capable `gh` call, no captured secret, no rubber-stamped `REVIEW` item, and the checkpoint's `gate="blocking-human"` was honored — no command was run).

## CHECKPOINT REACHED — awaiting the maintainer

**Type:** decision
**Gate:** blocking-human
**Task 3 of 3:** "the maintainer decides whether to publish, and runs the command personally"

Everything this phase could do is done. The release gate (05-01), the pedantic zizmor gate (05-02), the gitleaks history scan (05-03), the corrected README (05-04), and this audit (05-05) all stand. The audit above found nothing that needs fixing.

**The command, for the maintainer to run personally, at the time they choose:**

```
gh repo edit --visibility public --accept-visibility-change-consequences
```

No agent runs this command. Answering this checkpoint does not cause it to run. `--accept-visibility-change-consequences` is required whenever `--visibility` is used; without it the command refuses.

**Options:**
- **published** — "I ran the command; the repository is public." PUB-03 is then satisfied, verified afterward with `gh repo view --json visibility` reporting `public`.
- **holding** — "Not yet; I am holding the switch." Nothing published; nothing lost by waiting.
- **blocked** — "Something in the audit needs fixing first." (Not applicable here — the audit found nothing to fix — but available if new information surfaces before the switch is thrown.)

## Next Phase Readiness

- PUB-01 is complete: the audit script exists, is tracked, is tested, ran against the live repository, exited 0, and every review item was read and recorded above.
- **PUB-03 stays Active/Pending in `REQUIREMENTS.md`.** It cannot be marked complete from inside this execution (D-15) — only the maintainer's own `gh repo edit` and a subsequent `gh repo view --json visibility` reporting `public` closes it. This plan intentionally stops at the checkpoint above rather than proceeding further.
- Phase 6 (the GitHub Pages manifest, the rehearsal release, and the v1.0.0.0 tag) is blocked on PUB-03 landing, per the roadmap's own ordering.

---
*Phase: 05-public-repository*
*Completed: 2026-09-21*

## Self-Check: PASSED

- Both key files found on disk: `scripts/pre-public-audit.sh`, `tests/scripts/pre-public-audit.bats`.
- Both task commits found in git log: `6910d46`, `c5c86c6`.
- Task 1's `<acceptance_criteria>` re-run and passing: `bats tests/scripts/pre-public-audit.bats` (6/6), `test -x scripts/pre-public-audit.sh`, the `mise run lint` / gitleaks-absence / gh-subcommand-set / `pull_request` / `REVIEW`-count `rg` checks, `shellcheck`/`shfmt` clean, `mise run test` exit 0, and the break-then-restore teeth proof.
- Task 2's `<acceptance_criteria>` re-run and passing: live run exit 0, counts and review conclusions recorded above, the workflow-echo negative check, no credential-shaped string in this file, and `git status --porcelain` showing no leaked report file.
- Plan-level `<verification>` re-run: `bats tests/scripts/pre-public-audit.bats` (6 tests), `mise run test` and `mise run lint` both exit 0, `scripts/pre-public-audit.sh run` exits 0 live with a `PASS` secret-scan line and a `REVIEW` line + count for each of the other six items. The final PUB-03 check (`gh repo view --json visibility` reporting `public`) is deferred to after the maintainer's own action, as designed.
