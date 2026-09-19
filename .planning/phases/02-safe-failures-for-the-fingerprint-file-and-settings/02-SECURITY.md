---
phase: "02"
slug: "safe-failures-for-the-fingerprint-file-and-settings"
status: verified
# threats_open = count of OPEN threats at or above workflow.security_block_on severity (the blocking gate)
threats_open: 0
asvs_level: 1
block_on: high
created: "2026-09-19"
---

# Phase 02 — Security

> Per-phase security contract: threat register, accepted risks, and audit trail.

The register comes from the `<threat_model>` blocks of `02-01-PLAN.md`, `02-02-PLAN.md`, and `02-03-PLAN.md`, so it was authored at plan time. Every entry was re-verified against the implementation as it stands after commits `1f47767` and `325855e`, which rewrote `configPage.html` outside this phase's plans.

---

## Trust Boundaries

| Boundary | Description | Data Crossing |
|----------|-------------|---------------|
| Jellyfin plugin API → the settings page script | A rejected request's error value crosses into the browser. It can carry the request URL, which is the configured Emby address, which can embed a user name and password. | Credentials inside a URL; the API key |
| Emby-controlled data → the settings page DOM | Emby user names reach `#EmbyAuthMigrationUsers` through `GET /EmbyAuth/Migration`. An Emby administrator chooses those names; this plugin does not. | Attacker-influenced text |
| The administrator's browser → the stored plugin configuration | A Save writes the four input values over the stored settings. After a failed load those inputs are empty. | The Emby URL and the API key |
| The npm registry → this repository's build and test runs | The first external JavaScript dependency this repository has ever had. | Third-party code |
| The fingerprint file on disk → `EmbyVerifiedPasswords` in memory | Untrusted bytes. The file can be damaged, truncated, locked by another process, or unreadable because of permissions. | SHA-256 fingerprints of password hashes |
| `EmbyVerifiedPasswords.Matches` → a move to the Default login method | A true answer authorises a move. `DefaultLoginMethod.MoveAsync` and the migration task both gate on it, and the Quick Connect path carries no password check of its own. | An authorization decision |
| A concurrent login → the shared cache field | Several logins can call `Record` and `Matches` at once. | Shared mutable state |

---

## Threat Register

| Threat ID | Category | Component | Severity | Disposition | Mitigation | Status |
|-----------|----------|-----------|----------|-------------|------------|--------|
| T-02-01 | Information Disclosure | the load-failure and save-failure messages | high | mitigate | All four failure messages are string literals (`configPage.html:102,124,139,159`). All four `.catch` handlers are **zero-arity** (`:101,123,138,158`), so the rejection value is not in lexical scope. Config values reach only input `.value` (`:117-120`). Sentinel tests reject with an `Error` embedding a `user:password@host` URL and an API key and assert both substrings absent (`configPage.test.js:96-110`, `:139-157`). | closed |
| T-02-02 | Tampering (reflected markup) | the message element and the migration list | medium | mitigate | No markup-assigning property anywhere in `src/` — `innerHTML`, `outerHTML`, `insertAdjacentHTML`, `document.write`, `DOMParser`, `createContextualFragment`, `srcdoc` all return zero hits. `showEmbyAuthMessage` builds nodes from a static tag, a static class, and a static attribute name and value (`configPage.html:76-84`); variable data enters only through `createTextNode`. Emby user names reach only `item.textContent` on a fresh `<li>` (`:98`). Literal-rendering test at `configPage.test.js:253-263`. | closed |
| T-02-03 | Tampering / Denial of Service | a Save after a failed load overwriting the stored Emby URL and API key with empty strings; a failed save leaving the administrator believing the settings were stored | high | mitigate | A failed load disables Save (`configPage.html:124-126`). Re-enable is reachable only from a successful load (`:121`) or the success continuation of a save (`:156`), which itself requires a prior successful load. The save-failure `.catch` deliberately does not re-enable (`:158-159`). One `.catch` covers both rejection paths because the update promise is returned from the `then` at `:154`; `.finally` hides the indicator (`:160-162`). Tests: `configPage.test.js:43-55`, `:57-68`, `:70-94`, `:112-122`, `:124-137`. | closed |
| T-02-04 | Information Disclosure | the installed dependency tree entering the repository | low | mitigate | `.gitignore:5` lists `node_modules/`. No tracked path matches `node_modules`; `git ls-files tests/js/` returns exactly four files. | closed |
| T-02-05 | Repudiation | documentation that overstates what the page guarantees | low | mitigate | `docs/settings.md:14` states the behaviour and its limit, and copies none of the four message strings, so the documentation cannot drift into describing a message that no longer exists. | closed |
| T-02-06 | Denial of Service / destruction of data | `Record`'s read-modify-rewrite after a failed read | high | mitigate | The read catch returns `null` and never assigns `_fingerprints` (`EmbyVerifiedPasswords.cs:116-120`); the only two assignments are on success paths (`:108`, `:114`). `Record` returns before the write when the read failed (`:47-51`). `UnreadableFile_KeepsItsRecords_WhenALoginIsRecorded` asserts the file's bytes are unchanged and no `.tmp` survives (`EmbyVerifiedPasswordsTests.cs:121-135`). A zero-byte or wrong-shape file throws `JsonException` and is treated as unreadable, not as empty-and-writable. | closed |
| T-02-07 | Elevation of Privilege | an unreadable file read as "this user has no record and needs none" | high | mitigate | `EmbyVerifiedPasswords.cs:88` — `Load() is { } fingerprints && …`, so a failed read yields `false` for every user. `EmbyLoginMethodUsers.cs:51` and `MoveToDefaultLoginMethod.cs:48` are the only callers of `Matches` in `src/`; there is no third path. Tests at `EmbyVerifiedPasswordsTests.cs:111-118`, `:127`, `:133`. | closed |
| T-02-08 | Tampering | a part-built cache seen by a concurrent caller after a failed read | medium | mitigate | `Record` (`:45`) and `Matches` (`:86`) are the only callers of `Load` and both hold the one lock; `Load`'s body (`:99-123`) takes no second lock, so the lock count is exactly two. The cache field is assigned only after a complete successful deserialize. `ConcurrentRecords_AreNotWritten_WhenTheFileIsUnreadable` runs fifty parallel `Record` calls (`EmbyVerifiedPasswordsTests.cs:156-166`). | closed |
| T-02-09 | Information Disclosure | the error the plugin logs on a failed read | low | mitigate | `LogReadFailed` (`EmbyVerifiedPasswords.cs:125`) names only `{FilePath}` and what the plugin does. The file stores fingerprints, never hashes (`:16`), guarded by `File_DoesNotContainThePasswordHash` (`EmbyVerifiedPasswordsTests.cs:93-99`). | closed |
| T-02-10 | Repudiation | documentation that overstates the guarantee | low | mitigate | `docs/how-it-works.md:48` states the read-failure behaviour and bounds it. The negative gate holds: `prevent`, `eliminat`, `guarantee`, `never lose`, `no data loss`, `cannot lose` return zero hits across `docs/` and `README.md`. | closed |
| T-02-SC | Tampering (supply chain) | the first npm install in this repository (`jsdom`) | high | mitigate | Exact pin with no range operator (`tests/js/package.json:6` → `28.1.0`). Lockfile committed, `lockfileVersion: 3`. Lockfile-enforcing verb `npm --prefix tests/js ci` (`.mise.toml:34`). Whole-tree check, not jsdom alone: all 47 lockfile packages carry `integrity` and **none** declares `hasInstallScript`. The `gate="blocking-human"` checkpoint (`02-01-PLAN.md:132`) was recorded approved with independently re-verified registry facts (`02-01-SUMMARY.md:39,128,143`). | closed |

*Status: open · closed · open — below high threshold (non-blocking)*
*Severity: critical > high > medium > low — only open threats at or above `workflow.security_block_on` count toward `threats_open`*
*Disposition: mitigate (implementation required) · accept (documented risk) · transfer (third-party)*

---

## Register staleness — wording corrected, properties unchanged

The register was written before `1f47767` and `325855e`. Three entries described mechanisms those commits changed. Each property was re-verified against the current code and holds; the mitigation text above is the corrected wording. Recorded so a future audit greps the right symbols rather than concluding a mitigation vanished.

1. **The settings message element moved.** The plans named `#EmbyAuthMigrationSummary` as the sink for settings failures. Settings failures now write to `#EmbyAuthSettingsStatus` (`configPage.html:55`, written at `:124` and `:159`); `#EmbyAuthMigrationSummary` (`:61`) carries migration status only.
2. **T-02-02's proof text was inverted by design.** The plan's evidence was "the message element has zero child elements". Every failure message now contains a `<span class="material-icons warning" aria-hidden="true">` (`configPage.html:77-82`), so that assertion had to go. Its replacement is strictly stronger: `messageChildren()` (`testHelpers.js:203-210`) enumerates every element child with its tag name, sorted class list, `aria-hidden` value, and own text, deep-equalled against `EXPECTED_ICON` (`configPage.test.js:26`), which pins `text: ''`. That catches an icon span which gained attacker text or an extra child; "zero children" would not. The zero-child form is retained at all five plain-message sites. Because the icon's own text is empty, `element.textContent` still equals the message exactly, so the T-02-01 sentinel assertions were not weakened.
3. **T-02-03's re-enable clause was incomplete.** "The button turns back on only after a load succeeds" no longer covers every path — a successful save also re-enables (`configPage.html:156`). The property holds, because reaching that line requires the button to have been enabled already, which requires a prior successful load with populated inputs.

**The central question, answered:** moving from direct `textContent` assignment to element construction did not re-open a markup-injection surface. Every constructed node uses a static tag, a static class, and a static attribute name and value; the only variable data enters through `createTextNode` or `textContent`, neither of which parses markup.

---

## Accepted Risks Log

No accepted risks.

---

## Observations

Recorded, not counted toward `threats_open`. None reaches the `high` block threshold.

### O-1 — the write-failure path contradicts two shipped statements

Assessed because `02-UAT.md` carries WR-01 forward. `Record` mutates the shared cache before it writes: `Load()` returns the `_fingerprints` field by reference (`EmbyVerifiedPasswords.cs:99-123`), and `Record` assigns into it at `:58`, before the write at `:62-63`. A write failure is caught at `:64-67`, leaving the cache holding a record the disk does not.

Reachability is real, not theoretical. `Record` is called during the login (`EmbyAuthenticationProvider.cs:106`); `MoveToDefaultLoginMethod.OnEvent` runs afterwards on `AuthenticationResultEventArgs` and consults `Matches` (`MoveToDefaultLoginMethod.cs:48`), which reads the already-mutated cache. So in `MoveAfterFirstLogin` mode **the user is moved to Default despite the write failure**.

Two shipped sentences say otherwise:

- `EmbyVerifiedPasswords.cs:128` — "The plugin does not move this user to the Default login method until the user logs in again through Emby."
- `docs/how-it-works.md:49` — "moves no affected user to Default until that user logs in through Emby again."

**This is not an Elevation of Privilege.** Emby genuinely verified that password moments earlier in the same process, so the move is correctly authorized; only the record's durability is lost, and behaviour is fail-closed again after a restart. T-02-07 is not widened. The defect is Repudiation, the same family as T-02-05 and T-02-10, both `low`.

The write-failure path is FPRT-01, assigned to Phase 3. Phase 3 should either correct those two sentences or move the cache mutation to after a successful write. Interactions checked and found absent: T-02-06 (a failed write targets `.tmp` and leaves the real file untouched, so no destruction path) and T-02-08 (both callers hold the single lock, so no partial state is observable). One minor note: the write catch does not delete a surviving `.tmp`, which holds the same class of data as the real file in the same directory — no new exposure.

### O-2 — dependency classification, no security effect

`tests/js/package.json:4` declares `jsdom` under `dependencies`, while `02-01-SUMMARY.md:20` calls it a devDependency. No property of T-02-SC changes: the package is `private: true`, used only by tests, and never ships — the release zip contains only the plugin DLL and `meta.json`.

### O-3 — residual, mitigation present

T-02-03's click path is tested through real disabled-control activation behaviour (`testHelpers.js:270-272` uses `.click()`, so the disabled check is exercised rather than bypassed). Implicit submission by pressing Enter in a text input has no test. Per the HTML Standard's implicit-submission algorithm a disabled default button suppresses it, and the page exposes no other submit trigger.

---

## Security Audit Trail

| Audit Date | Threats Total | Closed | Open | Run By |
|------------|---------------|--------|------|--------|
| 2026-09-19 | 11 | 11 | 0 | gsd-security-auditor (ASVS L1, block_on high) |

The short-circuit permitted at `threats_open: 0` with a plan-time register at ASVS L1 was **not** taken. The implementation had drifted from the register after two out-of-phase commits, so the auditor ran and verified every mitigation against the current code.

---

## Sign-Off

- [x] All threats have a disposition (mitigate / accept / transfer)
- [x] Accepted risks documented in Accepted Risks Log — none
- [x] `threats_open: 0` confirmed
