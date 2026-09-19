---
phase: quick-260919-inm
plan: 01
subsystem: settings-page-ui
tags: [ui, accessibility, tdd]
status: complete
dependency-graph:
  requires: [260919-208]
  provides: [warning-icon-on-failure-messages]
  affects: [configPage.html, configPage.test.js]
tech-stack:
  added: []
  patterns:
    - "One message helper (showEmbyAuthMessage) mediates every DOM write to a status/summary element, so icon presence/absence lives in one place instead of nine call sites."
    - "CSS :before generated content on .material-icons.warning supplies the glyph, so the icon span stays empty and never contributes to accessible text."
key-files:
  created: []
  modified:
    - src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html
    - tests/js/configPage.test.js
    - tests/js/testHelpers.js
decisions:
  - "Reused the plan's exact helper signature showEmbyAuthMessage(element, text, isFailure) with a boolean rather than two functions, since there are exactly two message shapes."
  - "Kept the icon span textually empty (no glyph literal, no aria-label) — the dashboard's own :before rule paints the triangle, and this is what keeps element.textContent equal to the approved sentence."
metrics:
  duration: ~55min
  completed: 2026-09-19
actuals:
  tokens: 3981
  tasks: 3
  commits: 1
---

# Phase quick-260919-inm Plan 01: Add a warning icon to the four failure messages Summary

Added a Material warning icon (`span.material-icons.warning`, `aria-hidden="true"`) to the start of all four settings-page failure messages, routed through one new `showEmbyAuthMessage` helper that all nine message sites now call, so a stale icon can never survive a later plain message.

## What Was Built

- `tests/js/testHelpers.js`: added `messageChildren(element)`, exported alongside the existing helpers. It returns each element child's tag name, sorted class list, `aria-hidden` attribute, and text content, for exact deep-equal assertions against an expected child tree.
- `tests/js/configPage.test.js`: added four message string constants and one expected-icon constant; tightened nine existing tests (five `assert.match` → `assert.equal`, two `children.length === 0` → strict deep-equal against the one-element icon array, two added icon/empty-array assertions); added five new tests covering icon replacement by a plain message, non-accumulation on a repeated failure, the synchronous clear on a second save attempt, and presence of the sizing/spacing style rule.
- `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html`: added `showEmbyAuthMessage(element, text, isFailure)`, which empties the element with `replaceChildren()`, appends an icon span only when `isFailure` is true, then appends a text node. Replaced all nine direct `textContent` message assignments with calls to this helper (`true` for the four failure sites, `false` for the five plain sites). Added one scoped style rule, `#EmbyAuthConfigPage .material-icons { font-size: 1.2em; vertical-align: middle; margin-right: 0.25em; }`.

## Verification Results

### Task 1 RED run — prediction vs. actual

The plan predicted exactly three of the fourteen touched tests would stay green: `a successful load clears a stale settings failure`, `Run migration now sends the request and reports it`, and `an empty user list renders nothing and says so`.

**Actual: matches exactly.** `node --test tests/js/configPage.test.js` before touching `configPage.html` reported 19 pass / 11 fail out of 30 total. Of the 14 touched tests, precisely those three passed; the other 11 failed. Every failure traced to absent page behavior — either the message element held no children where the test expected the one-element icon array, or no page style rule selected `.material-icons`. No failure was a helper bug (no typo, no missing export); the RED run was accepted as-is with no test rewrites needed.

### Task 2 break-once — prediction vs. actual

The plan predicted deleting `element.replaceChildren()` from `showEmbyAuthMessage` would redden exactly six named tests: `a plain migration message replaces a stale warning icon`, `a repeated settings failure shows only one icon`, `a repeated migration failure shows only one icon`, `a second save attempt clears the stale failure icon at once`, `a successful load clears a stale settings failure`, `a failed load clears the migration list`.

**Actual: matches exactly.** After deleting the line, `node --test tests/js/configPage.test.js` reported 24 pass / 6 fail, and the six failing tests were exactly those six named above, no more and no fewer. The five tests the plan predicted would stay green despite a second write to the same element (`a later successful load turns Save back on`, `loading the migration status twice does not accumulate entries`, `a failed re-fetch during save shows the same message`, `a failed configuration update shows a message`, and both secret-sentinel tests) did in fact stay green. The line was restored and the suite returned to 30/30 green, confirmed again by a full `mise run test` and `mise run lint` pass afterward.

### Gate results

- `mise run test`: PASSED — 118 .NET unit tests, 8 `package.sh` bats tests, 30 settings-page node tests, all green.
- `mise run lint`: PASSED — `dotnet format --verify-no-changes`, shellcheck, shfmt, actionlint, zizmor (0 findings, 7 pre-existing suppressions).
- `mise run e2e`: PASSED — 27/27 bats scenarios, run against the isolated `emby-auth-e2e` compose project (ports 18096/28096), independent of the running `emby-auth-dev` environment.
- `prek run`: PASSED — the test hook ran against staged files and passed; the lint hook reported "no files to check" because none of the three staged files matched its path filters (shell/workflow files).

### Commit

`325855e` — `feat(ui): mark the settings page failure messages with an icon`, signed (`Good "git" signature`), three files changed (154 insertions, 24 deletions). Signing succeeded on the first attempt; no `1Password: agent returned an error` was encountered.

### Dev environment refresh

`dotnet publish src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj -c Release -o artifacts/plugin` then `docker compose -p emby-auth-dev -f e2e/compose.yaml restart jellyfin` (only the jellyfin container was restarted; `scripts/dev-env.sh up`/`down` was never run). Confirmed live in the running container:
- `docker exec emby-auth-dev-jellyfin-1 sh -c "grep -rl '\.material-icons\.warning:before' /jellyfin/jellyfin-web/*.css"` → found in `1133.03edf3bb7ee048ee10be.css`.
- `docker exec emby-auth-dev-jellyfin-1 grep -ac 'material-icons' /config/plugins/EmbyAuth_1.0.0.0/Jellyfin.Plugin.EmbyAuth.dll` → 2 (the rebuilt embedded page reached the container).

All three dev containers (`emby-auth-dev-jellyfin-1`, `emby-auth-dev-emby-1`, `emby-auth-dev-emby-proxy-1`) remained up throughout, with jellyfin reporting `healthy` seconds after restart. Emby state and demo users were never touched.

**For the user:** hard-reload the plugin settings page in the browser — the dashboard caches the configuration page. The quickest way to see all four icons: stop the Emby container so the migration calls fail, and enter a bad Emby URL so a save fails.

## Deviations from Plan

None. Both named predictions (Task 1's three-green RED set, Task 2's six-reddened break-once set) matched actual results exactly — no discrepancy to report. The commit signed successfully on the first attempt, so the signing-failure branch was not exercised.

## Known Stubs

None.

## Threat Flags

None. All four STRIDE mitigations (T-QINM-01 through T-QINM-04) were implemented as specified: no `innerHTML` anywhere, fixed literal failure messages, the helper's unconditional `replaceChildren()` clears any stale icon, and the icon span carries `aria-hidden="true"` with no text.

## Self-Check: PASSED

- FOUND: src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html
- FOUND: tests/js/configPage.test.js
- FOUND: tests/js/testHelpers.js
- FOUND: commit 325855e (`git log --oneline` confirms it in history, signature verified good)
