# Phase 3: Migration Status and Target - Context

**Gathered:** 2026-09-19
**Status:** Ready for planning

<domain>
## Phase Boundary

This phase makes the Migration section report what the migration task and the fingerprint file actually do, turns the login method that users move to into a setting, and gives the migration code unit tests. It covers FPRT-01, FPRT-03, UI-03, MIGR-01, MIGR-02, AUTH-06, TEST-02, TEST-03, DOCS-01, and DOCS-05.

In scope: the `GET /EmbyAuth/Migration` response shape, the Migration section of the settings page, the write-failure path of `EmbyVerifiedPasswords`, the move-target settings and their validation, the rename of the move classes and the task, unit tests against an SQLite in-memory `JellyfinDbContext`, and an e2e test that migrates a user to JellyfinSecurity.

Out of scope: the Emby sign-out fix (AUTH-05, Phase 4), concurrent logins and invalid settings on a running server (TEST-05, TEST-06, Phase 4), the load test and every write-count optimization (PERF-01, PERF-02, Phase 4), and everything in Phases 5 and 6.

</domain>

<decisions>
## Implementation Decisions

### The migration response

- **D-01:** `MigrationUser` carries one state enum instead of the `ReadyToMove` boolean: `Ready`, `NeedsEmbyLogin`, `NoPassword`, `Unknown`. AUTH-06 adds a third real state, and today's boolean renders an account with no saved password and an account whose password Emby never verified identically. One field means one switch on the page. The record is public but the plugin has never been released, so no consumer breaks. — **Reversibility:** reversible — the record is public but unreleased; no installed consumer reads it.
- **D-02:** One change to `MigrationStatus` carries everything the Migration section needs: a top-level `RecordsUnavailable` flag for the read failure (FPRT-03), a task record (UI-03), and the per-user state (AUTH-06). This is the single response change that ROADMAP criterion 2 requires.
- **D-03:** The task record carries the task state, the progress, the last end time, and the last result. The last end time is not decoration: `POST /EmbyAuth/Migration/Run` calls `QueueIfNotRunning` and returns 204 at once, so there is a window where the task is queued and its state is still `Idle`. A page that stopped at the first `Idle` would render the old list. The page records when the POST returned and stops only when the task is `Idle` **and** the last run ended after that moment.
- **D-04:** The page polls `GET /EmbyAuth/Migration` every 2 seconds. No websocket: the `ScheduledTasksInfo` socket API has never been used in this repository and the jsdom harness would need a socket stub. Polling reuses the one endpoint and the existing `ApiClient.getJSON` stub.
- **D-05:** Polling follows a `Running` task for as long as it runs — the server is stating it is still working, and a migration of many accounts can outlast any fixed window. Only the start window is capped: if no run has begun after about 20 seconds, the page stops and shows a message pointing to the Jellyfin log. Each failure mode gets the treatment it needs instead of one number serving both.
- **D-06:** `setTimeout(loadEmbyAuthMigration, 3000)` at `configPage.html:137` is deleted. Phase 2 D-11 deliberately left it untested so this change churns no existing test.

### Failure reporting

- **D-07:** While the fingerprint file cannot be read, every user renders with state `Unknown` and the section shows the `RecordsUnavailable` message pointing to the Jellyfin log. The account names come from the database and stay accurate; only readiness is unknowable. Rendering `NeedsEmbyLogin` for everyone — which is what `Matches` returning `false` would otherwise produce — would state something untrue about every account at once.
- **D-08:** A failed fingerprint **write** stays in the Jellyfin log only, as FPRT-01 specifies. Nothing changes on the page: the in-memory record is real, those users genuinely are ready to move now, and the next successful write clears the condition. The log message at `EmbyVerifiedPasswords.cs:128`, the XML doc, and `docs/how-it-works.md:49` all currently say the opposite of what the code does and are corrected to say the record stays in memory until Jellyfin restarts.
- **D-09:** `Record()` keeps its current order — the cache assignment at `EmbyVerifiedPasswords.cs:58` stays before the write at `:62-63`. This resolves the STATE.md blocker in favour of correcting the sentences rather than moving the mutation. Locked before this discussion by PROJECT.md §Key Decisions, "Confirmed at the requirements review": Emby did verify that password moments earlier, so the rule holds and a disk error does not block the migration.

### The move-target settings

- **D-10:** Two settings, not one. `MigrationTarget` governs the after-login move and the migration task. `PasswordSetTarget` governs the move that happens when an administrator sets a password in Jellyfin (`EmbyAuthenticationProvider.cs:130-132`). `PasswordSetTarget` offers the same choices plus a first entry, "Same as the migration target", which is its default — so an existing install and an administrator who does not care both keep today's behavior with no thought. This contradicts MIGR-01 as written; see the required roadmap changes below. — **Reversibility:** costly — two settings properties, two dropdowns, and the validation they share; collapsing to one later means a settings change on every install.
- **D-11:** The target dropdown offers two fixed entries plus a dynamic list. Fixed: **Move to Default** (always present) and **Remain on Emby Login**. Dynamic: one entry per enabled login method that is neither Jellyfin's Default nor this plugin's own Emby method. Labels read "Move to {plugin name}". `EmbyAuthenticationProvider` reports `IsEnabled => true` and `Name => "Emby"` (`:47`, `:50`), so it does appear in Jellyfin's enabled list and must be filtered out.
- **D-12:** **Remain on Emby Login** means no path moves anyone — not the after-login move, not the migration task, not the password-set path when `PasswordSetTarget` resolves to it. There is no hidden fallback to Default. An administrator who wants a single hand-over while the migration target is "Remain" sets `PasswordSetTarget` to a real destination; that is why the second setting exists.
- **D-13:** The list of enabled login methods comes from the plugin's own API. `EmbyAuthController` takes `IUserManager`, calls `GetAuthenticationProviders()`, drops its own Emby method, and returns the remaining name and ID pairs. Filtering is server-side and unit-testable through the `FakeUserManager` that TEST-03 needs anyway (`TestDoubles.cs:220`). The page has one source for everything it renders.
- **D-14:** A target that Jellyfin does not report as enabled is refused at save time with a message. At run time a target that has since disappeared — because its plugin was removed — does **not** refuse logins. Emby still checks every password, which is the core value; only the move is skipped, with an Error log, and the Migration section reports it. A broken target strands users on the Emby method; it never locks them out. This deliberately differs from every other invalid setting, which refuses all logins (`EmbyAuthenticationProvider.cs:144-153`).
- **D-15:** `MigrationMode` and `MigrationTarget` both stay. `MigrationMode` answers *when* a move happens, `MigrationTarget` answers *where*. The settings page states the combination plainly, including that `MigrationMode` has no effect while the target is "Remain on Emby Login". `KeepEmbyInCharge` is not removed — a second breaking settings change in one milestone is not worth it, and it still covers "do not move on login, but do move when I run the task".

### The settings page layout

- **D-16:** The `MigrationTarget` dropdown lives in the **Migration section**, directly above **Run migration now** — not in the settings form. It is the destination picker for a manual run and the persisted setting at the same time; one control, one value, no two dropdowns that can disagree. The settings form keeps `PasswordSetTarget`, which is about login behavior rather than migration.
- **D-17:** Clicking **Run migration now** saves the picked destination first, then queues the task. Jellyfin's scheduled task API accepts no run parameters — `IScheduledTask.ExecuteAsync` takes only a progress reporter and a cancellation token, and `ITaskManager.QueueIfNotRunning<T>()` takes nothing — so a per-run destination would need out-of-band state or a duplicated move loop. Saving first needs neither, and a run started from Dashboard > Advanced > Scheduled Tasks behaves identically because there is only ever one target. Consequence to document: a one-off run to a different destination also changes where automatic after-login moves go from then on.
- **D-18:** The AUTH-06 no-password warning renders only while at least one account is in the `NoPassword` state, and its wording follows the configured target. When the target is Jellyfin's Default it makes the specific claim, verified at `DefaultAuthenticationProvider.cs:61-68`. When the target is any other method the plugin states that it cannot tell how that method treats an account with no password. When the target is "Remain on Emby Login" the warning does not apply at all, because no account reaches another method. The plugin never claims something it has not verified.

### Renaming

- **D-19:** The task `Key` changes from `EmbyAuthMoveUsersToDefault` to `EmbyAuthMigration`. The plugin has never been released, so only the maintainer's own servers hold anything keyed to it — the same reasoning Phase 1 used to delete `JellyfinPasswordFirst` outright (01-CONTEXT D-07). The task has no default triggers, so nothing meaningful is stored against the key. After v1.0.0 this becomes a change that breaks real installs. Three lines move: `MoveEmbyUsersToDefaultTask.cs:45` and `e2e/helpers.bash:165,167`. — **Reversibility:** one-way after release — the key is the identifier Jellyfin stores trigger configuration against; changing it post-v1.0.0 orphans that state on every install.
- **D-20:** Migration wording for the renames, reusing the vocabulary the settings page, the API, and the docs already use:
  - `DefaultLoginMethod` → `LoginMethodMove`
  - `MoveToDefaultLoginMethod` → `MoveAfterLogin`
  - `MoveEmbyUsersToDefaultTask` → `EmbyMigrationTask`
  - task `Name` "Move Emby users to the Default login method" → "Finish the Emby migration"
  - settings properties `MigrationTarget` and `PasswordSetTarget`

### Tests

- **D-21:** The criterion 7 e2e test uses **JellyfinSecurity v2.6.1**, the `-jf12` zip, pinned and verified against the published `sha256`. No test-only provider is built in this repository: the goal is proving that this plugin's migration works, not that JellyfinSecurity works.
- **D-22:** JellyfinSecurity is installed into the **shared** Jellyfin container that all e2e files already use, not an isolated one. Both plugins side by side is the production topology for a migration that ends on JellyfinSecurity, so that is what the suite should run. If JellyfinSecurity turns out not to be inert at defaults and disturbs Emby-method logins, that is a finding about a supported path, not a reason to isolate the test.
- **D-23:** The test runs in CI on every pull request, and its scope stops at the hand-over: an Emby login, the password saved, the migration to JellyfinSecurity, and that same password logging the user in without a challenge. That is the whole state an Emby migration leaves behind. Everything past it — 2FA enrolment, OIDC, passkeys — belongs to JellyfinSecurity and the test must not grow into it.
- **D-24:** `MoveAfterLogin` stops reading the static `EmbyAuthPlugin.Instance` (`MoveToDefaultLoginMethod.cs:34`) and takes the settings source that Phase 1 D-10 added for exactly this. Tests then set the settings with no static state.

### The manual-flip finding — closed

- **D-25:** The Phase 2 finding that an administrator can flip an Emby-method user with no password to Default by hand, leaving an account that a blank password opens (`DefaultAuthenticationProvider.cs:62`, v12.1), is **closed with no code in this phase**. Phase 2 verified that Jellyfin 12.1 offers no veto on that path — no cancellable event, no `IUserManager` validation hook, no interceptor route — so nothing can prevent it. The mitigation Phase 2 proposed, writing a random password on the account, was ruled out by AUTH-06's 2026-09-18 rewrite: the plugin "does not invent one" and "does not write a password the user cannot type". What remains is already covered: AUTH-06 warns on the settings page, exactly where an administrator stands before doing this, and DOCS-05 states the behavior in `docs/how-it-works.md`. Logging or flagging it would need new persistent state — `UserUpdatedEventArgs` carries no previous value — for a best-effort report about a deliberate administrator action on their own server. `02-CONTEXT.md` §Deferred "New finding — assigned to Phase 3" is closed by this decision.

### Claude's Discretion

- How the SQLite in-memory `JellyfinDbContext` is stood up for TEST-02 and TEST-03 — it needs an `IJellyfinDatabaseProvider`, an `IDbContextFactory<JellyfinDbContext>` double, and a pinned EF Core SQLite package. Confirm that EF Core's InMemory provider is genuinely unusable before adding the dependency, since `LoginMethodMove.MoveAsync` uses `ExecuteUpdateAsync`.
- The shape of the `ITaskManager` fake for TEST-03, following the hand-written-double precedent (01-CONTEXT D-09). No mocking library.
- Whether MIGR-02's visibility test guards only `EmbyAuthenticationProvider` or every type that must stay internal.
- Where JellyfinSecurity is installed in the e2e suite lifecycle, given its install needs a Jellyfin restart and `e2e/setup_suite.bash` already waits up to 180 seconds per server.
- Whether the task `Key` change earns a `CHANGELOG.md` entry, following Phase 1's precedent for a settings-visible change.
- Exact wording throughout, within the rule that no message repeats the Emby URL or the API key, and that the plugin never claims behavior it has not verified.
- Test file names and the split across files, following `.planning/codebase/TESTING.md`.

</decisions>

<roadmap_change_required>
## Required Changes to the Roadmap and Requirements

Make these before planning, so the plan is verified against criteria the plugin will actually meet.

1. **MIGR-01 says "setting" in the singular** and routes the password-set path through that same setting: "Every path that moves a user off the Emby login method uses this setting, including the move after a password that an administrator sets in Jellyfin." D-10 splits this into `MigrationTarget` and `PasswordSetTarget`. Reword MIGR-01 in `.planning/REQUIREMENTS.md` and ROADMAP criterion 6.
2. **Neither MIGR-01 nor criterion 6 anticipates a no-move target.** "Remain on Emby Login" (D-11, D-12) is a value of the setting that means no path moves anyone. Both documents need it stated.
3. **Criterion 6 says "class and task names".** D-19 also changes the task `Key`, which is an identifier rather than a name. State it so the criterion covers what the phase does.
4. **Criterion 7 needs its second login method named.** D-21 through D-23 fix it as JellyfinSecurity v2.6.1 `-jf12` in the shared e2e stack, with the test scoped to the hand-over only. Without this, criterion 7 is unimplementable as written — the e2e stack has no third login method.

</roadmap_change_required>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase requirements and prior decisions

- `.planning/ROADMAP.md` §Phase 3 — the goal, the ten success criteria, and the work order that puts the database test seam and the `ITaskManager` fake first.
- `.planning/REQUIREMENTS.md` — FPRT-01, FPRT-03, UI-03, MIGR-01, MIGR-02, AUTH-06, TEST-02, TEST-03, DOCS-01, DOCS-05.
- `.planning/PROJECT.md` §Core Value, §Constraints, §Key Decisions — the rule that no password Emby did not verify opens an account; the fingerprint write decision (D-09); the MIGR-01 and MIGR-02 rationale.
- `.planning/phases/02-safe-failures-for-the-fingerprint-file-and-settings/02-CONTEXT.md` — D-01 (why the fingerprint file stays, with the evidence), D-02 (`Load()` returns `null` only on a failed read), D-11 (no test asserts the 3-second `setTimeout`), and §Deferred "New finding", which D-25 closes.
- `.planning/phases/01-account-creation-and-login-security/01-CONTEXT.md` — D-09 (hand-written doubles, no mocking library), D-10 (the settings source seam that D-24 uses), D-07 (the precedent for a breaking change before release, which D-19 follows).
- `.planning/STATE.md` §Blockers/Concerns — the FPRT-01 stated-behavior mismatch, resolved by D-08 and D-09.

### Repository rules

- `.claude/rules/plugin.md` §Verified passwords — why every move checks `Matches`. §API and settings page — `textContent` never `innerHTML`, PascalCase response properties, every controller keeps an e2e 403 test. §Settings — messages never repeat the configured values.
- `.claude/rules/e2e.md` — Emby users are created in `setup_suite.bash`; each file uses its own names; `emby_login_requests` proves whether a password reached Emby.
- `CLAUDE.md` — test first then break the code once; `mise run e2e` for every change under `src/`; warnings are errors; one mise task per CI job; pin exact versions.

### Code this phase changes

- `src/Jellyfin.Plugin.EmbyAuth/Api/EmbyAuthController.cs:37-59` — the response D-01 to D-03 reshape; `:66,76` the public records; the constructor D-13 adds `IUserManager` to.
- `src/Jellyfin.Plugin.EmbyAuth/Configuration/configPage.html:59-66` — the Migration section D-16 adds the target dropdown to; `:87-104` `loadEmbyAuthMigration`; `:132-141` the Run handler D-06 and D-17 rewrite.
- `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs:41-70` — `Record()`, whose order D-09 keeps; `:128` the log message D-08 corrects.
- `src/Jellyfin.Plugin.EmbyAuth/DefaultLoginMethod.cs` — renamed by D-20; `:18` the const the settings replace.
- `src/Jellyfin.Plugin.EmbyAuth/MoveToDefaultLoginMethod.cs:34` — the static read D-24 removes.
- `src/Jellyfin.Plugin.EmbyAuth/MoveEmbyUsersToDefaultTask.cs:42-48` — the `Name`, `Key`, and `Description` D-19 and D-20 change.
- `src/Jellyfin.Plugin.EmbyAuth/EmbyLoginMethodUsers.cs:46-52` — where `ReadyToMove` becomes the D-01 state.
- `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs:47,50` — `IsEnabled` and `Name`, why D-11 must filter this method out; `:130-132` the password-set path `PasswordSetTarget` governs.
- `src/Jellyfin.Plugin.EmbyAuth/Configuration/PluginConfiguration.cs` — where D-10's two properties land.
- `e2e/helpers.bash:165,167` — the task key D-19 changes; `:134-139` the pre-created-account flow that is why the fingerprint file exists.

### Analysis and conventions

- `.planning/codebase/ARCHITECTURE.md` §Migration, §Migration API, §Architectural Constraints — the four entry points, the DI lifetimes, and the type-visibility rule MIGR-02 depends on.
- `.planning/codebase/TESTING.md` — unit test conventions, `TestDoubles.cs`, the e2e layout, and the pre-commit hook file patterns.
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs:220` — `FakeUserManager.GetAuthenticationProviders()`, today a `NotImplementedException`, which D-13 needs.

### External, verified during this discussion

- `https://github.com/ZL154/JellyfinSecurity/releases/tag/v2.6.0` — published 2026-09-11. Adds Jellyfin 12 support by building one source twice: .NET 9 for 10.11.x, .NET 10 for 12.x, both under one catalog entry. A manual install on Jellyfin 12 takes the `-jf12` zip.
- `https://api.github.com/repos/ZL154/JellyfinSecurity/releases` — v2.6.1 published 2026-09-14, `draft: false`, `prerelease: false`. Asset `Jellyfin.Plugin.TwoFactorAuthv2.6.1.0-jf12.zip`, 24,539,922 bytes, with `.md5` and `.sha256` sidecars, so a pinned download can be checksum-verified.
- `https://github.com/ZL154/JellyfinSecurity/issues/172` — the 10.x/12.x ABI split that v2.6.0 closed. Useful background for why the `-jf12` build exists and why the plain zip must not be used.

**Unverified, and the researcher must settle it:** whether JellyfinSecurity is inert when installed but never configured. Its v2.5.22 notes state that it gates `POST /Users/AuthenticateByName` and the deprecated `POST /Users/{userId}/Authenticate` to enforce its password-login switch, empty-password blocking, and lockout. D-22 accepts the risk deliberately, but the plan needs to know the answer before it wires the suite.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets

- `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs` — `FakeUserManager` already implements the whole `IUserManager` surface with `NotImplementedException` bodies, so D-13 only fills in `GetAuthenticationProviders()`. `CapturingLogger<T>` asserts the D-14 Error entry. The `ITaskManager` fake belongs beside them.
- `tests/js/testHelpers.js` — the `ApiClient` and `Dashboard` stubs from Phase 2, including `getConfigFailsFromCall`. The polling tests in D-04 and D-05 extend this rather than starting a new harness.
- `EmbyLoginMethodUsers.ListAsync` is already shared by the task and the controller, so the D-01 state is computed once and both callers get it.
- `showEmbyAuthMessage(element, text, isFailure)` (`configPage.html:75-85`) already renders the warning icon, so every new message in D-07, D-14, and D-18 reuses it.

### Established Patterns

- A narrow write uses `ExecuteUpdateAsync` on `JellyfinDbContext` (`DefaultLoginMethod.cs:34-39`), never a full-entity save — this is an explicit anti-pattern in `.planning/codebase/ARCHITECTURE.md`. The target ID becomes a parameter of that call; the write stays one column in one statement.
- Log methods are `[LoggerMessage]` partials at the end of each class, carrying names and status codes only.
- The page builds every user-derived string with `textContent` (`configPage.html:98`), never `innerHTML`.
- Jellyfin serializes response records with PascalCase property names, which the page reads directly (`status.Users`, `user.ReadyToMove`).
- `EmbyAuthController` is public and so are `MigrationStatus` and `MigrationUser`, because Jellyfin discovers scheduled tasks through `Assembly.GetExportedTypes()`. The D-01 state enum is therefore also public. `EmbyAuthenticationProvider` stays internal, which is MIGR-02.

### Integration Points

- `PluginServiceRegistrator.RegisterServices` — where `MoveAfterLogin` picks up the D-24 settings source.
- `EmbyAuthController`'s constructor gains `IUserManager` for D-13.
- `e2e/compose.yaml` and `e2e/setup_suite.bash` — where JellyfinSecurity is installed for D-21 and D-22. `scripts/dev-env.sh` reuses both files, so `up` and `down` must be re-checked after any change, per `.claude/rules/e2e.md:13`.
- `docs/settings.md` gains a row per new setting; `docs/migration.md` gains the shutdown step DOCS-01 points at, and the manual-run destination behavior from D-17; `docs/how-it-works.md` takes DOCS-05, the corrected write-failure sentence at `:49`, and the DOCS-01 pointer.

</code_context>

<specifics>
## Specific Ideas

- The maintainer's framing for the e2e test, in their own words: "The goal is user logins via emby, password gets set, migration to jellyfinsecurity takes place with just said password. What happens after the migration is done is all handled by jellyfinsecurity or another plugin for that matter." This is the scope line for D-23 — the test ends at the hand-over.
- On the target dropdown: "We just need to test migration to it works not that the jellyfin security plugin does what it says it does." This is why D-21 rejects building a test-only provider *and* rejects treating JellyfinSecurity as the system under test.
- On the shared e2e container: "Both EmbyAuth and Jellyfinsecurity plugins need to run side by side to allow a migration to end on Jellyfinsecurity." The shared stack is the production topology, not a compromise — which is why D-22 treats any interference as a finding rather than a reason to isolate.
- Dropdown labels read "Move to {plugin name}", the maintainer's wording, so "Move to Default" and "Move to JellyfinSecurity" sit beside "Remain on Emby Login" in one consistent form.
- **Standing objection, recorded not dismissed:** the maintainer has now pushed four times to remove the fingerprint file, most recently as "this is why I'd rather not even need this file". Phase 2 D-01 settled it on evidence, and this phase does not reopen it. The one thing that would retire the file is upstream: somewhere in Jellyfin to record who chose a password. Today there is nowhere — `User` carries 39 Jellyfin-owned columns, `Preference.Kind` is a sealed 13-member enum, and a plugin cannot add a table or a migration to `JellyfinDbContext`. Watch for that, and do not re-derive the rejected alternatives as if they were new.

</specifics>

<deferred>
## Deferred Ideas

- **Drop `MigrationMode.KeepEmbyInCharge`** — considered under D-15 and rejected for this milestone. A second breaking settings change after Phase 1 removed `JellyfinPasswordFirst` is not worth it, and the value still covers "do not move on login, but do move when I run the task". Revisit at a major version.
- **A one-shot run destination that does not change the saved setting** — considered under D-17 and rejected because Jellyfin's scheduled task API accepts no run parameters, so it needs shared out-of-band state with a defined answer for a stale stash. Revisit only if the saved-setting side effect proves confusing in use.
- **Greying out `MigrationMode` while the target is "Remain on Emby Login"** — considered under D-15. Clearer on screen, but a second piece of page state to cover; the page explains the combination in prose instead.
- **Logging or flagging the manual flip to another login method** — considered under D-25 and rejected for this phase. It needs new persistent state for a best-effort report about a deliberate administrator action. Revisit only if Jellyfin ever adds a veto on the login-method change, which is the only thing that would actually close the gap.
- **Retiring the fingerprint file** — see §Specific Ideas. Blocked upstream, not by this plugin.

</deferred>

---

*Phase: 3-Migration Status and Target*
*Context gathered: 2026-09-19*
