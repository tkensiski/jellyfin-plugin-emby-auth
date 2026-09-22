---
phase: 06-catalog-install-and-first-public-release
plan: 03
subsystem: docs
tags: [readme, claude-md, catalog-install, version-bump-rule]

# Dependency graph
requires:
  - phase: 06-01
    provides: "e2e/85-catalog-install.bats and the plugin folder naming/casing facts this plan's README steps describe"
  - phase: 06-02
    provides: ".github/workflows/pages.yml, the workflow that publishes to the manifest URL this plan documents"
provides:
  - "README.md: a catalog-first Install section with the canonical manifest URL, an Update subsection, and the manual zip route kept as the named alternative for a server that cannot reach GitHub Pages"
  - "CLAUDE.md: a Jellyfin version bump rule naming all six pins a bump touches, including the two previously-missing entries"
affects: [06-04]

# Actuals (#2632)
actuals:
  tokens: 1312
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
    - CLAUDE.md

key-decisions:
  - "The README's Update subsection never repeats the 'Add Repository' instruction verbatim — it says the add step is not repeated, and describes the two-entries symptom if it is — so the add-step wording stays confined to the install procedure, per the plan's own must_haves truth"
  - "Kept the CLAUDE.md bump bullet as one line, matching the task's own explicit action text ('Write the bullet as one line. Do not hard-wrap.') and every other bullet in the file's convention, even though one of the plan's own automated acceptance checks cannot pass against a single-line bullet — see Deviations"

requirements-completed: [DOCS-02, DOCS-04]

coverage:
  - id: D1
    description: "README.md leads Install with the catalog route (Repositories > Add Repository, the canonical manifest URL, Catalog tab install, restart), adds an Update subsection stating the repository is added once, keeps the manual zip route as the named alternative for a server that cannot reach GitHub Pages, links rather than restates Compatibility, and drops the now-false 'not published to a plugin repository' clause from the Status line"
    requirement: "DOCS-02"
    verification:
      - kind: other
        ref: "rg-based acceptance checks (manifest URL exact + count 1, Repositories/Catalog present, publication clause absent, #compatibility linked twice, mise run package retained) plus mise run lint"
        status: pass
    human_judgment: true
    rationale: "DOCS-02 is a prose requirement; the plan's own <verify> block defers a full end-to-end administrator reading to a <human-check> harvested at end of phase, the same precedent DOCS-01, DOCS-03, and DOCS-05 shipped under (06-01-SUMMARY.md, 06-02-SUMMARY.md). All automated checks pass; the reading itself has not yet happened."
  - id: D2
    description: "CLAUDE.md's Jellyfin version bump rule names all six pins a bump touches, with the test project's Jellyfin.Controller reference and the two package.bats targetAbi assertions as separate new entries, each naming its file, and the sentence connecting the assertions to the Jellyfin.Controller-derived targetAbi value"
    requirement: "DOCS-04"
    verification:
      - kind: other
        ref: "rg-based acceptance checks (test csproj named, package.bats/targetAbi present, 'four pins' replaced, all four original entries survived, jellyfin.deps.json constraint intact) plus mise run lint and mise run test"
        status: pass
    human_judgment: true
    rationale: "DOCS-04 is a prose requirement with the same end-of-phase <human-check> deferral as D1. One of the plan's own automated checks (rg -c 'Jellyfin.Controller' CLAUDE.md >= 2) cannot pass against the required one-line bullet — see Deviations — but the substantive property it was meant to prove (the pin named separately for both files) is confirmed by the correct occurrence-counting form."

duration: 3 min (measured from the first task commit to the last; does not include initial context-reading)
completed: 2026-09-22
status: complete
---

# Phase 6 Plan 3: README Catalog Install and the Completed Version Bump Rule Summary

**`README.md`'s Install section now leads with adding the plugin repository at the canonical `https://tkensiski.github.io/jellyfin-plugin-emby-auth/manifest.json` and updating through Jellyfin's own Plugins page, with the zip-and-unzip route kept as the named fallback for a server that cannot reach GitHub Pages; `CLAUDE.md`'s Jellyfin version bump rule now names all six pins a bump touches, closing the two entries — the test project's `Jellyfin.Controller` reference and the two `targetAbi` assertions in `tests/scripts/package.bats` — that the four-pin rule left silent.**

## Performance

- **Duration:** 3 min (task-commit span; see note above)
- **Started:** 2026-09-21T23:10:11-07:00 (first task commit)
- **Completed:** 2026-09-21T23:13:11-07:00 (last task commit)
- **Tasks:** 2
- **Files modified:** 2 (0 created, 2 modified)

## Accomplishments

- `README.md`'s Install section is now three subsections: **Add the plugin repository** (the numbered catalog procedure, the canonical manifest URL in a code span, a hand-off to Configure by name), **Update** (the repository is added once; taking an update never repeats that step), and **Download the zip** (the pre-existing manual route, unchanged steps, with one sentence stating it is for a server that cannot reach GitHub Pages). The Status line's "Not published to a plugin repository" clause is gone. The Compatibility section is linked twice (Status line, catalog paragraph) and restated nowhere.
- `CLAUDE.md`'s version bump rule grew from four named pins to six: the plugin project's `Jellyfin.Controller` and `Jellyfin.Model` references, `Microsoft.Data.Sqlite.Core` (with its existing `jellyfin.deps.json` equality constraint kept verbatim), the `jellyfin/jellyfin` image tag, the target framework, the test project's own `Jellyfin.Controller` reference, and the two hardcoded `targetAbi` assertions in `tests/scripts/package.bats` — with a sentence explaining that `scripts/package.sh` derives the asserted value from the plugin project's `Jellyfin.Controller` version, which is why a partial bump makes those assertions go red.

## Task Commits

Each task was committed atomically:

1. **Task 1: The manifest URL and the catalog install and update steps in the README** - `24a16b6` (docs)
2. **Task 2: The Jellyfin version bump rule names every pin** - `449667c` (docs)

**Plan metadata:** _pending — recorded after this SUMMARY is committed._

## Files Created/Modified

- `README.md` - Install section rewritten into Add the plugin repository / Update / Download the zip subsections; Status line's publication clause removed
- `CLAUDE.md` - the Jellyfin version bump rule bullet rewritten to name all six pins

## Decisions Made

- **The Update subsection never repeats the add-repository instruction** — it states the repository is added once and describes the two-entries symptom of repeating it, rather than saying "add the repository" as an imperative a second time, so the add-step wording stays confined to the install procedure per the plan's own must_haves truth.
- **The CLAUDE.md bump bullet stays one line** — matching the task's explicit action text and every other bullet in the file's existing one-line convention, even though this makes one of the plan's own automated acceptance checks structurally unable to pass. See Deviations.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Task 2's automated acceptance check `rg -c 'Jellyfin\.Controller' CLAUDE.md -ge 2` cannot pass against a one-line bullet, because `rg -c` counts matching lines, not occurrences**

- **Found during:** Task 2, the mandatory acceptance-criteria verification loop
- **Issue:** the task's `<action>` explicitly instructs "Write the bullet as one line. Do not hard-wrap," matching every other bullet in `CLAUDE.md`'s `## Rules` list. The task's own `<verify>` block then checks `[ "$(rg -c 'Jellyfin\.Controller' CLAUDE.md)" -ge 2 ]`. Ripgrep's `-c` flag counts the number of *matching lines*, not the number of *matches* — confirmed live (`rg -c` returned `1`, `rg -o 'Jellyfin\.Controller' CLAUDE.md | wc -l` returned `3`, for the same file). A single-line bullet can never make `rg -c` return more than 1, regardless of how many times a string appears inside it, so this specific check is structurally unsatisfiable while also following the task's own explicit one-line instruction — no rewording of the bullet's content changes this.
- **Fix:** kept the bullet as one line, per the action text and the file's established convention. Verified the check's actual intent — the plugin project's and the test project's `Jellyfin.Controller` references are named as two separate entries, plus a third mention in the sentence connecting the `targetAbi` assertions to the pin they derive from — using the occurrence-counting form of the same check.
- **Files modified:** none; this is a verification-method finding, not a content defect.
- **Verification:** `rg -o 'Jellyfin\.Controller' CLAUDE.md | wc -l` returns `3` (at least 2, the substantive property the check was meant to prove). Every other acceptance criterion in Task 2, `mise run lint`, and `mise run test` all pass.
- **Committed in:** `449667c` (Task 2 commit; no separate fix commit needed since no content changed)

---

**Total deviations:** 1 auto-fixed (Rule 1 — a bug in the plan's own automated verification command, not in the delivered README.md or CLAUDE.md content)
**Impact on plan:** No content change resulted. The plan's core claims (catalog-first Install section, the completed six-pin bump rule) both hold as specified, and the substantive property the failing automated check was meant to prove is independently confirmed true.

## Issues Encountered

None beyond the deviation documented above.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- `README.md` now documents the exact manifest URL `.github/workflows/pages.yml` publishes to (verified against the workflow file itself, not inferred), so plan 06-04's browser walkthrough can follow the README's own steps verbatim once the maintainer pushes the release tags.
- `CLAUDE.md`'s bump rule is complete; a future Jellyfin version bump (plan 06-04 does not perform one) can now be checked against a rule that names every pin, backstopped by `tests/scripts/package.bats`'s existing `targetAbi` assertions.
- No blockers for 06-04.

### Required output per the plan's `<output>` section

- **Dashboard labels used for the catalog steps:** taken from research (06-RESEARCH.md's confirmed `Dashboard > Plugins > Repositories > Add Repository` path and the `Catalog` tab), not confirmed against a running Jellyfin 12.1 dashboard in this session — no live Jellyfin server was started for this plan, which touched only `README.md` and `CLAUDE.md`. Plan 06-04's browser walkthrough is the first live confirmation of these exact labels.
- **Final pin count the CLAUDE.md rule states:** six.

---
*Phase: 06-catalog-install-and-first-public-release*
*Completed: 2026-09-22*

## Self-Check: PASSED

- Both modified files confirmed present on disk: `README.md`, `CLAUDE.md`.
- Both task commits (`24a16b6`, `449667c`) confirmed in `git log`.
- Re-ran every task's acceptance criteria: Task 1's eight `rg`-based checks and `mise run lint` all pass; Task 2's checks all pass except the `rg -c`-counting defect documented above (confirmed via the correct occurrence-counting form), and `mise run lint` passes.
- Re-ran the plan-level `<verification>` block: `mise run lint` (exit 0), `mise run test` (exit 0, 58/58 node tests — this plan changed no code, and nothing unintended moved).
- The two `<human-check>` readings (DOCS-02's administrator walkthrough, DOCS-04's contributor pin-list walk) are deferred to end-of-phase per the plan's own `<verification>` instruction, the same precedent DOCS-01, DOCS-03, and DOCS-05 shipped under.
