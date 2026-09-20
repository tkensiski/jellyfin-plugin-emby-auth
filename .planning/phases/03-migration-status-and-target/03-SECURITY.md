---
phase: 03-migration-status-and-target
audited: 2026-09-20
auditor: gsd-security-auditor
mode: register_authored_at_plan_time
asvs_level: 1
block_on: high
threats_total: 16
threats_closed: 16
threats_open: 0
threats_open_non_blocking: 0
unregistered_flags: 0
verdict: SECURED
---

# Phase 03: Security Audit

**Phase:** 03 — Migration Status and Target
**Threats closed:** 16/16
**ASVS level:** 1 (grep depth: the declared mitigation must be present in the cited file), raised to trace depth for T-03-03, T-03-04, T-03-08, and T-03-11

Every distinct threat id in the seven plan registers resolves to CLOSED. A threat id that recurs across plans was treated as one threat carrying several mitigation claims, and each claim was checked on its own.

---

## Trust Boundaries

| Boundary | Description | Data Crossing |
|----------|-------------|---------------|
| Settings page to plugin API | An administrator's browser calls `GET /EmbyAuth/Migration` and `POST /EmbyAuth/Migration/Run` | Account names, per-account migration state, task state, login-method names and ids |
| Jellyfin configuration API to plugin | Any caller with an administrator token posts plugin settings | The migration target and the password-set target |
| Plugin to Jellyfin user store | The move writes one column on one account | A login-method id |
| Plugin to Emby | Password check and user list | A password, the API key |
| Build to e2e container | A third-party plugin archive is fetched and mounted | A release asset over the network |

---

## Threat Register

| Threat ID | Category | Severity | Disposition | Status | Evidence |
|-----------|----------|----------|-------------|--------|----------|
| T-03-01 | Tampering / Elevation of Privilege | high | mitigate | CLOSED | Save-time refusal at the one save chokepoint: `EmbyAuthPlugin.cs:76-88` throws before `base.UpdateConfiguration`, so a refused save writes nothing; `MigrationTargetValidation.cs:25-63`. Shape gate `EmbyAuthSettings.cs:83-91`. Tests `MigrationTargetValidationTests.cs:26-107`; e2e `e2e/50-migration-target.bats:86-98` asserts both refusals and that the prior settings survive. |
| T-03-02 | Tampering | medium | mitigate | CLOSED | Server-side filter `Api/EmbyAuthController.cs:55-57`; the validator refuses this plugin's own provider id even when Jellyfin lists it as enabled, `MigrationTargetValidation.cs:55-58`. The page filter is defence in depth, not the control. |
| T-03-03 | Elevation of Privilege | high | mitigate | CLOSED | Four code guards, not an informational control — see "The T-03-03 control is not informational" below. `EmbyLoginMethodUsers.cs:91-95`, `EmbyMigrationTask.cs:96`, `MoveAfterLogin.cs:63`, `LoginMethodMove.cs:37-41`. Warning text `configPage.html:208-226`; doc `docs/how-it-works.md:41`. |
| T-03-04 | Denial of Service (of the migration) / Tampering | medium | mitigate | CLOSED | Accepted risk — see AR-1. `LoginMethodMove.cs:69-80` sorts blank to `Invalid`, the sentinel to `Remain`, and any other string to `Move`, with no live-list read; `EmbyAuthenticationProvider.cs:145-147` writes the id unconditionally on `Move`. |
| T-03-05 | Information Disclosure | medium | mitigate | CLOSED | No message carries a configured value: `MigrationTargetValidation.cs:46,57,62`; `MoveAfterLogin.cs:78`; `EmbyMigrationTask.cs:113-123`; `EmbyAuthPlugin.cs:90`. `MigrationTargetValidationTests.cs:109-129` asserts it, which matters because the message also travels into an HTTP 4xx body via `EmbyAuthPlugin.cs:84`. |
| T-03-06 | Elevation of Privilege | high | mitigate | CLOSED | `[Authorize(Policy = Policies.RequiresElevation)]` at `Api/EmbyAuthController.cs:27`, once, at class level, covering both routes. e2e asserts 403 on both: `50-migration-target.bats:100-107`, `30-migration-modes.bats:106,108`. |
| T-03-07 | Information Disclosure | low | mitigate | CLOSED | `configPage.html:308` names the Jellyfin log and no path; `tests/js/configPage.test.js:325-334` tests the rendered text against a path pattern. |
| T-03-07 | Information Disclosure | low | accept | CLOSED | Accepted risk — see AR-2. `EmbyVerifiedPasswords.cs:145` names the file path only, on the same basis as the read-failure message at `:142`. No password, hash, or API key in either. |
| T-03-08 | Elevation of Privilege | high | mitigate | CLOSED | `EmbyAuthenticationProvider.cs:35` is `internal sealed`, with the reason in `<remarks>` at `:22-27`. Both guards live: `TypeVisibilityTests.cs:50-54` and `:56-69`. The allowlist at `:21-48` holds no authentication-provider type. |
| T-03-09 | Repudiation | medium | mitigate | CLOSED | `EmbyVerifiedPasswords.cs:145` now matches the code: the fingerprint is stored at `:58` before the write attempt at `:59-68`. Tests `EmbyVerifiedPasswordsTests.cs:181,196,208`. |
| T-03-10 | Cross-Site Scripting | high | mitigate | CLOSED | No `innerHTML`, `outerHTML`, `insertAdjacentHTML`, `document.write`, or `eval` in `configPage.html`. Sinks are `option.textContent` (`:120`), `item.textContent` (`:328`), `createTextNode` (`:95`). jsdom seeds a user name (`configPage.test.js:280-291`) and a provider-supplied label (`:645-659`), each asserting no child elements. |
| T-03-11 | Elevation of Privilege | high | mitigate | CLOSED | The verified-password gate precedes the only `MoveAsync` call: `MoveAfterLogin.cs:63` before `:68`. The task inherits it through `EmbyMigrationTask.cs:96`. Database backstop `LoginMethodMove.cs:37-41`. Quick Connect reaches the same event and the same gate. |
| T-03-12 | Tampering | medium | mitigate | CLOSED | Exactly one `ExecuteUpdateAsync` in the tree, `LoginMethodMove.cs:41`, setting one property. No `SaveChangesAsync` anywhere in `src/`. |
| T-03-13 | Tampering (supply chain) | high | mitigate | CLOSED | `scripts/fetch-jellyfinsecurity.sh` pins the tag (`:19`), the asset (`:20`), and the sha256 as a committed literal (`:28`). Fails closed at `:73-77`, removing the archive and returning 1 before `unzip` at `:79`. Unpacks only into `artifacts/`, mounted only into the disposable e2e container. |
| T-03-14 | Denial of Service | medium | mitigate | CLOSED | `e2e/compose.yaml:28` mounts JellyfinSecurity for every suite run; `50-migration-target.bats:29-39` fails with a diagnostic if it is absent from `AvailableTargets`; ordinary logins asserted 200 with it installed at `:56-57` and `:67-68`. See F-5. |
| T-03-15 | Information Disclosure | medium | mitigate | CLOSED | The new e2e users' passwords match the scanner pattern in `e2e/90-jellyfin-log.bats:16`, which runs last and fails the suite on a hit. |
| T-03-SC | Tampering (supply chain) | high | mitigate | CLOSED | `Microsoft.EntityFrameworkCore.Sqlite` `10.0.11`, exact pin, only in `tests/Jellyfin.Plugin.EmbyAuth.Tests/Jellyfin.Plugin.EmbyAuth.Tests.csproj:10`. Absent from the plugin project, so it never ships in the zip. |

*Status: open · closed · open — below high threshold (non-blocking)*

---

## The T-03-03 control is not informational

The register describes T-03-03's control as a disclosed-risk informational one: the plugin warns and neither blocks the move nor invents a password. That description understates what the code does, and this audit corrects it.

Every write to `AuthenticationProviderId` in `src/` and every call to `LoginMethodMove.MoveAsync` was traced. **No plugin-initiated path moves an account with no saved password to any login method.** Four guards, each sufficient alone:

1. `EmbyLoginMethodUsers.cs:91-95` — a null password hash becomes `NoPassword` before readiness is considered, so such an account can never be `Ready`.
2. `EmbyMigrationTask.cs:96` — the task moves only `State == Ready`.
3. `MoveAfterLogin.cs:63` — returns on `user?.Password is null` before the move at `:68`.
4. `LoginMethodMove.cs:37-41` — the `WHERE` requires `user.Password == passwordHash`, which a null password cannot satisfy.

The measurement recorded in `03-UAT.md` widened the set of *targets* that open a blank-password account. It did not widen the set of *accounts this plugin will move*, which remains exactly those carrying a hash Emby verified.

The residual the warning addresses is an administrator changing a login method by hand in Jellyfin's own interface — a path phase 02 established this plugin cannot veto. For a vector outside the plugin's reach an informational control is the only control available, so `mitigate` stands.

---

## Accepted Risks Log

| Risk ID | Threat Ref | Rationale | Accepted By | Date |
|---------|------------|-----------|-------------|------|
| AR-1 | T-03-04 (medium) | Resolve-time target re-validation | Maintainer | 2026-09-20 |
| AR-2 | T-03-07 (low, 03-02) | The write-failure message names the fingerprint file path | Maintainer (plan-time disposition) | 2026-09-19 |

**AR-1 — A migration target that was valid when saved is not re-checked when a move happens.** (T-03-04, medium)

`LoginMethodMove.ResolveTarget` never reads Jellyfin's enabled-method list; the refusal added by plan 06 is save-time only. A target whose providing plugin is later uninstalled still resolves to `Move` and is written by all three move paths.

Severity was measured rather than assumed. Jellyfin 12.1 assigns an account whose provider id names nothing installed to `InvalidAuthProvider`, which throws on every login — 401, with no fallback to `DefaultAuthenticationProvider`. Confirmed by source and by experiment with controls. The outcome is a stranded account an administrator can repair, not an exposed one.

Reaching it needs an administrator to uninstall a third-party plugin after configuring it as the migration target. Accepted on that basis. Full rationale and the measurement in `03-UAT.md`.

**One boundary on this rationale.** `03-UAT.md:49` records that `UserManager.ChangePassword` on a stale id routes to `InvalidAuthProvider.ChangePassword`, a no-op that reports success. An administrator who sets a password and *then* repairs the login method to Default stores nothing and lands a passwordless account on Default — the outcome T-03-03 exists to prevent. The immediate effect of a stale target is stranding; the recovery sequence an administrator would naturally attempt reaches exposure. The path is entirely in Jellyfin's code and needs two manual actions in the wrong order, so it changes neither disposition. The safe order — repair the login method first, set the password second — is the load-bearing part of this acceptance.

**AR-2 — The fingerprint write-failure message names the file path.** (T-03-07, low, 03-02)

`EmbyVerifiedPasswords.cs:145` names `{FilePath}` in the Error message, on the same basis as the pre-existing read-failure message at `:142`: a Jellyfin administrator can already read the plugin configuration folder. No password, hash, or API key appears in either message.

---

## Findings — register accuracy

None blocking. All are defects in plan-document text, not in code. They are the same class as the T-03-04 mitigation text corrected in `f03c323`.

**F-1 (medium).** `03-05-PLAN.md`'s T-03-04 row ends "Logins are unaffected either way, since Emby still checks every password." True only for a user still on the Emby method. A user written to a stale provider id lands on `InvalidAuthProvider` and gets 401 on every login.

**F-2 (medium).** `03-05-PLAN.md`'s T-03-03 row says the warning "states that the plugin cannot tell" how a non-Default method behaves. Commit `812d4ec` removed that wording and `configPage.test.js:707` asserts its absence. The code is stronger than the text.

**F-3 (medium).** The T-03-03 text in `03-02-PLAN.md`, `03-05-PLAN.md`, and `03-07-PLAN.md` calls the control informational and says the plugin neither blocks the move nor invents a password. Read alone it invites a future change to delete the four guards above as unnecessary. The guards are the mitigation and belong in the register text.

**F-4 (low).** `configPage.html:177-183` — the stale-target placeholder works because `placeholder.selected = true` at `:183` overrides a duplicate empty-valued option inserted at `:160`. Correct as shipped; its correctness rests on one line a plausible simplification would remove. Tracked as WR-01 and IN-01 in `03-REVIEW.md`.

**F-5 (low).** The `GET /Plugins` half of T-03-14's declared check exists as a one-off recorded in `03-07-SUMMARY.md`, not as a shipped assertion. The durable substitute at `50-migration-target.bats:29-39` is equivalent and runs every suite.

---

## Unregistered Flags

None found. No `## Threat Flags` section exists in any of the seven SUMMARY.md files, so the absence was not read as evidence. The phase's new attack surface was checked directly against the register and maps entirely: the reshaped `GET /EmbyAuth/Migration` payload (T-03-06, T-03-05), the two settings and the save-time refusal (T-03-01, T-03-02), the new controls and warning text (T-03-10, T-03-03), the third-party plugin in the e2e stack (T-03-13, T-03-14), and the new test-project package (T-03-SC).

The missing section is a process gap: it should be present and say "none" rather than be absent.

---

## Audit Trail

## Security Audit 2026-09-20

| Metric | Count |
|--------|-------|
| Threats found | 16 |
| Closed | 16 |
| Open | 0 |
