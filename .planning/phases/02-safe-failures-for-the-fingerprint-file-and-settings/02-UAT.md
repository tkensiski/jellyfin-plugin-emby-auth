---
status: complete
phase: 02-safe-failures-for-the-fingerprint-file-and-settings
source: [02-VERIFICATION.md]
started: 2026-09-19T08:06:56Z
updated: 2026-09-19T20:44:42Z
---

## Current Test

[testing complete]

## Tests

### 1. Message legibility in a real Jellyfin dashboard

test: |
  Run `scripts/dev-env.sh up` and open the plugin settings page on port 28196. Force a failed
  load (stop the Jellyfin container or block the plugin configuration request, then reload).
  Confirm the message is legible in the Migration section, Save is visibly unavailable, and
  neither the Emby URL nor the API key appears. Restore the load, force a failed save (make the
  configuration update fail), select Save, and confirm the message appears and the loading
  indicator does not stay up. Run `scripts/dev-env.sh down` afterward.
expected: |
  Both messages read as clear instructions to an administrator, not developer diagnostics, and
  each points to the Jellyfin log.
why_human: |
  The jsdom tests assert that a message element holds specific text. Whether that text reads
  well to a person in the real dashboard chrome is a judgment a test cannot make. Deferred from
  02-02-PLAN.md Task 3's <human-check> block under workflow.human_verify_mode=end-of-phase.
source: 02-02-PLAN.md Task 3
result: pass
reported: "I would move the save turned off below the save button so it looks like its part of it
  rather than the migration section" / "agreed it would be better to more than disable what the
  button does and make it look like its actually disabled" / "same for part b, it does as you
  said... but as before bad placement and the button should get deactivated"
severity: minor
result_note: |
  First run recorded an issue. The tester confirmed the fix in a real dashboard after commits
  1f47767, 325855e; the result above is that second, passing run.
observed: |
  Both messages say what they promise, and neither exposes the Emby URL or the API key. The
  wording passed on the first run. The placement and the button state did not, and were fixed.

  Part A, failed load. The message text matches configPage.html:93 exactly and the load did fail —
  both settings fields stayed empty. The disable works: selecting Save sends no request. Two
  presentation defects, both confirmed by the tester in a real dashboard:
    1. The message renders in the Migration section, about a screen below the Save control it
       describes. The tester did not find it unaided.
    2. Save keeps its enabled blue styling while disabled, so it reads as live next to a visibly
       grey "Run migration now". Selecting it does nothing and says nothing.

  Part B, failed save. The message appears and the loading indicator clears, as specified. The
  same two defects repeat: the message lands in the Migration section rather than with the Save
  control, and Save is never deactivated around the save attempt.

### 2. Fingerprint-file documentation reads as actionable

test: |
  Read the read-failure and write-failure bullets in `docs/how-it-works.md` (lines 48-49).
expected: |
  An administrator can act on the read bullet — it states what the plugin does while the file is
  unreadable, what it does not do to the file, and that the situation clears on its own. The
  write bullet reads as it did before this phase.
why_human: |
  Whether the wording is actionable is a judgment call, not something a regex match settles.
  Deferred from 02-03-PLAN.md Task 2's <human-check> block.
source: 02-03-PLAN.md Task 2
result: pass

## Summary

total: 2
passed: 2
issues: 0
pending: 0
skipped: 0
blocked: 0

## Gaps

- gap_id: G-02-1
  truth: "After a failed settings load, an administrator sees the failure message with the Save
    control it describes, and Save reads as unavailable."
  status: resolved
  reason: "User reported: I would move the save turned off below the save button so it looks like
    its part of it rather than the migration section. And: agreed it would be better to more than
    disable what the button does and make it look like its actually disabled."
  severity: minor
  test: 1
  status_note: "Closed by two quick tasks, then confirmed by the tester in a real dashboard."
  resolved_by: [260919-208, 260919-inm]
  resolved_at: 2026-09-19
  artifacts:
    - path: "src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html"
      issue: "One status element served both settings failures and migration status, and a
        disabled Save kept its enabled styling."
  missing: []

### Carried forward from 02-REVIEW.md

Two warning-level findings, for awareness, not as blockers:

- **WR-01** — `EmbyVerifiedPasswords.Record()` mutates its cache before the disk write, so on a
  write failure `Matches()` can still return true for that login before a restart. This
  contradicts `LogWriteFailed`'s message and `docs/how-it-works.md:49`, which both say the move
  waits until the user logs in again. The behaviour is safe, because Emby did verify that
  password. The write-failure path is FPRT-01, which belongs to Phase 3; 02-03-PLAN.md left that
  doc bullet's wording untouched for that reason. Phase 3 should correct the log message and the
  doc when it revises the write-failure guarantee.
- **WR-04** — the `pageshow` load-failure `.catch` in `configPage.html` does not clear
  `#EmbyAuthMigrationUsers`, so a successful load followed by a failed one leaves a stale
  migration list under the failure message. No Phase 2 must-have asserted list-clearing, so this
  is a follow-up rather than a phase gap. Untested.
