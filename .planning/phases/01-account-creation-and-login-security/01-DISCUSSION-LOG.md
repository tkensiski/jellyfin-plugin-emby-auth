# Phase 1: Account Creation and Login Security - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-09-17
**Phase:** 1-Account Creation and Login Security
**Areas discussed:** Double failure on create, Provider test seam, Hash save per login, PasswordFirst test swap

---

## Double failure on create

### Question 1 — How should the plugin create the account?

| Option | Description | Selected |
|--------|-------------|----------|
| Insert the row with the password | Write the account through `JellyfinDbContext` with the password and the Emby login method already set, in one `SaveChangesAsync`. No window, nothing to clean up. Copies Jellyfin's creation steps and skips `UserCreatedEventArgs`. | |
| Keep `CreateUserAsync` + cleanup | Keep `IUserManager.CreateUserAsync`, as PROJECT.md decided, and choose the cleanup for a failed save. | ✓ |
| Insert directly, publish the event | The same direct insert, then publish `UserCreatedEventArgs` through `IEventManager`. | |

**User's choice:** Keep `CreateUserAsync` + cleanup
**Notes:** The maintainer first asked whether the account could be created only after Emby accepts. It already is (`EmbyAuthenticationProvider.cs:95-108`); the account is created without a password, because `IUserManager.CreateUserAsync(string name)` takes only a name. Source read to answer it: `UserManager.cs:339-368`, `IUserManager.cs:90`, `EventingServiceCollectionExtensions.cs:64`, `UserCreatedLogger.cs:31-40`.

### Question 2 — Cleanup when the save fails (first pass)

| Option | Description | Selected |
|--------|-------------|----------|
| Delete, then move | `DeleteUserAsync`, and on its failure a single-column `ExecuteUpdateAsync` to the Emby login method. | |
| Move only, no delete | One single-column move to the Emby login method as the whole cleanup. | |
| Delete only, as today | Keep `DeleteUserAsync`, only widen the catch. | |

**User's choice:** None — the maintainer asked whether a blank password can open an account that has no password, and how Emby handles a failed password step.
**Notes:** Answered from source: an account without a password is refused on the Emby login method (`EmbyAuthenticationProvider.cs:62-66`) and opens with a blank password on the Default login method (`DefaultAuthenticationProvider.cs:62-68`). Emby uses the same two-request shape (`e2e/helpers.bash:91-100`), and its server is closed source. Jellyfin's own `POST /Users/New` performs no cleanup (`UserController.cs:520-533`).

### Question 3 — Cleanup order (second pass)

| Option | Description | Selected |
|--------|-------------|----------|
| Move first, then delete | Close the hole with the smallest write, then tidy up with the delete. | ✓ (later superseded) |
| Move only, no delete | One single-column move, nothing else. | |
| Delete only, as today | Keep the delete, widen the catch. | |

**User's choice:** Move first, then delete — then withdrawn in the next message.
**Notes:** The maintainer challenged the premise: "if the delete fails, why would we expect the update/move to succeed?" The answer given was that the two calls fail for different reasons (`DeleteUserAsync` takes the user lock, deletes several tables in one transaction, and publishes after the commit; `ExecuteUpdateAsync` is one statement with no lock and no `RowVersion` check), and that neither survives an unreachable database — though a blank-password login cannot complete then either (`UserManager.cs:672-675`).

### Question 4 — `CreateUserAsync` throws after the account exists

| Option | Description | Selected |
|--------|-------------|----------|
| Look up, move if unsafe | Find the account by name and move it to the Emby login method if it is on Default with no password. | |
| Look up, move, then delete | The same lookup, then the full cleanup. | |
| Refuse and log only | Catch every type, log at Error, refuse with `AuthenticationException`, no cleanup — what Jellyfin does. | ✓ |

**User's choice:** "Let's just enforce this the same way jellyfin does natively... All we are introducing is the auth type to point at emby", then: "if jellyfin fails to do the needful then it fails to do the needful... that's outside of our control."
**Notes:** This answer replaced the outcome of question 3 as well. The final decision (CONTEXT.md D-02, D-03) is: no compensating write anywhere, the existing best-effort delete stays, every failure is refused with `AuthenticationException` so no login returns HTTP 500. A follow-up question about how far the parity reaches was rejected as unnecessary.

---

## Provider test seam

### Question 1 — How the tests supply `IUserManager`

| Option | Description | Selected |
|--------|-------------|----------|
| Hand-written fake | `FakeUserManager` in `TestDoubles.cs` with the three used methods and `NotImplementedException` for the rest. No new dependency. | ✓ |
| NSubstitute 6.2.0 | The research pick; generates the fake, survives interface changes, adds the first mocking dependency. | |

**User's choice:** Hand-written fake
**Notes:** Matches the existing `TestDoubles.cs` pattern.

### Question 2 — How the tests set the settings

| Option | Description | Selected |
|--------|-------------|----------|
| Inject a settings source | The provider takes a settings source; the registration reads `EmbyAuthPlugin.Instance?.Configuration`. No static state in tests. | ✓ |
| Build the plugin in the test | Construct `EmbyAuthPlugin` with fakes, which sets the static `Instance`; tests must then share one xUnit collection. | |

**User's choice:** Inject a settings source

---

## Hash save per login

| Option | Description | Selected |
|--------|-------------|----------|
| Every login, as today | Each accepted login saves a fresh hash and records the fingerprint. The write cost is measured by PERF-02 in Phase 4. | ✓ |
| Only when it changes | Skip the writes when the saved hash already verifies the typed password. | |

**User's choice:** Every login, as today
**Notes:** The maintainer asked why the hash is needed at all. Answer: the Default login method checks `resolvedUser.Password` itself (`DefaultAuthenticationProvider.cs:76-80`), so the saved hash is what lets a migrated user keep the Emby password; and the Emby password can change while the user is still on the Emby login method, so the hash must follow the last password Emby accepted.

---

## PasswordFirst test swap

### Question 1 — Where the replacement e2e tests live

| Option | Description | Selected |
|--------|-------------|----------|
| Replace in place | Rewrite `30-migration-modes.bats:43` as a `KeepEmbyInCharge` test and `40-emby-outage.bats:43` as a refusal with a verified saved hash. Reuses existing Emby users. | ✓ |
| New file for AUTH-01 | One new file, for example `e2e/15-emby-decides.bats`, with new user names in `setup_suite.bash`. | |

**User's choice:** Replace in place

### Question 2 — An install that still holds the removed value

| Option | Description | Selected |
|--------|-------------|----------|
| Accept it, note it | Plain removal; Jellyfin replaces the unreadable settings file with defaults, so that install loses its Emby URL and API key. Noted in `CHANGELOG.md`. | ✓ |
| Read unknown values as a default | Tolerate an unknown migration behavior and fall back to the default, keeping the other settings. | |

**User's choice:** Accept it, note it
**Notes:** Verified at `MediaBrowser.Common/Plugins/BasePluginOfT.cs:184-198`. That `XmlSerializer` rejects an unknown enum value is expected but not yet verified — a flag for research.

---

## Claude's Discretion

- The shape of the settings source (a delegate or a small interface).
- Log message wording per failure path, without a password or the API key.
- Unit test class split and test names.
- The wording in `docs/how-it-works.md` for the moment when a new account has no password.

## Deferred Ideas

- Insert the account row directly with the password set, through `JellyfinDbContext`.
- Any compensating write after a failed save (single-column move, lookup by name, retry queue).
- Skip the hash and fingerprint write when the password did not change — PERF-02, Phase 4.
- Settings that survive an unknown stored value.
