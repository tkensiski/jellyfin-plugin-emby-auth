# Phase 2: Safe Failures for the Fingerprint File and Settings - Research

**Researched:** 2026-09-18
**Domain:** .NET file-read fail-safety (`EmbyVerifiedPasswords`) and a Node-based jsdom test harness for an inline Jellyfin plugin settings page
**Confidence:** HIGH

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**The fingerprint file stays**

- **D-01:** `EmbyVerifiedPasswords` keeps its JSON file in `PluginConfigurationsPath` (`PluginServiceRegistrator.cs:24`, `:34-36`). The maintainer challenged the file twice and both replacements were rejected on evidence:
  - **Derive the record from the database** ("a password present on the Emby login method must be one that Emby verified") is false for a pre-created account. `e2e/helpers.bash:134-139` creates a Jellyfin account, sets the password `NAME-jf-random`, and then assigns the Emby login method. All five e2e files use it, so this is the ordinary state of a pre-provisioned account, not an edge case. Under the derived rule `EmbyLoginMethodUsers.cs:51` would mark every such account ready and the migration task would move it to Default, which makes the administrator's placeholder a live credential for a user who never authenticated. Quick Connect is worse: `SessionManager.AuthenticateDirect` publishes the move event with no password check, so the fingerprint is the only gate on that path.
  - **A plugin-owned SQLite database** would remove the read-modify-rewrite pattern that makes FPRT-01 and FPRT-02 possible, but the plugin has no database today (no `DbContext`, no SQLite reference anywhere in `src/`). It would add a dependency and a second state file for a map of about one row per migrating user. Rejected as disproportionate, per the workspace rule to keep solutions simple outside homelab.
  - Provenance cannot be derived, only recorded, because the stored hash is byte-identical whether Emby verified it or an administrator typed it. Jellyfin has nowhere to record it: `User` carries 39 Jellyfin-owned columns, `Preference.Kind` is a sealed 13-member enum, and a plugin cannot add a table or a migration to `JellyfinDbContext`.
- **D-02:** `Load()` no longer caches a failed read. Today it assigns `_fingerprints = []` before reading (`EmbyVerifiedPasswords.cs:97`) and keeps that empty dictionary for the life of the process (`:92`, `:107-110`). Instead, a failed read leaves the cache unset, so the next `Matches` or `Record` reads the file again. A transient failure, such as a file lock at startup, then heals without a Jellyfin restart. The retry cost is bounded: the dictionary is cached only after a read succeeds. — **Reversibility:** reversible — the change is local to one private method.
- **D-03:** While the read fails, `Record()` writes nothing and keeps nothing, and `Matches()` returns `false` for every user. A login that Emby accepts during that window is not recorded, so that user shows as "needs one login while Emby runs" and does not migrate until the file is readable and the user logs in again. This is the strictest fail-closed reading: the plugin never asserts provenance that is not on disk.
- **D-04:** A `JsonException` and an `IOException` get the same treatment. D-02 and D-03 make the distinction unobservable, so the code does not split the catch. The existing catch list in `Load()` (`:107`) stays as it is.
- **D-05:** The regression test asserts both halves: after a failed read followed by a login that Emby accepts, `Matches` is `false` **and** the file on disk still holds its original bytes. The second assertion is what proves no silent reset happened; without it the test passes against the current broken code.

**Settings page failures**

- **D-06:** When `getPluginConfiguration` fails on `pageshow`, the page shows a message and **disables the Save button**. The administrator cannot start a save that would write the blank inputs over the stored settings. The page is visibly broken instead of looking normal but empty. Today the `pageshow` chain has `.finally` but no `.catch` (`configPage.html:84-92`), and the submit handler re-fetches the configuration and copies the four blank inputs over it (`:110-113`).
- **D-07:** When `updatePluginConfiguration` fails, the page shows a message. Today that call has no `.catch` (`:114-116`).
- **D-08:** Both messages are written to an inline element on the page, the same pattern as the existing `#EmbyAuthMigrationSummary` (`:48`, `:63`). No `Dashboard.alert` and no toast: the inline pattern is already there, it is what the migration section uses, and it is read directly in a test. Message text never repeats the Emby URL or the API key, because the URL can carry credentials (`.claude/rules/plugin.md` §Settings).

**JavaScript test harness (TEST-04)**

The maintainer delegated this choice. The repository has no JavaScript toolchain today: no `package.json`, no Node pin in `.mise.toml`.

- **D-09:** Pin `node` in `.mise.toml`, use Node's built-in test runner (`node --test`), and add **jsdom** as the DOM. No test framework dependency. `mise run test` gains one line and there is **no new CI job**, so `ci-success` does not change. CI installs the pin through `jdx/mise-action` with no workflow change. The pre-commit `test` hook already triggers on `.html`, so editing the settings page runs these tests locally. — **Reversibility:** costly — removing it later means removing a tool pin, a `package.json`, and a CI-visible test task together.
- **D-10:** The page script **stays inline** in `configPage.html`. jsdom loads the real file with `runScripts: 'dangerously'`, so the tests execute the shipping artifact with no extraction step and no build step. `ApiClient` and `Dashboard` are injected as stub globals before parsing; a failed load or save is a stub that returns a rejected promise. The tests then dispatch `pageshow`, a form submit, and a click on **Run migration now**. Rejected: extracting the script to a second embedded resource, which would need another `PluginPageInfo` and a check of how Jellyfin serves non-HTML plugin assets — more moving parts for no test benefit.
- **D-11:** The tests assert observable outcomes, not timing. TEST-04 requires covering the migration list and **Run migration now**, which Phase 3 rewrites for UI-03. So a test asserts that the list renders one entry per user with the right text, and that the button sends the POST and shows the failure message when it fails. **No test asserts the 3-second `setTimeout`** (`:100`), so the Phase 3 change does not churn the suite.

### Claude's Discretion

- jsdom against happy-dom, if the planner finds a concrete reason to prefer the lighter one. Look up the current stable version at plan time; do not assume one.
- The exact wording of each message, within D-08's rule that no message repeats the URL or the API key.
- Whether Phase 2 exposes a read-failure flag on `EmbyVerifiedPasswords` for Phase 3's FPRT-03 to read, or Phase 3 adds it. D-02 makes the flag trivial either way.
- Test file names and the split across files, following `.planning/codebase/TESTING.md`.
- Whether the disabled Save button in D-06 is re-enabled by a later successful load, or only by a page reload.

### Deferred Ideas (OUT OF SCOPE)

- **Clear the password when a user is assigned to the Emby login method** — the maintainer's proposal to make "password present on the Emby method" mean "the plugin wrote it". The hook exists (`IEventConsumer<UserUpdatedEventArgs>` fires from `UpdatePolicyAsync`, outside the user lock), but it is best-effort and cannot carry the invariant: the row commits before the event publishes, a consumer exception is swallowed while the administrator still sees HTTP 204, and `UserManager.cs:601-611` rewrites the same column with no event at all. Worth revisiting as defense in depth, never as a replacement for the record.
- **Refuse a move to the Default login method when the user has no password** — the maintainer's second rule. **Not possible in Jellyfin 12.1.** No veto exists on that path: no cancellable event, no `IUserManager` validation hook, no route for a plugin-supplied EF Core interceptor, no extension seam in the controller or manager guards, and no `IAuthenticationProvider` member consulted at assignment. It would need an upstream Jellyfin change. Do not design or document it as a block.
- **A plugin-owned SQLite store for the fingerprints** — would remove the read-modify-rewrite pattern that makes FPRT-01 and FPRT-02 possible. Rejected for v1 as disproportionate (D-01). Revisit only if the record ever grows beyond one row per user.
- **A password field on a plugin-owned "move to Default" action** — considered and dropped. `EmbyAuthenticationProvider.ChangePassword` already gives the safe route: setting a password on an Emby-method user saves the hash and moves the user to Default in one step (`EmbyAuthenticationProvider.cs:130-132`), so an administrator never needs the login-method dropdown.

**New finding — assigned to Phase 3, not Phase 2:** A manual flip to the Default login method can leave an open account, and nothing guards it today. This is tracked as a future requirement for Phase 3 and is explicitly **not** Phase 2 work; it does not block Phase 2 planning. See `02-CONTEXT.md` `<deferred>` for the full analysis.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-------------------|
| FPRT-02 | When the fingerprint file cannot be read, a later record does not replace the file and erase the records in it. | `EmbyVerifiedPasswords.Load()` read and diffed line-by-line (Code Examples); the exact one-line-of-intent fix (stop pre-caching `[]` before the read succeeds) is documented against the current code at `EmbyVerifiedPasswords.cs:90-113`. |
| UI-01 | When loading the plugin settings fails, the settings page shows a message, and a Save after that failed load does not write empty fields over the saved settings. | `configPage.html`'s current `pageshow` chain read and diffed (Code Examples); the `.catch` + disabled-Save-button fix pattern was built and verified GREEN against a real copy of the file in this session. |
| UI-02 | When saving the plugin settings fails, the settings page shows a message. | Same file, `submit` handler; the `.catch` fix pattern was built and verified GREEN in this session. |
| TEST-04 | Automated tests run the settings page JavaScript for load, save, their error messages, the migration list, and Run migration now. CI runs these tests on each pull request, and a test fails when a load or save error handler is removed. | The full `node --test` + jsdom harness (D-09/D-10) was built and run end-to-end against the real `configPage.html` in this session: pageshow, submit, and Run-migration-now were all successfully driven; a RED-phase test correctly failed via unhandled rejection when the `.catch` was absent (Common Pitfalls #4), directly satisfying "a test fails when a load or save error handler is removed." |
</phase_requirements>

## Summary

This phase has two independent halves, both already scoped precisely by CONTEXT.md's locked decisions (D-01 through D-11). Research targeted the implementation facts those decisions depend on but do not themselves state: the exact current code the plan must diff against, and whether the D-09/D-10 jsdom harness design actually works against the shipping `configPage.html`.

For the C# half (FPRT-02), `EmbyVerifiedPasswords.Load()` (`EmbyVerifiedPasswords.cs:90-113`) is an 11-line private method. D-02/D-03/D-04 change one thing: on a catch, do not assign `_fingerprints` at all (leave it `null`) instead of pre-assigning `[]` before the read. That single edit satisfies D-02 (retry on next call), D-03 (an unset cache still results in an empty dictionary being returned for that call, so `Matches` is `false` and `Record` writes fresh), and D-04 (no catch-list split needed). This is a small, well-bounded change.

For the JavaScript half (UI-01, UI-02, TEST-04), the plan in CONTEXT.md's D-09/D-10 was empirically verified in this research session, not just reasoned about: a working `node --test` + `jsdom` harness was built against a byte-for-byte copy of the real `configPage.html`, exercising `pageshow`, form `submit`, and the **Run migration now** click, with `ApiClient`/`Dashboard` stubs injected via jsdom's `beforeParse` hook. It works. It also caught two concrete pitfalls that would otherwise cost a debugging cycle during planning or execution: (1) `node --test <directory>` does **not** recursively discover tests the way `node --test` with no argument does — passing the directory path errors, while omitting it does not; and (2) testing the D-06 "Save is disabled" behavior requires calling `.click()` on the button, not dispatching a `submit` event on the form directly, because a disabled submit button only blocks the browser's *own* click/implicit-submit path, not a programmatically dispatched `submit` event — jsdom correctly implements this spec nuance, and a test written the wrong way (dispatching `submit` directly) passes even when the disabled-button guard is completely absent, silently failing to test D-06 at all.

**Primary recommendation:** Pin `node@24.21.0` (Active LTS, satisfies jsdom's engine requirement) in `.mise.toml`, add `jsdom@28.1.0` as the sole devDependency in a new `tests/js/package.json` (not the npm dist-tag `latest`, which the legitimacy gate flagged — see Package Legitimacy Audit), write a single `tests/js/configPage.test.js` using Node's built-in `node:test` and `node:assert/strict`, and add exactly one line, `"node --test"`, to `[tasks.test]` in `.mise.toml` with **no path argument**. Inject `ApiClient`/`Dashboard` stubs via `beforeParse(window)` on the `JSDOM` constructor options, and drive events with `element.dispatchEvent(new window.Event(...))` — except for the disabled-button assertion, which must use `.click()`.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Fingerprint read-failure fail-safety (FPRT-02) | API / Backend (`EmbyVerifiedPasswords.cs`, in-process, no HTTP) | Database / Storage (the JSON file itself) | Pure server-side state-machine correctness; no client involvement |
| Settings-load failure message + disabled Save (UI-01) | Browser / Client (`configPage.html` inline script, runs in the administrator's browser inside the Jellyfin dashboard SPA) | API / Backend (the `getPluginConfiguration` call it wraps, already built) | The fix is entirely a `.catch` and a DOM mutation in client-side JS; the backend endpoint is unchanged |
| Settings-save failure message (UI-02) | Browser / Client | API / Backend (`updatePluginConfiguration`, unchanged) | Same as above |
| JS test harness (TEST-04) | Tooling / CI (no runtime tier — it never ships) | — | Runs `node --test` against the shipping HTML file with a jsdom DOM; adds to `mise run test`, not to any deployed artifact |
| Migration list rendering + Run migration now (existing, exercised but not changed by this phase per D-11) | Browser / Client | API / Backend (`EmbyAuthController`, already built, already tested by e2e) | This phase only adds coverage; UI-03 (Phase 3) changes the behavior |

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `node` (mise tool) | `24.21.0` [VERIFIED: `mise ls-remote node` / `mise latest node@24`, run 2026-09-18] | JS runtime for the test harness | Active LTS per the Node.js release schedule [VERIFIED: github.com/nodejs/LTS, fetched 2026-09-18 — 24.x Active LTS 2025-10-28 through 2026-10-20, Maintenance until 2028-04-30]; satisfies jsdom's `engines.node` for both the recommended pin (`>=24.0.0`) and the newest release (`^24.15.0`) |
| `jsdom` | `28.1.0` [VERIFIED: `npm view jsdom@28.1.0 engines` / `npm view jsdom time`, run 2026-09-18] | DOM implementation that loads and executes the real `configPage.html` in Node | D-10; the only DOM implementation with a documented `runScripts: "dangerously"` mode that executes inline `<script>` tags exactly as a browser would, with no bundler or build step [CITED: registry.npmjs.org/jsdom README, fetched 2026-09-18] |
| Node built-in `node:test` | bundled with Node 24 | Test runner | D-09 — "no test framework dependency"; stable in Node 24, no separate install |
| Node built-in `node:assert/strict` | bundled with Node 24 | Assertions | Pairs with `node:test`; no separate install |

### Supporting

None. D-09 explicitly rejects a test framework dependency beyond jsdom.

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| jsdom | happy-dom | CONTEXT.md leaves this to planner discretion. happy-dom is lighter and faster, but its `<script>` execution model differs from a real browser's parser-driven synchronous execution and its API-compatibility with jsdom's `beforeParse`/`runScripts: "dangerously"` combination (the mechanism this research verified end-to-end) is not the same surface. Given the harness was already built and proven against jsdom in this session, switching to happy-dom would mean re-verifying the same event-dispatch and disabled-button semantics from zero — no concrete reason to prefer it surfaced during research. **Recommendation: stay with jsdom.** |
| `npm --prefix tests/js ci` (committed lockfile) | `npm install` on every run | `ci` requires and enforces a committed `package-lock.json`, consistent with the repo's "pin exact versions" rule; `install` would silently drift the resolved dependency tree between local and CI runs |

**Installation:**
```bash
mkdir -p tests/js
cd tests/js
npm init -y
npm pkg set type=commonjs
npm install --save-exact jsdom@28.1.0
# commit tests/js/package.json and tests/js/package-lock.json
```

**Version verification:** Ran 2026-09-18 against the live npm registry (not training data):
```
$ npm view jsdom version           # 30.1.0 (dist-tag latest)
$ npm view jsdom@28.1.0 engines    # { node: '^20.19.0 || ^22.12.0 || >=24.0.0' }
$ npm view jsdom@28.1.0 --json | .time['28.1.0']   # 2026-02-15T04:11:42.935Z
```
`30.1.0` (the `latest` dist-tag) was published `2026-09-17T00:57:24.186Z` — one day before this research session. `28.1.0` was published 2026-02-15, roughly seven months prior, and satisfies the same Node-version floor as the repo's other tooling. See Package Legitimacy Audit for why `28.1.0` is recommended over `latest`.

## Package Legitimacy Audit

| Package | Registry | Age (of pinned version) | Downloads (package-wide) | Source Repo | Verdict | Disposition |
|---------|----------|-----|-----------|-------------|---------|-------------|
| `jsdom` | npm | `28.1.0` published 2026-02-15 (~7 months) | 91,064,755/week [VERIFIED: `gsd-tools query package-legitimacy check`, run 2026-09-18] | `github.com/jsdom/jsdom` [VERIFIED: `npm view jsdom repository.url`] | `SUS` (on `latest`=30.1.0 — see note) | Approved, planner adds `checkpoint:human-verify` before the `npm install` task per the SUS-verdict protocol |

**Note on the SUS verdict:** `gsd-tools query package-legitimacy check --ecosystem npm jsdom` returned `"verdict": "SUS", "reasons": ["too-new"]`. This check queries the registry's general package metadata, which reflects the `latest` dist-tag (`30.1.0`, published within 24 hours of this research session) — it cannot be scoped to a specific pinned version. All other signals are unambiguous: `exists: true`, `deprecated: false`, `postinstall: null` (no arbitrary code runs on install), a canonical GitHub source repo matching the well-known `jsdom/jsdom` project (283 published versions since 2011), and 91M weekly downloads. `npm view jsdom scripts.postinstall` returned empty — no postinstall script exists at any version. **Recommendation:** pin `28.1.0`, not `latest`/`30.1.0` — this sidesteps the "too-new" signal on its merits (the recommended version is seven months old) while still following the protocol's requirement to gate the install behind a checkpoint given the tool's verdict on the package's current `latest` state.

**Packages removed due to [SLOP] verdict:** none.
**Packages flagged as suspicious [SUS]:** `jsdom` — see note above; not a hallucination or supply-chain risk on the evidence gathered, but the checkpoint is cheap and the protocol requires it.

## Architecture Patterns

### System Architecture Diagram

```
                        Administrator's browser (Jellyfin dashboard SPA)
                        ───────────────────────────────────────────────
   SPA navigates to            pageshow event
   the plugin's                on #EmbyAuthConfigPage
   config page   ──────────────────────┐
                                        ▼
                          ApiClient.getPluginConfiguration()
                                        │
                       ┌────────────────┴────────────────┐
                       │ success                          │ failure  [NEW: D-06]
                       ▼                                   ▼
              populate 4 input fields          show inline message,
              call loadEmbyAuthMigration()     disable Save button
                       │
                       ▼
              ApiClient.getJSON('EmbyAuth/Migration')
                       │
              (existing, untouched — EmbyAuthController,
               EmbyLoginMethodUsers.ListAsync, which calls
               EmbyVerifiedPasswords.Matches per user)


   Administrator clicks Save (button, if not disabled)
                       │
                       ▼
              form 'submit' event
                       │
                       ▼
        ApiClient.getPluginConfiguration()  (re-fetch, current code)
                       │
              ┌────────┴────────┐
              │ success          │ failure  [NEW: D-07]
              ▼                  ▼
    ApiClient.updatePluginConfiguration()   show inline message
              │
     ┌────────┴────────┐
     │ success          │ failure  [NEW: D-07]
     ▼                  ▼
  Dashboard.process...  show inline message


                        Jellyfin server process
                        ────────────────────────
   Emby login accepted ──► EmbyAuthenticationProvider
                                      │
                                      ▼
                          EmbyVerifiedPasswords.Record(userId, hash)
                                      │
                                      ▼
                          lock { Load() ──► Record/Matches }
                                      │
                          [CHANGED: D-02/D-03] a failed Load()
                          leaves _fingerprints unset (not []),
                          so the NEXT call retries the read
                          instead of caching an empty map forever
```

### Recommended Project Structure

```
tests/js/
├── package.json          # devDependency: jsdom, pinned exact; "type": "commonjs"
├── package-lock.json      # committed — npm ci in CI, not npm install
├── configPage.test.js     # single file: pageshow, submit, run-migration, error paths
└── testHelpers.js         # buildDom({...failure flags}), flush() — mirrors TestDoubles.cs's
                            # role as the one shared file among C# test files (TESTING.md)
```

### Pattern 1: Injecting page-global stubs before the inline `<script>` runs

**What:** jsdom's `JSDOM` constructor accepts a `beforeParse(window)` callback that runs *before* the HTML is parsed — and therefore before any inline `<script runScripts="dangerously">` executes, since with `runScripts: "dangerously"` jsdom executes inline scripts synchronously as the parser reaches them, exactly like a real browser.
**When to use:** Any time a page under test references globals (`ApiClient`, `Dashboard`) that the real Jellyfin dashboard shell normally provides. `configPage.html`'s top-level script only *registers* event listeners referencing `ApiClient`/`Dashboard` inside their callback bodies — it does not call them at parse time — so, empirically, injection even *after* construction would also work for this specific page. `beforeParse` is still the correct mechanism because it matches D-10's stated intent literally ("injected... before parsing") and is defensive against any future top-level use.
**Example (verified working in this session against a byte-identical copy of the shipping file, jsdom 28.1.0, Node 24.19.0):**
```javascript
// Source: jsdom README (registry.npmjs.org/jsdom, "Loading subresources" / ConstructorOptions.beforeParse,
// cross-checked against DefinitelyTyped/DefinitelyTyped types/jsdom/index.d.ts) + empirical verification 2026-09-18
const { JSDOM } = require('jsdom');
const fs = require('node:fs');

function buildDom({ getConfigFails = false } = {}) {
  const html = fs.readFileSync('src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html', 'utf8');
  return new JSDOM(html, {
    url: 'http://localhost/configurationpage', // any non-about:blank URL; no subresources are fetched
    runScripts: 'dangerously',
    beforeParse(window) {
      window.ApiClient = {
        getUrl: (path) => 'http://localhost/' + path,
        getJSON: () => Promise.resolve({ Users: [] }),
        ajax: () => Promise.resolve(),
        getPluginConfiguration: () =>
          getConfigFails
            ? Promise.reject(new Error('load failed'))
            : Promise.resolve({ EmbyServerUrl: 'x', EmbyApiKey: 'y', MigrationMode: 'MoveAfterFirstLogin', AccountAccess: 'CopyEmbyRemoteAccess' }),
        updatePluginConfiguration: () => Promise.resolve({}),
      };
      window.Dashboard = { showLoadingMsg() {}, hideLoadingMsg() {}, processPluginConfigurationUpdateResult() {} };
    },
  });
}
```
No `resources: "usable"` option is needed: `configPage.html` has no external `<script src>`, `<link>`, or `<img>`, and every network call the page makes goes through the stubbed `ApiClient`/`Dashboard`, not through jsdom's own fetch/resource pipeline. This also sidesteps a real jsdom limitation — **jsdom does not implement `window.fetch`** at all, by design [CITED: github.com/jsdom/jsdom/issues/1724, "jsdom doesn't have the capability... we need our own implementation... Node's is not relevant", still open as of the versions checked 2026-09-18] — irrelevant here only because the page never calls `fetch` directly; it always goes through `ApiClient`.

### Pattern 2: Driving `pageshow`, `submit`, and the migration button

**What:** Dispatch plain DOM events programmatically; do not use `HTMLFormElement.prototype.submit()` (which does not fire a `submit` event, matching real-browser behavior, and attempts navigation) or real Jellyfin SPA navigation (not present in this harness).
**Verified working (Node 24.19.0, jsdom 28.1.0, `node --test`, 2026-09-18):**
```javascript
// pageshow: the div's own 'pageshow' listener, not window's — matches configPage.html:81-93
document.querySelector('#EmbyAuthConfigPage').dispatchEvent(new window.Event('pageshow'));

// submit: dispatch directly on the <form>; must be { cancelable: true } so e.preventDefault() works
const form = document.querySelector('#EmbyAuthConfigForm');
form.dispatchEvent(new window.Event('submit', { cancelable: true }));

// Run migration now: a plain click listener, not a submit button — bubbles is harmless either way
document.querySelector('#EmbyAuthRunMigration')
  .dispatchEvent(new window.Event('click', { bubbles: true, cancelable: true }));

// Flushing chained .then()/.catch()/.finally() after dispatch — see Common Pitfalls #3
async function flush() {
  await new Promise((resolve) => setTimeout(resolve, 0));
  await new Promise((resolve) => setTimeout(resolve, 0));
}
```

### Pattern 3: Testing the disabled Save button (D-06) — must use `.click()`

**What:** After a failed load, D-06 requires the Save button to be disabled so a save cannot overwrite stored settings with blank fields. The *only* faithful test is `saveButton.click()`, verified against jsdom's real disabled-element semantics:
```javascript
// Source: empirical verification, jsdom 28.1.0, 2026-09-18 — see Common Pitfalls #2 for the failure
// mode this avoids.
const saveButton = document.querySelector('.button-submit');
assert.equal(saveButton.disabled, true);
let submitFired = false;
document.querySelector('#EmbyAuthConfigForm').addEventListener('submit', () => { submitFired = true; });
saveButton.click();
await flush();
assert.equal(submitFired, false); // PASSED in this session's smoke test
```

### Anti-Patterns to Avoid

- **`node --test <directory>` as a discovery mechanism:** does not work in Node 24 — see Common Pitfalls #1. Use `node --test` with no argument, or Node's own quoted glob syntax (`node --test "tests/js/**/*.test.js"` — untested in this session; the no-argument form was verified and is simpler).
- **Testing D-06 by dispatching `submit` directly on the form:** passes even with the disabled-button guard entirely missing — see Common Pitfalls #2. Silently defeats the acceptance criterion it was meant to test.
- **Real `HTMLFormElement.submit()`:** does not fire the `submit` event per spec (true in real browsers too) and jsdom additionally logs a "not implemented" navigation warning. Use `dispatchEvent(new Event('submit', {cancelable: true}))` instead.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| DOM + inline-script execution for a settings page test | A regex-based HTML parser or a manual `vm.runInContext` harness | jsdom's `runScripts: "dangerously"` | jsdom already implements the HTML parsing, event loop integration, and DOM API surface that a hand-rolled harness would need to reinvent; D-10 already rejected the "extract the script to a separate file" alternative on the same reasoning |
| Assertion library for `node --test` | A custom `assert`-like helper | `node:assert/strict` (built in) | Zero dependency, already ships with the pinned Node version |
| Test file discovery/glob | A custom `find`-based script feeding file paths to `node --test` | `node --test` with no path argument | Verified in this session: default discovery already recurses from cwd, matches `*.test.js`, and automatically skips `node_modules` — a hand-rolled `find` invocation would duplicate behavior Node already provides correctly |

**Key insight:** Every part of the D-09/D-10 harness design (Node's own test runner, jsdom's own script-execution mode, jsdom's own `beforeParse` injection point) is a documented, first-party mechanism. The only genuine engineering work in this phase is the four to six lines of `.catch()`/`.then()` diff to `configPage.html` and the ~15-line diff to `EmbyVerifiedPasswords.Load()`.

## Common Pitfalls

### Pitfall 1: `node --test <directory>` does not recursively discover tests

**What goes wrong:** `node --test tests/js` (passing the directory as a positional argument) fails with `MODULE_NOT_FOUND` / a test named after the directory that immediately fails — Node treats the path argument as something closer to a single module specifier, not a recursive-search root, in this version.
**Why it happens:** Verified empirically 2026-09-18 (Node v24.19.0): `node --test tests/js` → `Error: Cannot find module '.../tests/js'`, reported as one failing test named `tests/js`. Running `node --test` with **no** path argument, from the same working directory, correctly discovers `tests/js/configPage.test.js` (1 test found, ran, passed) and does not descend into `tests/js/node_modules` (confirmed: `node_modules/jsdom` contains hundreds of `.js` files; none were picked up as tests).
**How to avoid:** Add `"node --test"` (no arguments) to `[tasks.test]` in `.mise.toml`, run from the repo root (mise's default working directory for tasks). Do not add a directory or glob argument.
**Warning signs:** A `mise run test` that reports 0 or 1 unexpectedly-named failing "test" instead of the real suite's test count.

### Pitfall 2: A disabled Save button does not block a directly-dispatched `submit` event

**What goes wrong:** A test that asserts D-06 by doing `form.dispatchEvent(new Event('submit'))` after the load failure will still see `updatePluginConfiguration` get called — even with the `saveButton.disabled = true` line deleted from the fix entirely. The test gives a false pass.
**Why it happens:** Per the HTML spec (and confirmed empirically in jsdom 28.1.0), a disabled submit button only suppresses the browser's *own* activation behavior — a real user click on it, and the "implicit submission" that happens when Enter is pressed in a text field and the only submit button is disabled. A `submit` `Event` object dispatched directly via `EventTarget.dispatchEvent()` bypasses that check entirely; the form's `submit` listener fires regardless of any button's `disabled` state.
**How to avoid:** Test the disabled-button behavior by calling `.click()` on the button element itself (see Code Examples, Pattern 3), not by dispatching `submit` on the form. Verified in this session: `saveButton.click()` on a disabled button correctly produces zero `submit` event dispatches; a directly-dispatched `submit` event on the form does not respect `disabled` at all.
**Warning signs:** A "Save is disabled after a failed load" test that never fails no matter what the disabled-button code does.

### Pitfall 3: Promise-chain timing after `dispatchEvent`

**What goes wrong:** `dispatchEvent()` is synchronous, but the `pageshow`/`submit`/`click` handlers in `configPage.html` are `.then()`/`.catch()`/`.finally()` chains 2-3 levels deep. Asserting on DOM state immediately after `dispatchEvent()` returns will see pre-resolution state.
**Why it happens:** Each `.then()` in the chain is a separate microtask tick; a chained `.catch().finally()` needs multiple event-loop turns to fully resolve, and the stub `ApiClient` methods return freshly-created `Promise.resolve()`/`Promise.reject()` values, adding another tick each.
**How to avoid:** `await` at least two `setTimeout(resolve, 0)` macrotask boundaries after dispatch before asserting (verified sufficient for the current 2-level chain depth in this session; add more if a chain grows deeper). A single `await Promise.resolve()` was insufficient in early iterations of this session's smoke test — `setTimeout`-based flushing was reliable, `queueMicrotask`-based flushing was not tested but is a plausible lighter-weight alternative worth trying at plan time.
**Warning signs:** Flaky assertions that pass under a debugger (which adds delay) but fail in CI, or that fail only for the deeper 3-level submit chain while the shallower pageshow chain passes.

### Pitfall 4: An uncaught rejection inside the RED-phase test is the correct TDD signal, not a harness bug

**What goes wrong (misdiagnosis risk):** Before the D-06/D-07 fix is applied, dispatching `pageshow` with a failing `getPluginConfiguration` stub produces a test failure whose stack trace looks like an unrelated jsdom internal error (`at HTMLDivElement.callTheUserObjectsOperation ... at EventTarget-impl.js`), not a normal assertion failure.
**Why it happens:** This is Node's unhandled-rejection detection surfacing the exact defect TEST-04 requires the suite to catch: today's `pageshow` chain has `.finally()` but no `.catch()` (`configPage.html:90-92`, unchanged as of this research), so a rejected `getPluginConfiguration()` promise is never handled, and `node --test` correctly attributes the resulting unhandled rejection to the currently-running test.
**How to avoid:** Nothing to avoid — this is the desired RED-phase behavior for the TDD cycle CLAUDE.md requires ("write the test first... watch it fail"). Verified in this session: the failure did not crash the whole test run; `node --test` isolated it to the one test and continued running the other four, exiting 1 overall.
**Warning signs:** None — flag this in the plan so whoever executes it does not mistake the RED failure's stack trace for a broken harness and "fix" the test instead of the page.

## Code Examples

### The exact current `Load()` method D-02/D-03/D-04 changes

```csharp
// Source: src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs:90-113 (read 2026-09-18)
private Dictionary<Guid, string> Load()
{
    if (_fingerprints is not null)
    {
        return _fingerprints;
    }

    _fingerprints = [];
    if (!File.Exists(_filePath))
    {
        return _fingerprints;
    }

    try
    {
        _fingerprints = JsonSerializer.Deserialize<Dictionary<Guid, string>>(File.ReadAllText(_filePath)) ?? [];
    }
    catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
    {
        LogReadFailed(_logger, ex, _filePath);
    }

    return _fingerprints;
}
```
The defect line 97 assigns `_fingerprints = []` **before** the file-existence check and the try/catch, so a caught exception at line 109 leaves that empty dictionary cached in `_fingerprints` for the rest of the process's life (confirmed: no code path re-nulls `_fingerprints` after this point; `Record` and `Matches` both call `Load()` under the same `_lock`, `:47` and `:83`). D-02's fix is to stop pre-assigning before the read succeeds — return a *local* empty dictionary on the exception path instead of caching it into the field, so the next call re-enters this method and retries the read.

### The exact current settings-page script D-06/D-07/D-08 change

```javascript
// Source: src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html:81-121 (read 2026-09-18)
document.querySelector('#EmbyAuthConfigPage')
    .addEventListener('pageshow', function () {
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(EmbyAuthConfig.pluginUniqueId).then(function (config) {
            document.querySelector('#EmbyServerUrl').value = config.EmbyServerUrl;
            document.querySelector('#EmbyApiKey').value = config.EmbyApiKey;
            document.querySelector('#MigrationMode').value = config.MigrationMode;
            document.querySelector('#AccountAccess').value = config.AccountAccess;
            return loadEmbyAuthMigration();
        }).finally(function () {           // <-- no .catch before this .finally (D-06 target)
            Dashboard.hideLoadingMsg();
        });
    });

document.querySelector('#EmbyAuthConfigForm')
    .addEventListener('submit', function (e) {
        Dashboard.showLoadingMsg();
        ApiClient.getPluginConfiguration(EmbyAuthConfig.pluginUniqueId).then(function (config) {
            config.EmbyServerUrl = document.querySelector('#EmbyServerUrl').value;
            config.EmbyApiKey = document.querySelector('#EmbyApiKey').value;
            config.MigrationMode = document.querySelector('#MigrationMode').value;
            config.AccountAccess = document.querySelector('#AccountAccess').value;
            ApiClient.updatePluginConfiguration(EmbyAuthConfig.pluginUniqueId, config).then(function (result) {
                Dashboard.processPluginConfigurationUpdateResult(result);
            });                              // <-- no .catch anywhere in this handler (D-07 target)
        });

        e.preventDefault();
        return false;
    });
```
The `#EmbyAuthMigrationSummary` element (`configPage.html:48`) already exists and is already the inline-message target for the existing `loadEmbyAuthMigration` failure path (`:76-78`) and the existing `Run migration now` failure path (`:101-103`) — D-08 reuses this exact element and pattern, adding no new DOM node. The `Save` button element to disable per D-06 is `.button-submit` (`configPage.html:40`, the only element with that class on the page — a `document.querySelector('.button-submit')` is unambiguous).

**Verified GREEN**, this session: applying `.catch()` handlers matching this shape (setting `summary.textContent` and, on the pageshow path, `saveButton.disabled = true`) to a copy of this file, then re-running the same jsdom harness, produced the expected passing behavior — the load-failure message appeared, the button became disabled, and (separately) a save-failure message appeared without needing to change the submit handler's re-fetch structure.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|---------------|--------|
| jsdom's legacy `ResourceLoader` class for subresource fetching | `resources: "usable" \| { userAgent, dispatcher, interceptors }` built on `undici` | jsdom v27+ [CITED: github.com/jsdom/jsdom PR #4033] | Not used by this phase's harness at all (no subresources are fetched — see Pattern 1), but relevant if a future phase needs jsdom to fetch something real; do not use the old `ResourceLoader` class shape from older jsdom tutorials found via general web search. |
| Node test runner glob support absent | `node --test` supports glob patterns as of Node 21+ | Node 21 [CITED: multiple GitHub issue threads, nodejs/node#50658 and nodejs/help#3902, cross-referenced 2026-09-18] | Irrelevant to this phase's recommended no-argument invocation, but relevant if the planner considers an explicit glob instead — quoting matters (`"tests/js/**/*.test.js"`, not unquoted) and this session's own attempt at an explicit glob returned 0 matches even quoted, so the no-argument form is the only one verified working here. |

**Deprecated/outdated:** None specific to this phase's scope.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | A single `tests/js/configPage.test.js` file (rather than one file per behavior group) is the right granularity, by analogy to TESTING.md's one-file-per-tested-type C# convention. | Recommended Project Structure | Low — this is a file-organization choice with no behavioral consequence; easy to split later if the file grows unwieldy. |
| A2 | `queueMicrotask`-based flushing was not empirically tried as a lighter alternative to the two-`setTimeout` flush used in this session's verified smoke tests. | Pitfall 3 | Low — the `setTimeout`-based flush is proven to work; a planner or executor who wants to try `queueMicrotask` instead should re-verify it against the actual chain depth, not assume it is equivalent. |
| A3 | `node --test "tests/js/**/*.test.js"` (a quoted glob) returning 0 matches in this session is a genuine finding and not an artifact of this session's specific shell/quoting setup. | State of the Art table | Low-Medium — if this was a local artifact, the no-argument form (independently verified twice, in two different directory layouts) is still the recommended, simpler approach regardless. |

## Open Questions

None blocking. CONTEXT.md's "Claude's Discretion" items (exact message wording, Save-button re-enable trigger, read-failure flag exposure, test file split) remain the planner's to decide within the constraints this research confirms are technically sound.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| `node` | JS test harness (TEST-04) | ✓ (locally, via mise; not yet pinned in `.mise.toml`) | 24.19.0 installed locally; `24.21.0` is the latest 24.x and the recommended pin | — |
| `npm` | Installing `jsdom`, running `npm ci` | ✓ (bundled with node) | 11.17.0 | — |
| Docker | Not required by this phase (no e2e test added; existing e2e suite is a regression gate only, per CLAUDE.md's "mise run e2e for every change to src/" rule) | ✓ (assumed from Phase 1's e2e usage; not re-probed this session) | — | — |

**Missing dependencies with no fallback:** none — `node` is not yet pinned in `.mise.toml`, but this phase's own work is to add that pin; it is a plan deliverable, not a blocker.

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework (C# half) | xUnit v3 `4.0.1`, Microsoft.Testing.Platform, per `tests/Jellyfin.Plugin.EmbyAuth.Tests.csproj:10` [VERIFIED: TESTING.md, cross-read against the csproj this session] |
| Framework (JS half, new) | Node built-in `node:test` + `node:assert/strict`, no config file — behavior is CLI-flag driven |
| Quick run command (C#) | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` (whole suite: 786ms measured in TESTING.md; no filter needed at this size) |
| Quick run command (JS) | `node --test` from repo root (no path argument — see Pitfall 1) |
| Full suite command | `mise run test` (adds the `node --test` line to the existing `dotnet test` + `bats tests/scripts` chain) |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| FPRT-02 | A failed fingerprint-file read does not erase existing records on the next accepted login | unit (xUnit) | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` | ⚠️ Extend existing `UnreadableFile_MatchesNothing_AndLogsAnError` (`EmbyVerifiedPasswordsTests.cs:100-108`) per D-05's two-assertion requirement — file does not yet assert on-disk bytes after a subsequent `Record` |
| UI-01 | Failed settings load shows a message and disables Save; a subsequent Save does not overwrite stored settings with blanks | unit (`node:test` + jsdom) | `node --test` | ❌ Wave 0 — `tests/js/` does not exist yet |
| UI-02 | Failed settings save shows a message | unit (`node:test` + jsdom) | `node --test` | ❌ Wave 0 |
| TEST-04 | Automated JS tests cover load, save, their error messages, the migration list, and Run migration now; CI runs them; a test fails when an error handler is removed | unit (`node:test` + jsdom) | `node --test` (via `mise run test` in CI's existing `test` job — no new CI job) | ❌ Wave 0 |

### Sampling Rate

- **Per task commit:** run the affected layer's quick command only (`dotnet test --solution ...` for the C# change; `node --test` for the JS change) — both complete in well under a second based on this session's measurements (786ms for the full C# suite; the 5-test jsdom smoke suite in this session ran in ~3.4s including a full jsdom parse-and-execute per test, well within a "quick" budget).
- **Per wave merge:** `mise run test` (full: `dotnet test` + `bats tests/scripts` + `node --test`).
- **Phase gate:** `mise run test` green, plus the existing `mise run e2e` regression run per CLAUDE.md's "a change under `src/` needs `mise run e2e`" rule — `EmbyVerifiedPasswords.cs` is under `src/`, so the existing e2e suite (unmodified, no new e2e test required by this phase's requirements) must still pass. `configPage.html` is also under `src/`, so the same rule applies to the JS-facing half; no e2e test currently loads or exercises the settings page beyond an HTTP 200 check (`e2e/10-login-checks.bats:15-18`, unchanged), which remains sufficient given the new coverage is at the unit layer.

### Wave 0 Gaps

- [ ] `tests/js/package.json` + `tests/js/package-lock.json` — new, holds the `jsdom` devDependency
- [ ] `tests/js/configPage.test.js` — new, the TEST-04 suite
- [ ] `tests/js/testHelpers.js` — new, shared `buildDom()`/`flush()` helpers (optional split; could also live at the top of the single test file given the harness's small size)
- [ ] `.mise.toml` — add `node` tool pin and the `"node --test"` line to `[tasks.test]`
- [ ] `.gitignore` — add `node_modules/` (currently absent; confirmed by reading `.gitignore` this session — only `bin/`, `obj/`, `artifacts/`, `TestResults/` are listed)
- [ ] `EmbyVerifiedPasswordsTests.cs` — extend `UnreadableFile_MatchesNothing_AndLogsAnError` (or add a new test) per D-05's on-disk-bytes assertion

*(`.pre-commit-config.yaml`'s `test` hook already matches `.html` — confirmed this session, `.pre-commit-config.yaml:15` — so no pre-commit config change is needed; a `configPage.html` edit already triggers `mise run test` locally once the new task line exists.)*

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-------------------|
| V5 Input Validation | No new input surface — the settings page's inputs and their server-side validation (`EmbyAuthSettings.cs`) are unchanged by this phase | N/A |
| V7 Error Handling and Logging | Yes — this phase's entire purpose is adding error-handling UI | Generic, non-specific inline error messages (`#EmbyAuthMigrationSummary.textContent`); never `innerHTML`; never repeat configured values |

### Known Threat Patterns for this stack

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|----------------------|
| An error message echoes the configured Emby URL or API key back to the page (both can carry or grant credentials — a URL can embed `user:pass@host`, per `.claude/rules/plugin.md` §Settings) | Information Disclosure | D-08: messages are fixed strings ("Jellyfin cannot load the settings. See the Jellyfin log." / "...save the settings...") that never interpolate `config.EmbyServerUrl` or `config.EmbyApiKey`. Verified in this session's GREEN smoke test — the fix pattern used a fixed string, not a template literal referencing the config object. |
| Rendering a failure message (or the migration list) with `innerHTML` from server-controlled or Emby-controlled data (a crafted Emby username) | Tampering (stored/reflected XSS) | Already-established repo pattern: every DOM write in `configPage.html` uses `.textContent`, never `.innerHTML` (`configPage.html:73`, `.claude/rules/plugin.md` §API and settings page). The new D-06/D-07/D-08 messages must follow the same rule — they are fixed strings, so this is automatically satisfied, but a plan reviewer should confirm no interpolation sneaks in later. |

## Sources

### Primary (HIGH confidence)

- `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs` (read in full, 2026-09-18)
- `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html` (read in full, 2026-09-18)
- `src/Jellyfin.Plugin.EmbyAuth/EmbyLoginMethodUsers.cs:30-53` (read 2026-09-18)
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs` (read in full, 2026-09-18)
- `docs/how-it-works.md` (relevant sections read 2026-09-18; line 48's existing text about read/write failure already matches the D-02/D-03 target behavior — likely needs no doc change)
- `.mise.toml`, `.github/workflows/ci.yml`, `.pre-commit-config.yaml`, `.gitignore` (all read in full, 2026-09-18)
- `.planning/codebase/TESTING.md`, `.planning/codebase/CONCERNS.md` (read in full, 2026-09-18)
- Empirical jsdom + `node --test` harness built and run against a byte-identical copy of the shipping `configPage.html`, this session, scratchpad directory, Node v24.19.0, jsdom 28.1.0 — 4 RED-phase assertions passed, 1 correctly failed via unhandled rejection (Pitfall 4); a GREEN-phase patched copy then passed the load/save-failure assertions; a targeted third test confirmed the disabled-button click semantics (Pitfall 2)
- npm registry, queried live 2026-09-18: `npm view jsdom version`, `npm view jsdom@28.1.0 engines`, `npm view jsdom time --json`, `npm view jsdom scripts.postinstall`, `npm view jsdom repository.url`
- `gsd-tools query package-legitimacy check --ecosystem npm jsdom`, run 2026-09-18

### Secondary (MEDIUM confidence)

- registry.npmjs.org/jsdom and github.com/jsdom/jsdom README content (JSDOM constructor options, `beforeParse`, `runScripts`, `resources`) — fetched via Exa search 2026-09-18
- github.com/nodejs/LTS release schedule table — fetched via Exa search 2026-09-18
- github.com/jsdom/jsdom PR #4033 ("Replace the resource loader API") and issue #1724 ("Implement the Fetch API") — fetched via Exa search 2026-09-18
- DefinitelyTyped `types/jsdom/index.d.ts` (`beforeParse` type signature) — fetched via Exa search 2026-09-18

### Tertiary (LOW confidence)

- Various StackOverflow/GitHub-issue community threads on `node --test` glob quoting behavior across Node versions (State of the Art table) — informative but not independently re-verified beyond this session's own empirical no-argument test, which is the recommendation actually made

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — versions confirmed live against the npm registry and mise's own remote listing, not training data
- Architecture: HIGH — the harness design was built and run, not just described
- Pitfalls: HIGH — all four pitfalls were reproduced empirically in this session, with exact error text captured
- Package legitimacy: HIGH confidence in the underlying signals (downloads, repo, no postinstall); MEDIUM confidence in the SUS verdict's actionability, since it reflects the `latest` tag rather than the specifically recommended `28.1.0` pin

**Research date:** 2026-09-18
**Valid until:** 30 days for the architecture/pattern findings (stable jsdom/Node APIs); 7 days for the specific version pins (`jsdom@28.1.0`, `node@24.21.0`) given jsdom's roughly monthly release cadence observed in its version history (27.x→28.x→29.x→30.x releases spaced ~1-2 months apart, Dec 2025–Sep 2026) — re-run the `npm view` verification commands at plan time if more than a week has passed.
