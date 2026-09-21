---
phase: 05-public-repository
plan: 04
subsystem: docs
tags: [readme, targetAbi, compatibility, jellyfin, emby]

# Dependency graph
requires:
  - phase: 05-public-repository
    provides: "the release gate, pedantic zizmor gate, and gitleaks scan (05-01..05-03) that keep README claims about the release process true"
provides:
  - "a README Compatibility section stating the measured Jellyfin/Emby versions and the targetAbi floor mechanism"
  - "two README Install sentences corrected to hold true both before and after the repository visibility switch"
affects: [05-05-visibility-switch, phase-6-docs]

# Actuals (#2632)
actuals:
  tokens: 591
  tasks: 2
  commits: 2

# Tech tracking
tech-stack:
  added: []
  patterns: []

key-files:
  created: []
  modified:
    - README.md

key-decisions:
  - "The Compatibility section states only the measured Jellyfin 12.1.0 / Emby 4.10.0.40 combination and frames targetAbi as a floor with no ceiling, never an endorsement of untested newer versions"
  - "The two Install sentences D-19 named are rewritten as properties of the release files and the mechanism, not as claims about this repository's current visibility, so neither becomes false when plan 05-05 flips the switch"

patterns-established: []

requirements-completed: [DOCS-03]

coverage:
  - id: D1
    description: "README gains a ## Compatibility section between Requirements and Install, naming the tested Jellyfin/Emby versions and defining targetAbi as a minimum with no upper bound"
    requirement: "DOCS-03"
    verification:
      - kind: other
        ref: "rg checks: '^## Compatibility$' heading present and ordered between Requirements/Install; '12\\.1\\.0\\.0' and '4\\.10\\.0\\.40' present; 'minimum' present; no 'supports Jellyfin 1[3-9]'-style claim; Status line unchanged; no nested ### heading"
        status: pass
    human_judgment: true
    rationale: "Task 1's <verify> includes a <human-check> asking whether a reader who has never heard of targetAbi can state what it controls, what happens below/above 12.1.0.0, and that a newer version is untested rather than supported — a judgment call the automated rg checks cannot make on their own"
  - id: D2
    description: "The Install section's granted-access clause and the manifest paragraph's private-repository conditional are corrected to sentences true in both the pre- and post-visibility-switch states"
    requirement: "DOCS-03"
    verification:
      - kind: other
        ref: "rg checks: 'repository is private', 'you need access to it', and 'only when the release files are public' all absent; 'without GitHub credentials' and 'Not published to a plugin repository' still present; git diff scoped to ## Install and the Task 1 ## Compatibility block only"
        status: pass
    human_judgment: true
    rationale: "Task 2's <verify> includes a <human-check> asking whether the Install section reads as true both imagining the repository private and imagining it public — a dual-state truth judgment no single automated check proves"

duration: 8min
completed: 2026-09-21
status: complete
---

# Phase 5 Plan 4: README Compatibility Section Summary

**Added a `## Compatibility` section explaining `targetAbi` as a floor with no ceiling, and corrected the two Install sentences the coming visibility switch would otherwise falsify.**

## Performance

- **Duration:** 8 min
- **Completed:** 2026-09-21T05:04:09Z
- **Tasks:** 2
- **Files modified:** 1

## Accomplishments

- `README.md` now has a `## Compatibility` section between `## Requirements` and `## Install` that names the tested combination (Jellyfin 12.1.0, Emby 4.10.0.40, local containers only), defines `targetAbi` in plain words for a reader who has never seen the term, states its value for this build (`12.1.0.0`), and explains that Jellyfin treats it as a minimum with no upper bound — so a newer, untested Jellyfin release will still install the plugin, which the section explicitly does not call "supported."
- The `## Install` section's opening bullet no longer claims the repository is private or that a reader needs granted access; it now simply names a GitHub release as a source.
- The manifest paragraph in `## Install` now states the no-GitHub-credentials requirement as a property of the release files themselves ("a manifest URL only works for release files that anyone can download without signing in to GitHub") rather than as a claim about this repository's current visibility, so the sentence stays true through plan 05-05's switch.

## Task Commits

Each task was committed atomically:

1. **Task 1: A Compatibility section that explains targetAbi to someone who has never seen it** - `40691d2` (docs)
2. **Task 2: Correct the two sentences that the visibility change falsifies** - `157614e` (docs)

**Plan metadata:** committed alongside this SUMMARY

## Files Created/Modified

- `README.md` - new `## Compatibility` section (Task 1); two corrected sentences in `## Install` (Task 2)

## Decisions Made

- The Compatibility section cites both version strings in their own forms — `12.1.0` for the tested Jellyfin version, `12.1.0.0` for the four-part `targetAbi` value that `scripts/package.sh:33-38`'s `target_abi()` derives from it — and never presents them as interchangeable, per D-18.
- Both corrected Install sentences (D-19) were checked against two readings — repository still private, repository already public — and hold true in each: naming a GitHub release as a source claims nothing about who may download it, and the manifest paragraph's "anyone can download without signing in to GitHub" phrasing describes a property of the files rather than the repository's current setting.

## Deviations from Plan

None - plan executed exactly as written.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- `README.md` carries no claim that will become false when plan 05-05 flips repository visibility, clearing the path for that plan.
- Phase 6's DOCS-02 (rewriting `## Install` around the manifest URL) starts from a clean, already-accurate paragraph rather than one still carrying stale private-repository language.
- `mise run lint` passes (gitleaks git: 211 commits scanned, no leaks found; zizmor --persona=pedantic: no findings), confirming this plan's README-only change did not disturb the gates 05-01–05-03 built.

## Self-Check: PASSED

- `README.md` exists on disk.
- Commits `40691d2` and `157614e` both found in `git log --oneline --all`.
- `rg -n '^## '` confirms heading order `Requirements, Compatibility, Install, Configure, Migrate users, Documentation, License, Contact`.
- `12.1.0.0` and `4.10.0.40` both present; `repository is private` absent.
- `mise run lint` exits 0 (gitleaks: 211 commits scanned, no leaks; zizmor --persona=pedantic: no findings).

---
*Phase: 05-public-repository*
*Completed: 2026-09-21*
