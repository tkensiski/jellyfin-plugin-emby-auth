---
phase: 01-account-creation-and-login-security
audited: 2026-09-17
auditor: gsd-security-auditor
mode: register_authored_at_plan_time
asvs_level: 1
block_on: high
threats_total: 23
threats_closed: 23
threats_open: 0
threats_open_non_blocking: 0
unregistered_flags: 0
verdict: SECURED
---

# Phase 01: Security Audit

**Phase:** 01 — Account Creation and Login Security
**Threats closed:** 23/23
**ASVS level:** 1 (grep depth: the declared mitigation must be present in the cited file)
**Block threshold:** high

Every threat in the four plan registers was verified against the code as it stands today, after commits `6f1dcde` and `d929b0a`. No verdict below rests on a SUMMARY.md claim alone. Every `mitigate` row cites a file and a line. Every `accept` row cites the decision that accepted it and the artifact that records the consequence.

## Threat Verification

Threat IDs repeat across the four plan registers, so each row below carries its plan number as a prefix.

| Threat ID | Category | Severity | Disposition | Status | Evidence |
|-----------|----------|----------|-------------|--------|----------|
| 01-01/T-01-DoS | Denial of Service | high | mitigate | CLOSED | `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs:158,174,189,210` — all four catches are unconditional `catch (Exception ex)`. `rg 'when \(ex is DbUpdateException or ResourceNotFoundException\)' src/` returns no match. The cleanup delete is guarded at `:184-192`. Every exit from `Authenticate` throws `AuthenticationException` (`:68,73,79,87,91,97,147,161,194,213`). Tests: `tests/.../EmbyAuthenticationProviderTests.cs:197,210,223,235,247,270`. |
| 01-01/T-01-Info | Information Disclosure | medium | mitigate | CLOSED | `EmbyAuthenticationProvider.cs:219-250` — every `[LoggerMessage]` template takes only `{Username}`, `{EmbyUserName}`, `{Status}`, `{AccountAccess}` or `{Problem}`; no template has a password or hash parameter. The client-facing message is the `InvalidLogin` constant (`:39`). The one other client-facing message is the settings problem (`:147`), and `EmbyAuthSettings.cs:41-73` never repeats a configured value; `:55-58` rejects a URL that carries credentials. Test: `EmbyAuthenticationProviderTests.cs:264-266`. End-to-end guard: `e2e/90-jellyfin-log.bats`. |
| 01-01/T-01-Race | Spoofing / Elevation of Privilege | medium | mitigate | CLOSED | The mitigation is "do not widen the window". `EmbyAuthenticationProvider.cs:156-172` holds only synchronous statements between `CreateUserAsync` and `UpdateUserAsync` (`:164-166`). The hash is computed before the account is created (`:100`). One delete call site (`:186`). Pinned by `EmbyAuthenticationProviderTests.cs:301-310`, which asserts the call sequence is exactly `["CreateUserAsync","UpdateUserAsync"]`. |
| 01-01/T-01-Double | Denial of Service (soft) | medium | accept | CLOSED | See Accepted Risks AR-1. The code implements the accepted behavior exactly: `EmbyAuthenticationProvider.cs:184-194` attempts the delete once, logs `LogDeleteFailed` at Error naming the account (`:240-241`), and still throws `AuthenticationException`. |
| 01-01/T-01-Fake | Tampering | low | mitigate | CLOSED | `rg 'FakeUserManager\|FakeCryptoProvider' src/` returns no match. The project reference is one-way: `tests/Jellyfin.Plugin.EmbyAuth.Tests/Jellyfin.Plugin.EmbyAuth.Tests.csproj:14` references `src/`, and `src/` references no test project. Production hashing stays on Jellyfin's `ICryptoProvider` (`EmbyAuthenticationProvider.cs:31,100,130`). |
| 01-01/T-01-SC | Tampering (supply chain) | n/a | accept | CLOSED | See Accepted Risks AR-3. `git diff --stat main...HEAD -- '*.csproj' 'Directory.*.props' '*.slnx'` is empty. The package set is unchanged: `Jellyfin.Controller 12.1.0`, `Jellyfin.Model 12.1.0`, `xunit.v3 4.0.1`. |
| 01-02/T-01-Bypass | Spoofing | high | mitigate | CLOSED | `Authenticate` has no branch that reads the saved hash. The Emby call at `EmbyAuthenticationProvider.cs:90` is unconditional, and the hash rewrite at `:100` and `:203` is unconditional. Test `AsksEmbyEveryTime_AndRewritesTheHash_EvenWhenTheSavedHashAlreadyMatches` (`EmbyAuthenticationProviderTests.cs:328-346`) asserts two `AuthenticateByName` requests and two `UpdateUserAsync` calls for two logins with the same password. |
| 01-02/T-01-Fingerprint | Elevation of Privilege | high | mitigate | CLOSED | `verifiedPasswords.Record` is called at exactly one place, `EmbyAuthenticationProvider.cs:106`, after the awaited `CreateAccountAsync` / `SavePasswordAsync`, both of which throw before returning when the save fails (`:194`, `:213`). Tests: `EmbyAuthenticationProviderTests.cs:349-374`, which read the file back through a fresh `EmbyVerifiedPasswords` instance. |
| 01-02/T-01-Race | Spoofing / Elevation of Privilege | medium | mitigate | CLOSED | Same code evidence as 01-01/T-01-Race, plus the honest documentation half at `docs/how-it-works.md:16-22`. |
| 01-02/T-01-Overclaim | Repudiation | medium | mitigate | CLOSED | `docs/how-it-works.md:16-22` names the window, its cause (`IUserManager.CreateUserAsync` takes only a name), the risk during it (a blank password on the Default login method would open the account), and both failure outcomes. The negative gate passes: `rg -i 'eliminat\|closes this window\|prevents this window\|cannot happen' docs/how-it-works.md` returns no match. The `## Limits` back-reference is at `:41`. |
| 01-02/T-01-Info | Information Disclosure | low | mitigate | CLOSED | No hash literal in the test file: `rg '"\$2[aby]?\$' tests/.../EmbyAuthenticationProviderTests.cs` returns no match. Expected hashes are derived by calling the fake hasher at `:265,288,333,371`. |
| 01-02/T-01-SC | Tampering (supply chain) | n/a | accept | CLOSED | See Accepted Risks AR-3. |
| 01-03/T-01-Bypass | Spoofing | high | mitigate | CLOSED | `rg -v '^\s*//' src/.../EmbyAuthenticationProvider.cs \| rg 'MigrationMode'` returns no match — the provider no longer branches on the migration behavior. `SavedPasswordMatches` and `LogSavedPasswordUnreadable` are gone. The repo-wide search over `src tests e2e docs README.md` returns no match. End-to-end proof: `e2e/40-emby-outage.bats:42-47` asserts HTTP 401 for `uma`, who holds an Emby-verified saved hash, while Emby is stopped. |
| 01-03/T-01-Stale | Tampering (indirect) | low | accept | CLOSED | See Accepted Risks AR-2. The rationale was re-checked, not assumed: `git tag -l` returns zero tags and `gh release list` is empty, so the plugin was never published and only the maintainer's own servers can hold the removed value. The consequence is recorded at `CHANGELOG.md:15-17`. |
| 01-03/T-01-Rename | Spoofing | high | mitigate | CLOSED | `PluginConfiguration.cs:9-20` holds exactly two members, `MoveAfterFirstLogin` and `KeepEmbyInCharge`, neither renamed and neither reordered. The provider holds no `MigrationMode` comparison at all, so there is no branch for a shim to hide in. There is no "accept the saved password while Emby is unreachable" path: `Authenticate` reaches `SavePasswordAsync` only after `EmbyClient.AuthenticateAsync` returns a login (`EmbyAuthenticationProvider.cs:90-91`). |
| 01-03/T-01-UIOnly | Repudiation | high | mitigate | CLOSED | The enum member itself is deleted, so `POST /Plugins/{id}/Configuration` cannot set the value either. Counted separately, as the plan's gate requires: `PluginConfiguration.cs` holds 2 `MigrationMode` members and 3 `AccountAccess` members; `configPage.html:25-35` holds 5 `option value=` elements in the same 2 + 3 split. The help text at `:28` names the second choice in the singular. |
| 01-03/T-01-Fake | Tampering | low | mitigate | CLOSED | Same evidence as 01-01/T-01-Fake. |
| 01-03/T-01-SC | Tampering (supply chain) | n/a | accept | CLOSED | See Accepted Risks AR-3. |
| 01-04/T-01-Stale | Repudiation | medium | mitigate | CLOSED (mitigation delivered by removal) | The documentation gate passes over `src tests e2e docs README.md`. `docs/settings.md:31-32` holds exactly two migration-behavior rows, whose labels match `configPage.html:25-26` word for word. `docs/how-it-works.md` login steps run 1 to 6 with no gap. The screenshot half of this mitigation was closed by deletion instead of by a retake: `docs/images/` does not exist, and `rg 'settings-page.png'` outside `.planning/` returns no match. A stale image cannot contradict the shipped page when no image ships. The change was directed by the maintainer at the 01-04 Task 3 checkpoint and is recorded in `01-04-SUMMARY.md`. |
| 01-04/T-01-Silent | Repudiation | medium | mitigate | CLOSED | `CHANGELOG.md:15-17` states that the affected server loses its Emby server URL and its Emby API key, that Jellyfin replaces the settings file with a default one, that logins on the Emby login method are refused until an administrator enters the values again, where to enter them, and that only servers that had selected that behavior are affected. |
| 01-04/T-01-KeyLeak | Information Disclosure | high | mitigate | CLOSED | The screenshot attack surface is gone: there is no `docs/images/` directory and no reference to the image outside `.planning/`. The `CHANGELOG.md` gate passes: `rg -i 'x-emby-token\|api[_ ]key[:=] ?[A-Za-z0-9]' CHANGELOG.md` returns no match. The file names the API key only as a thing an operator must re-enter; it carries no key, no token and no server URL. The demo environment is torn down and `git status --porcelain src tests e2e docs CHANGELOG.md` is empty, so no artifact from it reached the tree. |
| 01-04/T-01-Window | Repudiation | medium | mitigate | CLOSED | The account-creation-window text survived the step renumber: `docs/how-it-works.md:16-22` is intact, `rg 'CreateUserAsync' docs/how-it-works.md` matches at `:18`, the overclaim gate still passes, and the `## Limits` entry at `:41` still points at it. |
| 01-04/T-01-SC | Tampering (supply chain) | n/a | accept | CLOSED | See Accepted Risks AR-3. |

## Accepted Risks

**AR-1 — A half-made account can survive when Jellyfin's user store fails twice.** (01-01/T-01-Double, medium)
When `UpdateUserAsync` fails and the cleanup `DeleteUserAsync` also fails, the new account stays on the Default login method with no password, so a blank password would open it. Accepted per D-02 and D-03 (`01-CONTEXT.md:23-24`): the plugin does not build machinery to compensate for a failure inside Jellyfin's own user store, and Jellyfin's own `POST /Users/New` leaves the same half-made account. The login is still refused, the delete is attempted exactly once and never retried, and `LogDeleteFailed` records the account name at Error so an administrator can remove it or give it a password (`EmbyAuthenticationProvider.cs:184-194,240-241`). Disclosed to operators at `docs/how-it-works.md:22` and in `.claude/rules/plugin.md`.

**AR-2 — An install whose settings file still holds the removed migration-behavior value loses its settings.** (01-03/T-01-Stale, low)
Jellyfin cannot deserialize the stored value, so it builds a default configuration and saves it over the file (`MediaBrowser.Common/Plugins/BasePluginOfT.cs:184-198`, tag `v12.1`). That install loses its Emby server URL and its Emby API key and refuses every login on the Emby login method until an administrator enters them again. Accepted per D-07 (`01-CONTEXT.md:33`) rather than kept alive by a compatibility path, because a compatibility path would preserve the behavior that AUTH-01 and AUTH-02 exist to remove. The rationale was re-checked at audit time and still holds: the repository has zero tags and no releases, so the plugin was never published and only the maintainer's own servers can hold the value. Recorded at `CHANGELOG.md:15-17`. A human confirmed the one-way change at the 01-03 checkpoint.

**AR-3 — Supply chain: no legitimacy checkpoint in this phase.** (T-01-SC, all four plans, n/a)
The phase installed no package. Verified at audit time, not taken from the research note: `git diff --stat main...HEAD` over `*.csproj`, `Directory.*.props` and `*.slnx` is empty, and the package set is still `Jellyfin.Controller 12.1.0`, `Jellyfin.Model 12.1.0` and `xunit.v3 4.0.1`.

## Unregistered Flags

None. No `## Threat Flags` section appears in any of the four SUMMARY files, and the audit found no new attack surface that the register does not cover: no new package, no new network call, no new file write, no new API route and no new public type in `src/`.

## Observations

These sit outside the register. They are recorded so that a later phase can decide on them, not scored against this phase.

**O-1 — The prior review's residual gap is now closed, and the CHANGELOG sentence is accurate again.** `01-REVIEW.md` flagged that a `DbUpdateException` from `CreateUserAsync` itself would escape `Authenticate` as HTTP 500, which contradicted D-02's literal wording. Commit `6f1dcde` widened that catch: `EmbyAuthenticationProvider.cs:158` is now `catch (Exception ex)`, with tests at `EmbyAuthenticationProviderTests.cs:197` and `:210`. D-02 now holds as written, and `CHANGELOG.md:13` ("a failure while creating or saving an account") is no longer an overstatement.

**O-2 — What the fingerprint file stores is safe at rest, but the file is a capability.** `EmbyVerifiedPasswords.cs:87-88` stores `SHA-256` of the saved password hash as hex, never the password and never the hash. The pre-image is a high-entropy PBKDF2 hash string, so the file gives an attacker no offline-cracking advantage, and a fingerprint cannot be replayed as a credential. The residual property is integrity, not confidentiality: anyone who can write that file can pre-authorize a move to the Default login method for a hash of their choosing, because `Matches` is what every move path checks (`.claude/rules/plugin.md`, "Verified passwords"). That is the existing design, and `01-CONTEXT.md:13` puts fingerprint-file failures in Phase 2 and Phase 3. Recommend that the Phase 2 register carry it as an explicit integrity threat on the file.

**O-3 — The "only `AuthenticationException` escapes `Authenticate`" guarantee rests on reasoning past the `IUserManager` boundary, not on a catch.** The register scopes 01-01/T-01-DoS to the `IUserManager` exception surface, and that surface is fully closed. Two calls in `Authenticate` sit outside any try: `verifiedPasswords.Record` at `:106`, whose own catch admits only `IOException` and `UnauthorizedAccessException` (`EmbyVerifiedPasswords.cs:60`), and `cryptoProvider.CreatePasswordHash` at `:100`. I could not construct a reachable case for either — the realistic `File.WriteAllText` and `File.Move` failures are `IOException` subclasses or `UnauthorizedAccessException`, and serializing a `Dictionary<Guid,string>` does not throw. I also tested the response-parsing path directly on .NET 10 rather than assume it: `ReadFromJsonAsync` does not reject a non-JSON content type, so a hostile or wrong server at `EmbyServerUrl` yields `JsonException`, which `EmbyClient.IsUnreadable` already catches (`EmbyClient.cs:81,152-153`). Nothing here is scored. A one-line `try`/`catch` around the `Record` call would turn the guarantee from an argument into a control, if Phase 2 touches that file anyway.

---

*Audited: 2026-09-17 — ASVS level 1, block threshold `high`*
