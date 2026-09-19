---
status: testing
phase: 02-safe-failures-for-the-fingerprint-file-and-settings
source: [02-VERIFICATION.md]
started: 2026-09-19T08:06:56Z
updated: 2026-09-19T08:06:56Z
---

## Current Test

number: 1
name: Message legibility in a real Jellyfin dashboard
expected: |
  Both failure messages read as clear instructions to an administrator, not developer
  diagnostics, and each points to the Jellyfin log. Save is visibly unavailable after a failed
  load. Neither the Emby URL nor the API key appears in either message. The loading indicator
  does not stay up after a failed save.
awaiting: user response

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
result: [pending]

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
result: [pending]

## Summary

total: 2
passed: 0
issues: 0
pending: 2
skipped: 0
blocked: 0

## Gaps

No gaps block this phase's goal. Two warning-level findings from 02-REVIEW.md are carried forward
for awareness, not as blockers:

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
