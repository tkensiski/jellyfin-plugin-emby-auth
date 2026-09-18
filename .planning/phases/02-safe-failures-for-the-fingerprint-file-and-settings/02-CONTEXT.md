# Phase 2: Safe Failures for the Fingerprint File and Settings - Context

**Gathered:** 2026-09-17
**Status:** Ready for planning

<domain>
## Phase Boundary

This phase stops a failed read and a failed request from destroying saved data without a message. It covers FPRT-02, UI-01, UI-02, and TEST-04.

In scope: the read-failure path of `EmbyVerifiedPasswords`, the load and save failure paths of the settings page, and the first JavaScript test harness in this repository.

Out of scope: the fingerprint write failure (FPRT-01, Phase 3), the read failure shown on the settings page (FPRT-03, Phase 3), the migration list that follows the real task state (UI-03, Phase 3), the Emby sign-out fix (AUTH-05, Phase 4), and every write-count optimization (PERF-02, Phase 4).

</domain>

<decisions>
## Implementation Decisions

### The fingerprint file stays

- **D-01:** `EmbyVerifiedPasswords` keeps its JSON file in `PluginConfigurationsPath` (`PluginServiceRegistrator.cs:24`, `:34-36`). The maintainer challenged the file twice and both replacements were rejected on evidence:
  - **Derive the record from the database** ("a password present on the Emby login method must be one that Emby verified") is false for a pre-created account. `e2e/helpers.bash:134-139` creates a Jellyfin account, sets the password `NAME-jf-random`, and then assigns the Emby login method. All five e2e files use it, so this is the ordinary state of a pre-provisioned account, not an edge case. Under the derived rule `EmbyLoginMethodUsers.cs:51` would mark every such account ready and the migration task would move it to Default, which makes the administrator's placeholder a live credential for a user who never authenticated. Quick Connect is worse: `SessionManager.AuthenticateDirect` publishes the move event with no password check, so the fingerprint is the only gate on that path.
  - **A plugin-owned SQLite database** would remove the read-modify-rewrite pattern that makes FPRT-01 and FPRT-02 possible, but the plugin has no database today (no `DbContext`, no SQLite reference anywhere in `src/`). It would add a dependency and a second state file for a map of about one row per migrating user. Rejected as disproportionate, per the workspace rule to keep solutions simple outside homelab.
  - Provenance cannot be derived, only recorded, because the stored hash is byte-identical whether Emby verified it or an administrator typed it. Jellyfin has nowhere to record it: `User` carries 39 Jellyfin-owned columns, `Preference.Kind` is a sealed 13-member enum, and a plugin cannot add a table or a migration to `JellyfinDbContext`.
- **D-02:** `Load()` no longer caches a failed read. Today it assigns `_fingerprints = []` before reading (`EmbyVerifiedPasswords.cs:97`) and keeps that empty dictionary for the life of the process (`:92`, `:107-110`). Instead, a failed read leaves the cache unset, so the next `Matches` or `Record` reads the file again. A transient failure, such as a file lock at startup, then heals without a Jellyfin restart. The retry cost is bounded: the dictionary is cached only after a read succeeds. — **Reversibility:** reversible — the change is local to one private method.
- **D-03:** While the read fails, `Record()` writes nothing and keeps nothing, and `Matches()` returns `false` for every user. A login that Emby accepts during that window is not recorded, so that user shows as "needs one login while Emby runs" and does not migrate until the file is readable and the user logs in again. This is the strictest fail-closed reading: the plugin never asserts provenance that is not on disk.
- **D-04:** A `JsonException` and an `IOException` get the same treatment. D-02 and D-03 make the distinction unobservable, so the code does not split the catch. The existing catch list in `Load()` (`:107`) stays as it is.
- **D-05:** The regression test asserts both halves: after a failed read followed by a login that Emby accepts, `Matches` is `false` **and** the file on disk still holds its original bytes. The second assertion is what proves no silent reset happened; without it the test passes against the current broken code.

### Settings page failures

- **D-06:** When `getPluginConfiguration` fails on `pageshow`, the page shows a message and **disables the Save button**. The administrator cannot start a save that would write the blank inputs over the stored settings. The page is visibly broken instead of looking normal but empty. Today the `pageshow` chain has `.finally` but no `.catch` (`configPage.html:84-92`), and the submit handler re-fetches the configuration and copies the four blank inputs over it (`:110-113`).
- **D-07:** When `updatePluginConfiguration` fails, the page shows a message. Today that call has no `.catch` (`:114-116`).
- **D-08:** Both messages are written to an inline element on the page, the same pattern as the existing `#EmbyAuthMigrationSummary` (`:48`, `:63`). No `Dashboard.alert` and no toast: the inline pattern is already there, it is what the migration section uses, and it is read directly in a test. Message text never repeats the Emby URL or the API key, because the URL can carry credentials (`.claude/rules/plugin.md` §Settings).

### JavaScript test harness (TEST-04)

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

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase requirements and prior decisions

- `.planning/ROADMAP.md` §Phase 2 — the goal, the four success criteria, and the order that puts the test harness before the page changes.
- `.planning/REQUIREMENTS.md` — FPRT-02, UI-01, UI-02, TEST-04.
- `.planning/PROJECT.md` §Core Value, §Constraints — the rule that no password Emby did not verify opens an account.
- `.planning/phases/01-account-creation-and-login-security/01-CONTEXT.md` — D-08 keeps a fingerprint write on every accepted login, D-09 sets the hand-written-double precedent, D-10 adds the settings source seam.

### Repository rules

- `.claude/rules/plugin.md` §Verified passwords — why every move to Default checks `Matches`. §API and settings page — `textContent` never `innerHTML`, PascalCase response properties. §Settings — messages never repeat the configured values.
- `.claude/rules/e2e.md` — Emby users are created in `setup_suite.bash`; each file uses its own names.
- `CLAUDE.md` — test first then break the code once; `mise run e2e` for every change under `src/`; warnings are errors; one mise task per CI job.

### Code and analysis

- `.planning/codebase/CONCERNS.md:38-45` — the read-failure data loss, with its confirmed impact. `:58-64` — the settings page error handling. `:106` — the untested page JavaScript.
- `.planning/codebase/TESTING.md` — unit test conventions, `TestDoubles.cs`, the e2e layout, and the pre-commit hook file patterns.
- `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs:90-113` — `Load()`, the method D-02 changes.
- `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html:57-122` — the inline script under test.
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs:101-108` — `UnreadableFile_MatchesNothing_AndLogsAnError`, the existing read test that D-05 extends.

### Jellyfin source verified during this discussion (tag `v12.1`)

- `Jellyfin.Server.Implementations/Users/DefaultAuthenticationProvider.cs:48-68` — a blank password opens an account whose stored `Password` is null or empty (`:62`). Confirmed present in v12.1.
- `Jellyfin.Server.Implementations/Users/UserManager.cs:848-927` — `UpdatePolicyAsync`. It writes `AuthenticationProviderId` with no validation (`:874`), commits (`:920-921`), then publishes `UserUpdatedEventArgs` (`:925-927`).
- `Jellyfin.Server.Implementations/Users/UserManager.cs:601-611` — a successful login rewrites `AuthenticationProviderId` through `ExecuteUpdateAsync` and publishes **no event**.
- `Jellyfin.Server.Implementations/Events/EventManager.cs:43-63` — a consumer exception is caught and logged (`:58-61`) and never reaches the caller.
- `Jellyfin.Data/Events/Users/UserUpdatedEventArgs.cs:8-17` and `Jellyfin.Data/Events/GenericEventArgs.cs:9-25` — the event carries the resulting `User` only. No changed-field set and no previous value, and `RenameUser` publishes the same type.
- `MediaBrowser.Model/Users/UserPolicy.cs:186-188` — `AuthenticationProviderId` is validated only as a non-empty string.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets

- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs` — the file already writes a real temporary file per test with a GUID name and cleans up in `Dispose`, including the `.tmp` file. `UnreadableFile_MatchesNothing_AndLogsAnError` writes `not json` to force the failure, which is the setup D-05 needs.
- `CapturingLogger<T>` in `TestDoubles.cs` — used to assert an `Error:` entry exists after a failed read.
- `#EmbyAuthMigrationSummary` (`configPage.html:48`) — the inline message element that D-08 follows.

### Established Patterns

- `Record()` skips the write when the fingerprint is unchanged (`EmbyVerifiedPasswords.cs:48-51`), so a repeat login with the same password writes nothing. The file holds one entry per user and is replaced on a password change; it is not append-only.
- The file is written through a temporary file and `File.Move(..., overwrite: true)` (`:56-58`). The write is atomic; the defect is that a failed read allows the write at all.
- The page builds content from user names with `textContent` (`:73`), never `innerHTML`.
- Log methods are `[LoggerMessage]` partials at the end of the class.

### Integration Points

- `.mise.toml` gains the `node` pin and one line in `[tasks.test]`. `.github/workflows/ci.yml` needs **no** change.
- `.pre-commit-config.yaml` already lists `.html` in the `test` hook file pattern, so the new tests run on a settings page edit with no change.
- A new `tests/js/` directory, and a `package.json` for the jsdom dependency.
- `EmbyVerifiedPasswords.Load()` is the only method D-02 changes. `Record` and `Matches` call it under the same `Lock`.

</code_context>

<specifics>
## Specific Ideas

- The maintainer pushed twice to remove the fingerprint file, and the discussion resolved it on evidence rather than on preference. Record the reasoning, not only the outcome: the file records **who chose the password**, which the hash cannot carry, and the pre-created-account flow in `e2e/helpers.bash:134-139` is the case that breaks every derived rule.
- The maintainer's instinct that "no password on the Emby login method means not yet synced" is correct and unconditional. Only the positive direction fails.
- The harness choice was delegated with "simplest, easiest path". D-09 and D-10 were chosen on that basis: no new CI job, no build step, no extraction step, and the test runs the shipping file.

</specifics>

<deferred>
## Deferred Ideas

- **Clear the password when a user is assigned to the Emby login method** — the maintainer's proposal to make "password present on the Emby method" mean "the plugin wrote it". The hook exists (`IEventConsumer<UserUpdatedEventArgs>` fires from `UpdatePolicyAsync`, outside the user lock), but it is best-effort and cannot carry the invariant: the row commits before the event publishes, a consumer exception is swallowed while the administrator still sees HTTP 204, and `UserManager.cs:601-611` rewrites the same column with no event at all. Worth revisiting as defense in depth, never as a replacement for the record.
- **Refuse a move to the Default login method when the user has no password** — the maintainer's second rule. **Not possible in Jellyfin 12.1.** No veto exists on that path: no cancellable event, no `IUserManager` validation hook, no route for a plugin-supplied EF Core interceptor, no extension seam in the controller or manager guards, and no `IAuthenticationProvider` member consulted at assignment. It would need an upstream Jellyfin change. Do not design or document it as a block.
- **A plugin-owned SQLite store for the fingerprints** — would remove the read-modify-rewrite pattern that makes FPRT-01 and FPRT-02 possible. Rejected for v1 as disproportionate (D-01). Revisit only if the record ever grows beyond one row per user.
- **A password field on a plugin-owned "move to Default" action** — considered and dropped. `EmbyAuthenticationProvider.ChangePassword` already gives the safe route: setting a password on an Emby-method user saves the hash and moves the user to Default in one step (`EmbyAuthenticationProvider.cs:130-132`), so an administrator never needs the login-method dropdown.

### New finding — assigned to Phase 3, not Phase 2

**A manual flip to the Default login method can leave an open account, and nothing guards it today.** If an administrator changes `AuthenticationProviderId` to Default for a user on the Emby login method who has no password, that account is then opened by a blank password (`DefaultAuthenticationProvider.cs:62`, confirmed in v12.1). The fingerprint file does not cover this path — it gates only the plugin's own moves. The plugin cannot refuse the change, because no veto exists (see the deferred item above).

**Mitigation under evaluation:** the plugin observes the flip through `IEventConsumer<UserUpdatedEventArgs>` and sets a random high-entropy password on the account, so it cannot be opened with a blank password. The administrator's remedy is the normal "set a password" flow. This direction is reachable only through `UpdatePolicyAsync`, which does publish the event — `GetAuthenticationProviders(user)` filters to the user's assigned provider (`UserManager.cs:994-997`), so the eventless rewrite at `:604-611` cannot produce an Emby-to-Default flip. Known limits: the row commits before the event publishes (`:920-921` then `:925-927`), so the exposure shrinks from permanent to a short window rather than closing; a consumer exception is swallowed, so the write needs an Error log; and `UserUpdatedEventArgs` carries no previous value, so the plugin needs its own record of which users were on the Emby method to avoid clobbering an account that was always on Default and deliberately passwordless — a state Jellyfin supports.

**Decision:** the maintainer assigned this to **Phase 3**, after a research pass closes the open questions (shadow-state lifetime, the startup window, which write route to use, and what the mitigation does not cover). `ROADMAP.md` §Phase 3 and `REQUIREMENTS.md` take a new requirement once that research lands. It is not Phase 2 work and does not block Phase 2 planning.

</deferred>

---

*Phase: 2-Safe Failures for the Fingerprint File and Settings*
*Context gathered: 2026-09-17*
