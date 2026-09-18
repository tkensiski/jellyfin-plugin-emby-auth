# Phase 2: Safe Failures for the Fingerprint File and Settings - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-09-17
**Phase:** 2-Safe Failures for the Fingerprint File and Settings
**Areas discussed:** Fingerprint read-failure model, JS test harness and CI, Where the page script lives, Settings page failure behavior

---

## Fingerprint read-failure model

### Question 1 — retry the read, or latch the failure?

| Option | Description | Selected |
|--------|-------------|----------|
| Latch until restart | Keep today's behavior; the plugin stays in the failed state until Jellyfin restarts | |
| Retry on every call | Do not cache a failure; a transient lock or a permissions fix heals with no restart | ✓ |
| Retry only for IO errors | Latch a `JsonException`, retry an `IOException` | |

**User's choice:** Retry on every call.
**Notes:** Not asked, because ROADMAP criterion 1 already decides it: the file must be left untouched while unreadable, which rules out quarantine-and-restart.

### Question 2 — records made while the file is unreadable

| Option | Description | Selected |
|--------|-------------|----------|
| Hold them, write when the read heals | Buffer, then merge and write on the next successful read | |
| Hold in memory only, never write | Matches true for the process lifetime; consistent with the FPRT-01 write-failure rule | |
| Drop them | Record does nothing; Matches false for everyone until the file is readable | ✓ |

**User's choice:** Drop them.
**Notes:** Answered with a challenge attached — "do we need the file when we can compare against the hash in the db?" — which opened the architecture thread below. The choice was confirmed once that thread resolved in favour of keeping the file.

### Architecture challenge — can the fingerprint file be removed?

The maintainer proposed deriving the record from the database: a password present on a user assigned to the Emby login method means Emby verified it; no password means it was never synced.

**Outcome:** rejected on evidence. The negative direction holds unconditionally; the positive direction fails for a pre-created account. `e2e/helpers.bash:134-139` creates a Jellyfin account, sets a password, and then assigns the Emby login method — the flow every e2e file uses. Under the derived rule the migration task would move all such accounts to Default and make the administrator's placeholder a live credential.

**Follow-up proposal:** make the rule true by construction — wipe the password when a user is assigned to the Emby method, and require a password when flipping back to Default. A subagent verified both halves against Jellyfin v12.1.

| Half | Verdict |
|---|---|
| Wipe on assignment to Emby | Possible but best-effort. The row commits before the event publishes; `EventManager` swallows a consumer exception; and `UserManager.cs:601-611` rewrites the same column with no event at all. |
| Require a password when flipping to Default | Not possible. No veto point exists anywhere on the path in v12.1 — all five candidate mechanisms ruled out with citations. |

### Question 3 — where should the record live?

| Option | Description | Selected |
|--------|-------------|----------|
| Keep the JSON file, fix it | Guard the write, retry the read; ~20 lines, no new dependency, no roadmap change | ✓ |
| Move it to a plugin-owned SQLite DB | Removes the read-modify-rewrite bug class structurally; new dependency, reshapes Phases 2 and 3 | |
| Keep the file, add wipe-on-assignment too | Defense in depth, but best-effort and does not let the record go | |

**User's choice:** Keep the JSON file, fix it.
**Notes:** The maintainer asked whether the plugin already has a SQLite database. It does not — no `DbContext` and no SQLite reference in `src/`. It reads and writes **Jellyfin's** database through `JellyfinDbContext`, but owns no table. The SQLite option would have introduced a database that does not exist, which is what settled the choice.

---

## JS test harness and CI

| Option | Description | Selected |
|--------|-------------|----------|
| Node + node:test + happy-dom | Built-in runner, one npm dependency for a real DOM | |
| Node + node:test + hand-written DOM stub | Zero npm packages, ~100 lines of stub, fidelity risk | |
| Playwright against the e2e container | Real browser and real Jellyfin; slower, browser downloads in CI | |
| Jint inside the existing xUnit project | No new runtime; NuGet dependency plus a C# DOM shim | |

**User's choice:** Delegated — "no fucking clue, simplest easiest path".
**Notes:** Claude chose Node pinned in mise, `node --test`, and jsdom, with tests in `tests/js/` and one extra line in `mise run test` — no new CI job. Selected for least new machinery: jsdom loads the real `configPage.html` and executes the inline script, so there is no extraction step and no build step.

---

## Where the page script lives

**Decided without a separate question**, because the harness choice constrains it. The script stays inline in `configPage.html`; jsdom loads the real file with `runScripts: 'dangerously'`. Extracting it to a second embedded resource would need another `PluginPageInfo` and a check of how Jellyfin serves non-HTML plugin assets — more moving parts for no test benefit.

---

## Settings page failure behavior

| Option | Description | Selected |
|--------|-------------|----------|
| Disable Save + show a message | The admin cannot start a destructive save; page state is visibly broken | ✓ |
| Leave Save enabled, refuse on submit | Message appears when the admin tries; they may type into the fields first | |
| Write only the fields the admin changed | Permits partial recovery; "changed" is ambiguous when every input started blank | |

**User's choice:** Disable Save + show a message.
**Notes:** The message mechanism was not put to a question. Claude chose an inline element, matching the existing `#EmbyAuthMigrationSummary` pattern — already present, used by the migration section, and read directly in a test.

---

## Claude's Discretion

- The harness runtime and DOM library (delegated outright).
- The message display mechanism on the settings page.
- jsdom against happy-dom, if the planner finds a concrete reason to prefer the lighter one.
- Exact message wording, within the rule that no message repeats the Emby URL or the API key.
- Whether Phase 2 exposes a read-failure flag for Phase 3's FPRT-03, or Phase 3 adds it.
- Test file names and the split across files.
- Whether a disabled Save button is re-enabled by a later successful load, or only by a page reload.

## Deferred Ideas

- Clear the password when a user is assigned to the Emby login method — best-effort only; worth revisiting as defense in depth, never as a replacement for the record.
- Refuse a move to Default when the user has no password — not possible in Jellyfin 12.1; would need an upstream change.
- A plugin-owned SQLite store for the fingerprints — rejected for v1 as disproportionate.
- A password field on a plugin-owned "move to Default" action — dropped; `ChangePassword` already gives the safe route.
- **New finding:** a manual flip to the Default login method for a passwordless user leaves an account that a blank password opens. No requirement covers it and the plugin cannot refuse the change. Raise at the next roadmap review.
