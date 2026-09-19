---
phase: quick-260919-inm
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - tests/js/testHelpers.js
  - tests/js/configPage.test.js
  - src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html
autonomous: true
requirements: [UI-01, UI-02, TEST-04]

estimate:
  tokens: 42000
  raw_tokens: 28000
  tasks: 3
  confidence: low

must_haves:
  truths:
    - "Each of the four failure messages renders with exactly one leading Material warning icon element, and each message string stays byte-identical to the approved text."
    - "The icon element carries aria-hidden=\"true\" and contributes no text, so the accessible text of each failure message is exactly the approved sentence."
    - "The three plain migration messages, and an emptied message element, hold no icon element at all."
    - "Writing a plain message into an element that currently shows an icon removes the icon."
    - "Writing a failure message twice into the same element leaves exactly one icon and one copy of the text."
    - "The page ships a scoped style rule that sizes the icon to the surrounding text and puts a gap between the icon and the sentence."
    - "No message element is ever built with innerHTML; the migration user list still renders a name containing markup characters as text."
  artifacts:
    - tests/js/testHelpers.js
    - tests/js/configPage.test.js
    - src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html
  key_links:
    - "All nine message sites in the page script write through one helper, showEmbyAuthMessage(element, text, isFailure), so the icon-clearing rule lives in one place."
    - "The icon markup uses the classes material-icons and warning together; the dashboard stylesheet 1133.*.css supplies both the base rule and the .material-icons.warning:before glyph."
    - "The page style rule selects #EmbyAuthConfigPage .material-icons, which matches the icon the helper creates and nothing outside this page."
---

<objective>
Put a Material warning icon at the start of every failure message on the plugin settings page, so a failure reads as a failure at a glance. Success and neutral status messages stay plain.

Purpose: the four failure messages currently look identical to the three neutral ones. A tester reading the page has nothing but the sentence to tell them apart.

Output: one message helper in the page script, an icon element on the four failure paths, one scoped style rule, and a test suite that pins the icon, its absence, its removal, and its non-accumulation.
</objective>

<execution_context>
@~/.claude/gsd-core/workflows/execute-plan.md
@~/.claude/gsd-core/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
@CLAUDE.md
@.claude/CLAUDE.md
@.claude/rules/plugin.md
@src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html
@tests/js/configPage.test.js
@tests/js/testHelpers.js
@.planning/quick/260919-208-move-settings-failure-message-next-to-sa/260919-208-SUMMARY.md
</context>

<research_findings>
Measured in the running dev Jellyfin (`docker exec emby-auth-dev-jellyfin-1`) on 2026-09-19, not assumed:

- `/jellyfin/jellyfin-web/index.html` links exactly two stylesheets: `1133.03edf3bb7ee048ee10be.css` and `main.jellyfin.62ea10d695d4592fccae.css`. Both load on every dashboard page, so both apply to a plugin configuration page.
- `1133.*.css` declares `@font-face{font-family:Material Icons;...woff2/woff/ttf}`, the base rule `.material-icons{display:inline-block;font-family:Material Icons;font-size:24px;line-height:1;...}`, and the glyph rule `.material-icons.warning:before{content:"<private-use codepoint>"}`. So `class="material-icons warning"` resolves with no new webfont, no codepoint literal in the markup, and no new class.
- The glyph arrives through CSS generated content, so the span stays empty. `element.textContent` therefore still equals the message exactly, and no ligature word can enter the accessible text.
- `.material-icons{margin-right:3px}` exists only in lazily loaded page chunks (`movies-movies`, `music-songs`, `shows-episodes`, and similar). It is **not** in either globally linked sheet. So on a plugin configuration page there is no default gap, and a 24px glyph would render flush against body text.
- `main.jellyfin.*.css` sets `vertical-align:middle` on `.material-icons` in its own scoped inline-icon rules. That is the dashboard's own precedent for an icon sitting beside text.

Conclusion: the markup is an existing convention and needs no CSS of its own to render the triangle. One small scoped rule is still needed for size and spacing, exactly as the previous quick task shipped one scoped rule for the disabled Save button.
</research_findings>

<scope_boundaries>
IN scope:
- One message helper in the page script, used by all nine message sites.
- A warning icon on the four failure messages named below.
- One scoped style rule for icon size, vertical alignment, and the gap before the sentence.
- Tests for the icon, its absence on plain messages, its removal by a later plain message, and its non-accumulation on a repeated failure.

The four FAILURE messages, which gain the icon. All four strings stay byte-identical:
1. `Jellyfin cannot load the plugin settings. Save is turned off until the settings load. See the Jellyfin log.` — written to `#EmbyAuthSettingsStatus` by the `pageshow` catch.
2. `Jellyfin cannot save the plugin settings. See the Jellyfin log.` — written to `#EmbyAuthSettingsStatus` by the `submit` catch.
3. `Jellyfin cannot read the migration status. See the Jellyfin log.` — written to `#EmbyAuthMigrationSummary` by the `loadEmbyAuthMigration` catch.
4. `Jellyfin did not start the migration. See the Jellyfin log.` — written to `#EmbyAuthMigrationSummary` by the Run migration now catch.

The three PLAIN messages, which stay without an icon:
- `The migration runs. This list updates in a few seconds.`
- `Users on the Emby login method: N. Ready to move: M.`
- `No users are on the Emby login method.`

DECIDED, not deferred:

- **Adopt the single helper.** There are nine message sites in the page script: five plain (the populated or empty migration summary, the `clearEmbyAuthMigration` blank, the `pageshow` success blank, the migration-started line, the `submit` synchronous blank) and four failure. Repeating an icon-removal step at nine sites is how a stale triangle survives. One helper, `showEmbyAuthMessage(element, text, isFailure)`, puts the removal in one place. The boolean parameter is accepted here rather than split into two functions, because there are exactly two message shapes and the parameter name reads at the call site.
- **Ship the style rule, scoped by id, with no new class.** Selector `#EmbyAuthConfigPage .material-icons`. The page contains no other Material icon, and the id prefix keeps the rule off every other dashboard page. Inventing an icon class is forbidden by the task; adding a context selector is not the same thing.
- **No change to `docs/settings.md`.** The repo rule is that documentation stays accurate when behavior changes. `docs/settings.md` already states that a message appears under Save, what it says, and what Save does. None of that becomes wrong. The icon is decoration on an already documented message, and naming it would create a maintenance obligation for a visual detail the reader does not need in order to use the plugin.

OUT of scope, stated rather than silently dropped:
- No `role="status"` on `#EmbyAuthMigrationSummary`. It has none today; adding one changes screen-reader announcement behavior and was not asked for.
- No color change to any message. The previous quick task measured that neither `--jf-palette-error-main` nor `--jf-palette-error-light` holds contrast across both the dark and the light theme.
- No rewording. All seven message strings stay byte-identical.
- No change to `.planning/phases/02-*/02-UAT.md` or `ROADMAP.md`.
</scope_boundaries>

<tdd_discipline>
The repo rule is absolute: write the test first, run it, watch it fail for the right reason, and only then change the page. Task 1 is the RED task and it touches no file under `src/`. A task that edits `configPage.html` before Task 1's run has gone red is a plan defect.

No RED commit. `prek` runs `mise run test` and blocks any commit that leaves a test failing. The RED proof is the captured failing output of Task 1, recorded in the summary. The whole change lands as one commit in Task 3.

Task 2 carries the repo's break-once rule. Its expected reddening set is enumerated exactly, having been derived by walking every existing test for a second write to the same message element. See Task 2.
</tdd_discipline>

<tasks>

<task type="tracer" tdd="true">
  <name>Task 1: Write the failing warning-icon tests</name>
  <files>tests/js/testHelpers.js, tests/js/configPage.test.js</files>
  <precondition>`tests/js/node_modules/jsdom` exists. If it does not, run `npm --prefix tests/js ci` first.</precondition>
  <behavior>
    Add exactly one helper to `tests/js/testHelpers.js` and export it. Do not create a second doubles file. The existing `ApiClient` and `Dashboard` stubs already carry every switch these tests need: `getConfigFails`, `updateConfigFails`, `migrationStatusFails`, and `runMigrationFails` are all mutable properties, so one test can flip a stub from failing to succeeding between two events.

    `messageChildren(element)` — returns an array describing the element children of a message element, in document order. Each entry holds `tagName`, `classes` (the class list as a sorted array, so the assertion does not depend on class order), `ariaHidden` (the `aria-hidden` attribute, or null), and `text` (the child's `textContent`). One `assert.deepEqual` against this array then pins the child count, the element type, the exact class set, the aria-hidden attribute, and the fact that the icon contributes no text. Give it a Google-style JSDoc block, matching the file.

    Rejected, with the reason: a `migrationSummary(document)` helper. The raw `document.querySelector('#EmbyAuthMigrationSummary')` appears in eight existing tests. Adding the helper and using it only in the tests this task touches would leave the file half-migrated, and converting all eight is diff noise in a change about icons. New tests use the raw selector, matching the surrounding file.

    In `tests/js/configPage.test.js`, add four message constants beside the two that exist, and one expected-icon constant:
    - migration read failure — `Jellyfin cannot read the migration status. See the Jellyfin log.`
    - migration start failure — `Jellyfin did not start the migration. See the Jellyfin log.`
    - migration started — `The migration runs. This list updates in a few seconds.`
    - no users — `No users are on the Emby login method.`
    - the expected icon child — `tagName` `SPAN`, `classes` `['material-icons', 'warning']`, `ariaHidden` `'true'`, `text` the empty string.

    Update these nine existing tests. Every change here either adds an assertion or tightens `assert.match` to `assert.equal` against the full approved string. No assertion may move from `assert.equal` to `assert.match`.
    - `a failed settings load shows a message on the page` — add: the settings status children deep-equal a one-element array holding the expected icon.
    - `a failed configuration update shows a message` — add the same icon assertion.
    - `a failed migration status shows its message` — tighten `assert.match` to `assert.equal` against the migration read failure constant, and add the icon assertion on the migration summary.
    - `a failed Run migration now shows its message` — tighten `assert.match` to `assert.equal` against the migration start failure constant, and add the icon assertion.
    - `the load-failure message repeats neither the configured URL nor the API key` — this test asserts `status.children.length` is 0 today, which was the no-markup-injection guarantee. Replace that with the deep-equal against the one-element expected-icon array, which is strictly stronger: it pins the exact tree rather than only its size. Keep both secret-sentinel checks, and add an exact equality against the load failure constant.
    - `the save-failure message repeats neither the configured URL nor the API key` — the same three changes, against the save failure constant.
    - `a successful load clears a stale settings failure` — add: after the recovery, the settings status children deep-equal the empty array.
    - `Run migration now sends the request and reports it` — tighten `assert.match` to `assert.equal` against the migration started constant, and add: the summary children deep-equal the empty array.
    - `an empty user list renders nothing and says so` — add: the summary children deep-equal the empty array.

    Add these five tests:
    - `a plain migration message replaces a stale warning icon` — build with two users, one ready and one not, and `migrationStatusFails` set. Fire `pageshow`, flush, assert the summary children hold the icon. Set `api.migrationStatusFails` to false, fire `pageshow` again, flush, then assert the summary children deep-equal the empty array and the summary text equals `Users on the Emby login method: 2. Ready to move: 1.`. This is the stale-triangle hazard and the populated-plain-message case in one test.
    - `a repeated settings failure shows only one icon` — with `getConfigFails`, fire `pageshow` twice with a flush after each, then assert the settings status children deep-equal the one-element expected-icon array and its text equals the load failure constant. The text equality catches a doubled sentence as well as a doubled icon.
    - `a repeated migration failure shows only one icon` — the same shape with `migrationStatusFails`, against the migration summary and the migration read failure constant.
    - `a second save attempt clears the stale failure icon at once` — with `updateConfigFails`, fire submit, flush, assert the settings status children hold the icon. Fire submit again and assert, **before any flush**, that the children deep-equal the empty array and the text is the empty string. This pins the synchronous clear in the submit handler to the helper rather than to a leftover direct write. Await a flush at the end so the pending rejection settles.
    - `the warning icon is sized and spaced for inline text` — use the existing `pageStyleRules(document)` helper to find a rule whose `selectorText` includes `.material-icons`. Assert the rule exists, that its `fontSize` and `verticalAlign` are both non-empty, and that its `marginRight` parses to a number above 0. Assert presence rather than exact values: jsdom renders nothing, so the honest claim is that the page declares a sizing and spacing rule, and the appearance itself is a human-judgment item.
  </behavior>
  <action>Write only the two test files. Do not touch `configPage.html` or any other file under `src/`. Follow the existing file style: `'use strict'`, `node:test`, `node:assert/strict`, and a Google-style JSDoc block on the new helper. Then run the suite and read every failure. Record, in the summary, the failure reason for each new and each updated test. A failure that reads as a helper bug, such as a typo or a missing export, is not a valid RED — fix the test and re-run until every remaining failure names absent page behavior.</action>
  <verify>
    <automated>npm --prefix tests/js ci && node --test tests/js/configPage.test.js</automated>
  </verify>
  <done>The run exits non-zero. Each failure traces to absent page behavior: the failure messages hold no element children, so the deep-equal against the expected-icon array fails, and no page style rule selects a Material icon.

Exactly three of the fourteen touched tests are expected to stay green in this run, because every assertion they gained is already satisfied by a plain message that has no children today: `a successful load clears a stale settings failure`, `Run migration now sends the request and reports it`, and `an empty user list renders nothing and says so`. The other eleven must fail. If the green set differs from those three, record the actual set and the reason in the summary rather than revising this prediction.

No file under `src/` has changed.</done>
</task>

<task type="auto" tdd="true">
  <name>Task 2: Add the message helper, the icon, and the scoped style rule</name>
  <files>src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html</files>
  <behavior>Task 1's tests define the contract. This task turns them green without editing them.</behavior>
  <action>
Change `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html` in one batched edit.

Style. Extend the existing `style` element inside `#EmbyAuthConfigPage`, beside the disabled-Save rule the previous quick task added. Add one rule whose selector is `#EmbyAuthConfigPage .material-icons`. Set `font-size` to `1.2em`, so the glyph scales with the surrounding sentence instead of holding the global 24px. Set `vertical-align` to `middle`, which is the value the dashboard's own inline-icon rules use in `main.jellyfin.*.css`. Set `margin-right` to `0.25em`, because the dashboard's 3px default lives only in lazily loaded page chunks and never reaches a plugin configuration page, so without this the glyph renders flush against the first word. See `<research_findings>` for each measurement.

Script. Add a function `showEmbyAuthMessage(element, text, isFailure)` near the top of the inline script, above `loadEmbyAuthMigration`. It does three things in order. First it empties the element with `replaceChildren()`. Second, only when `isFailure` is true, it creates a `span` with `createElement`, sets its `className` to the two class names `material-icons` and `warning` separated by one space, sets the attribute `aria-hidden` to the string `true`, and appends it. The span gets no text of its own: the dashboard stylesheet paints the triangle through a `:before` rule, so the element stays empty and the accessible text of the message stays exactly the sentence. Third it appends a text node built with `createTextNode(text)`. Never assign `innerHTML` anywhere in this function or at any call site.

Replace all nine message assignments with calls to that helper. Pass `true` for the fourth, sixth, eighth, and ninth of the list below, and `false` for the rest.
1. The migration summary line in `loadEmbyAuthMigration`, whose text is the existing ternary between the populated sentence and the no-users sentence.
2. The blank the `pageshow` success path writes to the settings status.
3. The blank `clearEmbyAuthMigration` writes to the migration summary.
4. The migration read failure in the `loadEmbyAuthMigration` catch.
5. The migration-started line in the Run migration now success path.
6. The migration start failure in the Run migration now catch.
7. The blank the `submit` handler writes synchronously to the settings status.
8. The save failure in the `submit` catch.
9. The load failure in the `pageshow` catch.

Leave the per-user list item untouched. It is built from an Emby user name and stays a direct `textContent` assignment on a freshly created `li`, which is not a message site. Leave `replaceChildren()` on `#EmbyAuthMigrationUsers` as it is.

All seven message strings stay byte-identical. No message may name the Emby server URL or the API key.

Run `mise run test` and `mise run lint`. Once both are clean, apply the repo's break-once rule. Delete the `replaceChildren()` line from `showEmbyAuthMessage` and re-run the settings-page suite.

The predicted reddening set is exactly these six tests. It was derived by walking every existing test for a second write to the same message element, which is the only way that line can be observed:
  - `a plain migration message replaces a stale warning icon`
  - `a repeated settings failure shows only one icon`
  - `a repeated migration failure shows only one icon`
  - `a second save attempt clears the stale failure icon at once`
  - `a successful load clears a stale settings failure`
  - `a failed load clears the migration list`

Tests that write to one message element twice but assert no text or children on it stay green, and that is expected: `a later successful load turns Save back on`, `loading the migration status twice does not accumulate entries`, `a failed re-fetch during save shows the same message`, `a failed configuration update shows a message`, and both secret-sentinel tests. In each of the last three, the synchronous blank appends an empty text node and no icon, so both the text equality and the child deep-equal still hold.

If the observed set differs from those six in either direction, record the actual set in the summary with the reason for each difference, and do not treat the run as a pass until each difference is explained. Do not revise the prediction after the fact. Then restore the line and confirm the suite is green again. Record both runs in the summary.
  </action>
  <verify>
    <automated>mise run test && mise run lint</automated>
  </verify>
  <done>`node --test` reports every settings-page test passing, including the five new ones. `mise run test` passes end to end. `mise run lint` is clean. The break-once run was performed literally, its observed reddening set is recorded, and the restore returned the suite to green.</done>
</task>

<task type="auto">
  <name>Task 3: Verify end to end, commit, and refresh the dev page</name>
  <files>src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html, tests/js/configPage.test.js, tests/js/testHelpers.js</files>
  <precondition>Docker runs and `docker ps` lists `emby-auth-dev-jellyfin-1`. The e2e suite binds ports 18096 and 28096 under project `emby-auth-e2e`, while the dev environment holds 18196 and 28196 under project `emby-auth-dev`, so the two run side by side.</precondition>
  <action>
Run `mise run e2e`. The repo rule requires it for every change under `src/`, and this page ships inside the plugin assembly as an embedded resource.

Run `prek run`, then commit all three files in one commit. Subject, imperative and under 72 characters: `feat(ui): mark the settings page failure messages with an icon`. In the body, state that all four failure messages now carry a Material warning icon, that the message text is unchanged, that the icon is `aria-hidden` so the accessible text stays the sentence, and that all nine message sites now write through one helper so a plain message always removes a stale icon. End the message with:
`Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>`

**If the commit fails with `1Password: agent returned an error`,** the user is away from the keyboard and the signing prompt expired. Do not retry in a loop, do not pass `--no-gpg-sign`, do not disable or change the signing configuration, and do not commit through any other route. Leave every file staged exactly as it is, stop the task there, and report in the summary that the work is complete and staged but unsigned, and that the user should re-run `git commit` when they are back at the keyboard. Report the remaining steps below as not done.

Then put the rebuilt page in front of the tester without destroying the environment they already configured. Do not run `scripts/dev-env.sh up` or `down`: `up` begins with `docker compose down --volumes`, which wipes the Jellyfin and Emby state and the demo users. The plugin directory is a bind mount, so republishing into it and restarting one container is enough. Run `dotnet publish src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj -c Release -o artifacts/plugin`, then `docker compose -p emby-auth-dev -f e2e/compose.yaml restart jellyfin`.

In the summary, tell the user to hard-reload the plugin settings page in the browser, because the dashboard caches the configuration page, and that the quickest way to see all four icons is to stop the Emby container so the migration calls fail, and to enter a bad Emby URL so a save fails.

Do not edit `.planning/phases/02-*/02-UAT.md` or `ROADMAP.md`.
  </action>
  <verify>
    <automated>mise run e2e && docker exec emby-auth-dev-jellyfin-1 sh -c "grep -rl '\.material-icons\.warning:before' /jellyfin/jellyfin-web/*.css" && docker exec emby-auth-dev-jellyfin-1 grep -ac 'material-icons' /config/plugins/EmbyAuth_1.0.0.0/Jellyfin.Plugin.EmbyAuth.dll</automated>
  </verify>
  <done>`mise run e2e` passes. The dashboard stylesheet check names at least one CSS file, proving the glyph class still resolves in the running Jellyfin. The grep against the mounted assembly returns at least 1, proving the rebuilt embedded page reached the container. One commit holds all three files, or the work is staged and the signing failure is reported per the branch above. The dev environment is still up on ports 18196 and 28196 with its users and settings intact.</done>
</task>

</tasks>

<threat_model>
## Trust Boundaries

| Boundary | Description |
|----------|-------------|
| plugin settings page → administrator's browser | The page renders text that originates in plugin state, in Emby user names, and in rejected promises. |
| administrator's browser → plugin API | Only the existing configuration and migration calls cross here. This change adds no call. |

## STRIDE Threat Register

| Threat ID | Category | Component | Severity | Disposition | Mitigation Plan |
|-----------|----------|-----------|----------|-------------|-----------------|
| T-QINM-01 | Tampering | `showEmbyAuthMessage` message construction | high | mitigate | The helper builds every message with `createElement`, `setAttribute`, and `createTextNode`. No `innerHTML` at any of the nine call sites. The existing test that renders a user name containing markup characters as text stays in the suite unchanged. |
| T-QINM-02 | Information disclosure | the four failure messages | high | mitigate | Every failure path passes a fixed literal and never the rejection value. The two secret-sentinel tests keep rejecting with an Error carrying a URL that contains a password and an API-key sentinel, and now also assert the exact resulting text and the exact child tree. |
| T-QINM-03 | Spoofing | a stale warning icon over a success message | medium | mitigate | The helper empties the element before every write, so a plain message cannot inherit an icon. Two tests cover it: a plain migration message after a failure, and the synchronous clear on a second save attempt. |
| T-QINM-04 | Information disclosure | screen-reader announcement of the icon | medium | mitigate | The icon element carries `aria-hidden="true"` and holds no text, so the accessible text of a `role="status"` live region stays exactly the approved sentence. Asserted by the `ariaHidden` and `text` fields of the expected-icon deep-equal. |
| T-QINM-SC | Tampering | package installs | low | accept | No package-manager install task. `npm --prefix tests/js ci` restores the existing pinned lockfile and adds no dependency. No webfont, script, or stylesheet is fetched from outside the Jellyfin server. |
</threat_model>

<verification>
- `node --test tests/js/configPage.test.js` — the RED run in Task 1 and the break-once run in Task 2.
- `mise run test` — dotnet build with warnings as errors, unit tests, script tests, settings-page tests.
- `mise run lint` — dotnet format, shellcheck, shfmt, actionlint, zizmor.
- `mise run e2e` — required by the repo rule for any change under `src/`.
- `prek run` — before the commit.
- A container-side check that `.material-icons.warning:before` is still declared in the running Jellyfin's dashboard CSS, so the plan's central measurement is re-proven rather than assumed.
</verification>

<success_criteria>
- All four failure messages render with exactly one leading `span` carrying the classes `material-icons` and `warning` and the attribute `aria-hidden="true"`.
- All seven message strings are byte-identical to the approved text, and the accessible text of each failure message is exactly the approved sentence.
- The three plain migration messages and every emptied message element hold no element child.
- A plain message written after a failure removes the icon, and a repeated failure leaves exactly one icon and one copy of the sentence.
- Nine message sites write through one helper. No `innerHTML` anywhere.
- The page ships a scoped rule for icon size, vertical alignment, and the gap before the sentence.
- `docs/settings.md`, `02-UAT.md`, and `ROADMAP.md` are unchanged.
- All three mise gates and `prek run` pass, and one commit holds the change — or the change is staged with the signing failure reported and nothing bypassed.
</success_criteria>

<output>
Create `.planning/quick/260919-inm-add-a-warning-icon-to-the-four-failure-m/260919-inm-SUMMARY.md` when done.
</output>
