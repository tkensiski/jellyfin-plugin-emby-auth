# Phase 1: Account Creation and Login Security - Context

**Gathered:** 2026-09-17
**Status:** Ready for planning

<domain>
## Phase Boundary

This phase makes Emby the only judge of a login on the Emby login method, and it makes every account creation failure end in a refused login instead of an HTTP 500. It covers AUTH-04, AUTH-03, AUTH-01, AUTH-02, and TEST-01.

In scope: the failure paths of `EmbyAuthenticationProvider.CreateAccountAsync` and `SavePasswordAsync`, the removal of `JellyfinPasswordFirst`, the first unit tests for `EmbyAuthenticationProvider`, and the documentation of the moment when a new account has no password.

Out of scope: the Emby sign-out fix (AUTH-05, Phase 4), the fingerprint file failures (Phase 2 and Phase 3), the settings page (Phase 2), concurrent logins and the load test (Phase 4), and every write-count optimization (PERF-02, Phase 4).

</domain>

<decisions>
## Implementation Decisions

### Account creation and failure handling

- **D-01:** Account creation keeps Jellyfin's own steps: `IUserManager.CreateUserAsync(name)`, then the Emby-verified hash and `EmbyAuthenticationProvider.ProviderId` in the `UpdateUserAsync` call directly after it. The plugin adds only the login method and the hash. The maintainer rejected an insert of the account row through `JellyfinDbContext`, which would have removed the moment without a password but would also copy Jellyfin's creation steps (`InternalId`, default permissions, the user name rule at `UserManager.cs:963`) and skip `UserCreatedEventArgs`.
- **D-02:** The plugin never compensates for a failure inside Jellyfin's user store. Every exception type from `CreateUserAsync` and from `UpdateUserAsync` is caught, logged at Error, and refused with `AuthenticationException`, so no login returns HTTP 500. There is no account lookup by name, no single-column move to the Emby login method, and no retry. Jellyfin's own `POST /Users/New` also leaves a half-made account (`Jellyfin.Api/Controllers/UserController.cs:520-533`, tag `v12.1`), and a failure in Jellyfin's store is outside the plugin's control. — **Reversibility:** reversible — the compensating writes were never written; adding one later touches one method.
- **D-03:** The existing best-effort delete of the new account stays (`EmbyAuthenticationProvider.cs:197-203`). A failure of `DeleteUserAsync` is caught and logged; it never escapes and never triggers a second cleanup attempt.
- **D-04:** The Error log for a failed save or a failed delete names the account, so an administrator can find a half-made account. It never contains the password or the API key.

### Removal of `JellyfinPasswordFirst`

- **D-05:** `MigrationMode.JellyfinPasswordFirst` and the saved-password branch (`EmbyAuthenticationProvider.cs:80-86`) are deleted, with the settings page option, the `docs/settings.md` row, the `docs/how-it-works.md` step 2, and the test that uses the value. No compatibility path and no renamed replacement. — **Reversibility:** one-way — the value disappears from the settings contract; an install that stored it loses its settings (see D-07).
- **D-06:** The two end-to-end tests that use the value are rewritten in place, not moved to a new file, so they reuse the Emby users that `e2e/setup_suite.bash` already creates:
  - `e2e/30-migration-modes.bats:43` becomes a `KeepEmbyInCharge` test: after the password of the Emby user changes, the old password is refused at once, the new password is accepted, and the saved hash follows the new password.
  - `e2e/40-emby-outage.bats:43` becomes: while Emby is unreachable, the user with a verified saved hash (`uma`) is refused on the Emby login method.
- **D-07:** An install that still holds `MigrationMode = JellyfinPasswordFirst` in its settings file is accepted as broken by the change. Jellyfin catches a failed deserialization, builds a default configuration, and saves it over the file (`MediaBrowser.Common/Plugins/BasePluginOfT.cs:184-198`, tag `v12.1`), so that install loses the Emby URL and the API key and refuses every login until an administrator types them again. `CHANGELOG.md` states this. The plugin was never public, so only the maintainer's own servers can be affected.

### Hash and fingerprint writes

- **D-08:** Every login that Emby accepts keeps saving a fresh hash on the account and recording its fingerprint. No branch that skips the write when the saved hash already verifies the typed password. The write cost belongs to PERF-02 in Phase 4, which measures it first.

### Unit test seams (TEST-01)

- **D-09:** `IUserManager` is replaced by a hand-written `FakeUserManager` in `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs`. It implements `CreateUserAsync`, `UpdateUserAsync`, and `DeleteUserAsync`, throws `NotImplementedException` for every other member, and can fail each method with a chosen exception type. No mocking library: the repository has none, and `.planning/research/STACK.md:15` recommends NSubstitute only to avoid maintaining such a fake. — **Reversibility:** reversible — a mocking library can replace the fake later.
- **D-10:** `EmbyAuthenticationProvider` stops reading the static `EmbyAuthPlugin.Instance` (`EmbyAuthenticationProvider.cs:146`). It takes a settings source in its constructor, and `PluginServiceRegistrator` passes a source that reads `EmbyAuthPlugin.Instance?.Configuration`. Tests then set the settings with no static state, so parallel test classes cannot affect each other. `MoveToDefaultLoginMethod.cs:34` reads the same static and can use the seam when Phase 3 tests that class. — **Reversibility:** costly — the constructor signature and the service registration change together.
- **D-11:** `ICryptoProvider` also needs a test double, because the Jellyfin implementation is not in the `Jellyfin.Controller` package. Keep it in `TestDoubles.cs` beside the others.

### Claude's Discretion

- The exact shape of the settings source in D-10 (`Func<PluginConfiguration?>` or a small interface).
- Log message wording for each failure path, within the rule that no message holds a password or the API key.
- The split of the unit tests over test classes and the test names, following the naming pattern in `.planning/codebase/TESTING.md`.
- The wording in `docs/how-it-works.md` for the moment when a new account has no password, as long as it states the risk plainly and names the cause (Jellyfin has no create-with-password call).

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase requirements and prior decisions
- `.planning/ROADMAP.md` §Phase 1 — the goal, the five success criteria, and the work order.
- `.planning/REQUIREMENTS.md` — AUTH-01, AUTH-02, AUTH-03, AUTH-04, TEST-01.
- `.planning/PROJECT.md` §Constraints, §Key Decisions — the password rule, and the decision to keep Jellyfin's create-then-save pattern.

### Repository rules
- `.claude/rules/plugin.md` — Jellyfin behavior the plugin depends on: only `AuthenticationException` is caught (line 15), `IUserManager` is resolved per login (line 16), blank passwords and the Default login method (lines 32-36), the fingerprint check before any move (line 28).
- `.claude/rules/e2e.md` — Emby users are created in `setup_suite.bash`, each file uses its own names, and `emby_login_requests` proves whether a password reached Emby.
- `CLAUDE.md` — test first, then break the code once; `mise run e2e` for every change under `src/`; warnings are errors.

### Code and analysis
- `.planning/codebase/CONCERNS.md` §Fragile Areas — the account creation window and its failure paths.
- `.planning/codebase/ARCHITECTURE.md` §Data Flow, §Anti-Patterns — the login path and the four rules this phase must not break.
- `.planning/codebase/TESTING.md` — the unit test conventions, `TestDoubles.cs`, and the e2e file layout.
- `.planning/research/ARCHITECTURE.md:15-52` — Change 1, with the Jellyfin v12.1 line references for the account creation window.
- `.planning/research/PITFALLS.md:175-185` — why a wider full-entity save must not replace the narrow writes.
- `.planning/research/STACK.md:15` — the NSubstitute recommendation that D-09 rejects, with its reason.

### Jellyfin source read during this discussion (tag `v12.1`)
- `Jellyfin.Server.Implementations/Users/UserManager.cs:339-368` — `CreateUserAsync` commits the account (`:362`), then publishes `UserCreatedEventArgs` (`:365`).
- `Jellyfin.Server.Implementations/Users/UserManager.cs:371-416` — `DeleteUserAsync` takes the user lock, deletes in one transaction, and publishes after the commit (`:415`).
- `Jellyfin.Server.Implementations/Users/UserManager.cs:536-708` — `AuthenticateUser`: the `Guid.Empty` lock (`:550`), the disabled check (`:623`), and the `ExecuteUpdateAsync` writes that a successful login needs (`:658-675`).
- `Jellyfin.Server.Implementations/Users/DefaultAuthenticationProvider.cs:56-80` — a blank password opens an account whose saved password is empty (`:62-68`).
- `Jellyfin.Api/Controllers/UserController.cs:520-533` — Jellyfin's own create-then-set-password, with no cleanup on failure.
- `Jellyfin.Server.Implementations/Events/EventManager.cs:43-63` — consumer errors are caught, but the consumers are built outside the `try` (`:52`).
- `MediaBrowser.Common/Plugins/BasePluginOfT.cs:184-198` — a settings file that cannot be read is replaced by a default one.
- `MediaBrowser.Controller/Library/IUserManager.cs:72-176` — the interface has no single-column write.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs` — `StubHttpMessageHandler`, `StubHttpClientFactory`, `ManualTimeProvider`, and `CapturingLogger<T>`. The new `FakeUserManager` and the crypto double belong here.
- `EmbyUserDirectoryTests.cs` — the pattern for a test that uses real collaborators (`EmbyClient`) over the HTTP stub. The provider tests can use real `EmbyClient`, `EmbyUserDirectory`, and `EmbyVerifiedPasswords` with a temporary file, and fake only `IUserManager` and `ICryptoProvider`.
- `e2e/helpers.bash` — `login_status`, `set_password`, `policy_field`, `emby_login_requests`, and `run_migration_task` cover everything the rewritten e2e tests need.

### Established Patterns
- Every refusal throws `AuthenticationException(InvalidLogin)`; only invalid settings use a different message (`EmbyAuthenticationProvider.cs:36`, `:152`).
- Log methods are `[LoggerMessage]` partials at the end of the class, with names and status codes only.
- A narrow write uses `ExecuteUpdateAsync` on `JellyfinDbContext` (`DefaultLoginMethod.cs:31`). This phase adds no such write, because D-02 removes the compensating cleanup.
- The plugin resolves `IUserManager` from `IServiceProvider` on each login, never in the constructor.

### Integration Points
- `PluginServiceRegistrator.RegisterServices` gains the settings source for D-10.
- `Configuration/PluginConfiguration.cs`, `Configuration/configPage.html:27`, `docs/settings.md:33`, and `docs/how-it-works.md:8` all hold `JellyfinPasswordFirst` text that D-05 removes.
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthSettingsTests.cs:37-40` uses the value and needs a different enum member.

</code_context>

<specifics>
## Specific Ideas

- The maintainer's rule for this phase: do what Jellyfin does. A failure inside Jellyfin's own user store is outside the plugin's control, and the plugin does not build machinery to compensate for it.
- The one open question the maintainer asked twice, answered in the code: an account without a password is safe on the Emby login method (the plugin refuses a blank password at `EmbyAuthenticationProvider.cs:62-66`) and open on the Default login method (`DefaultAuthenticationProvider.cs:62-68`). Keep this distinction in the documentation wording.

</specifics>

<deferred>
## Deferred Ideas

- **Insert the account row directly, with the password set, through `JellyfinDbContext`** — removes the moment without a password completely. Rejected for v1: it copies Jellyfin's creation steps and skips `UserCreatedEventArgs`. Revisit only if Jellyfin adds a create-with-password call.
- **A compensating write when the save fails** (single-column move to the Emby login method, an account lookup by name, or a retry queue) — considered and rejected by D-02. Recorded so that a later phase does not rediscover it as new.
- **Skip the hash and fingerprint write when the password did not change** — belongs to PERF-02 in Phase 4, after the load test measures the cost.
- **Settings that survive an unknown stored value** — rejected by D-07; it would keep a compatibility path that AUTH-02 forbids.

</deferred>

<roadmap_change_required>
## Required Change to the Roadmap

Phase 1 success criterion 1 in `.planning/ROADMAP.md:29` states: "In each case the login is refused without HTTP 500, and no enabled account stays on the Default login method without a password." D-02 and D-03 mean the plugin no longer promises the second half when Jellyfin's delete fails. The criterion, `.claude/rules/plugin.md:33-36`, and the PROJECT.md constraint about `CreateUserAsync` need the same change:

- What stays: every failure path refuses the login and never returns HTTP 500, and the plugin always attempts the delete.
- What changes: when Jellyfin cannot save and cannot delete, the account stays on the Default login method without a password. The plugin logs the account name at Error. This matches Jellyfin's own `POST /Users/New`.

Make this change before planning, so the plan is verified against the criterion the plugin actually meets.

</roadmap_change_required>

---

*Phase: 1-Account Creation and Login Security*
*Context gathered: 2026-09-17*
