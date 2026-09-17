# Feature Research

**Domain:** Jellyfin 12.1 server plugin — third-party authentication/migration plugin preparing a public v1.0.0
**Researched:** 2026-09-17
**Confidence:** HIGH for Jellyfin-core behavior (primary source: `jellyfin/jellyfin` source and generated SDKs); MEDIUM for community-repo conventions and general release-engineering practice; LOW findings are marked explicitly and excluded from recommendations.

This file does not re-research the plugin's existing login/migration behavior — that is mapped in `.planning/codebase/`. It covers what Jellyfin itself expects from a plugin repository, a settings/status page, a release, and what comparable auth/migration plugins reveal about administrator expectations, scoped to the four target features in the milestone context and the open items in `.planning/codebase/CONCERNS.md`.

## Feature Landscape

### 1. Public third-party plugin repository (manifest, catalog install/update, trust)

#### Table Stakes

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| `manifest.json` with `guid`, `name`, `description`, `overview`, `owner`, `category`, and `versions[]` (each with `version`, `changelog`, `targetAbi`, `sourceUrl`, `checksum`, `timestamp`) | This is the exact schema Jellyfin's `InstallationManager`/`PluginManager` parse (`PackageInfo` + `VersionInfo`). A manifest missing a required field, or with fields in the wrong shape, fails silently — Jellyfin logs a `JsonException` and returns an empty package list rather than surfacing an error to the administrator. | LOW (largely already implemented in `scripts/package.sh`) | Confirmed against `MediaBrowser.Common.Plugins.PluginManifest` and the official docs example. Source: [jellyfin/jellyfin PluginManifest.cs](https://github.com/jellyfin/jellyfin/blob/9239b121/MediaBrowser.Common/Plugins/PluginManifest.cs) (HIGH), [jellyfin-jellyfin.mintlify.app/concepts/plugins](https://jellyfin-jellyfin.mintlify.app/concepts/plugins) (HIGH — mirrors the same source), [VersionInfo (TS SDK)](https://typescript-sdk.jellyfin.org/interfaces/generated-client.VersionInfo.html) (HIGH — generated from the server OpenAPI spec). |
| **Checksum must be a plain hex MD5 string, not a prefixed/other-algorithm digest** | `InstallationManager.PerformPackageInstallation` computes `Convert.ToHexString(MD5.HashData(stream))` on the downloaded zip and does a case-insensitive `string.Equals` against `VersionInfo.Checksum` — no algorithm-prefix parsing exists anywhere in the comparison. A manifest that follows the official docs' illustrative example (`"checksum": "sha256:..."`) would never match and every install/update would throw `InvalidDataException` and fail. | LOW (verify `scripts/package.sh` already emits plain hex MD5, per `.planning/codebase/INTEGRATIONS.md`) | Confirmed by reading `Emby.Server.Implementations/Updates/InstallationManager.cs` across three revisions (master and two historical SHAs) — the MD5-hex-no-prefix behavior is stable across the versions checked. Source: [InstallationManager.cs (master)](https://github.com/jellyfin/jellyfin/blob/master/Emby.Server.Implementations/Updates/InstallationManager.cs) (HIGH, primary source). The docs example showing `sha256:` is best read as illustrative and not literal — flag this as a documentation trap if any future contributor copies it verbatim into this plugin's manifest generation. |
| `targetAbi` set correctly per version, matching the Jellyfin package version the DLL was built against | Jellyfin filters `versions[]` by `Version.Parse(x.TargetAbi) <= appVer` and marks an incompatible install `NotSupported`; a wrong `targetAbi` either hides a compatible release or offers an incompatible one. | LOW | Confirmed: [InstallationManager.GetCompatibleVersions](https://github.com/jellyfin/jellyfin/blob/master/Emby.Server.Implementations/Updates/InstallationManager.cs) (HIGH). This is the exact pin `.planning/codebase/CONCERNS.md` item 8 already flags as under-covered by the version-bump rule. |
| A **stable manifest URL that lists every released version**, not just the latest | This is how every third-party repo Jellyfin's own docs list actually works (LDAP-Auth's own repo, 9p4/jellyfin-plugin-sso, Ani-Sync, danieladov's repo, LizardByte's repo). Jellyfin's update check compares the installed version against every version in the manifest, so a manifest that drops old versions breaks "what am I currently running vs. what update is available" for anyone who installed an older tag, and breaks downgrade. | LOW (already the chosen design; see `PROJECT.md` Key Decisions) | Source: [jellyfin.org/docs/general/server/plugins](https://jellyfin.org/docs/general/server/plugins/) lists the exact third-party manifest URLs administrators already add (HIGH — official docs, current list of real repos). |
| Release **zip contents and `meta.json`** exactly match what `PluginManager`/`ImportPluginFrom` expects (DLL + `meta.json` with `guid`, `name`, `version`, `status`, `autoUpdate`, `targetAbi`) | This is the on-disk contract, independent of the manifest. Already implemented per `INTEGRATIONS.md`. | LOW (existing) | Source: [jellyfin-jellyfin.mintlify.app/concepts/plugins](https://jellyfin-jellyfin.mintlify.app/concepts/plugins) (HIGH). |
| Public repository + public release assets, reachable **without GitHub credentials** | Jellyfin's `InstallationManager` fetches the manifest and the zip with a plain `HttpClient` — no auth headers, no GitHub token. A private repo or private release makes every install/update fail with a generic network error and no indication of "make the repo public." This is already the top Active requirement in `PROJECT.md`. | LOW (flip a repo setting, after the secret-history scan) | Confirmed: `GetAsync(new Uri(package.SourceUrl), ...)` in `InstallationManager.cs` has no auth (HIGH). Corroborated by `.planning/codebase/INTEGRATIONS.md`'s own statement that the manifest only works as a repo URL when the release files are public. |
| A README that states, in the repository's own words, **the exact repository URL to add** and the steps (Dashboard > Plugins > Repositories > Add Repository > paste URL > restart) | Every third-party repo Jellyfin's docs list is discoverable this way — from the project's own current README/docs, not a stale forum post. Community guidance explicitly warns administrators to re-add from the project's current docs because manifest URLs move. | LOW | Source: [LumaDock — Jellyfin plugin repositories not working](https://lumadock.com/tutorials/jellyfin-plugin-repositories-not-working) (MEDIUM — third-party tutorial site, not Jellyfin-official, but consistent with the official docs' own repo-URL listing pattern) and [jellyfin.org/docs/general/server/plugins](https://jellyfin.org/docs/general/server/plugins/) (HIGH). |
| Semantic versioning (`MAJOR.MINOR.PATCH`) and a per-version changelog entry | Listed as an explicit "Plugin Development Best Practice" in Jellyfin's own docs, and it is the field (`changelog`) the catalog UI shows next to each installable version. | LOW (already produced by `jprm`-style tooling / `scripts/package.sh`) | Source: [jellyfin-jellyfin.mintlify.app/concepts/plugins](https://jellyfin-jellyfin.mintlify.app/concepts/plugins) — "Use semantic versioning... Document breaking changes in changelogs" (HIGH). |
| GPL-3.0 (or a GPL-3.0-compatible permissive) license, stated plainly | Jellyfin's own plugin template states that any plugin linking the `Jellyfin.Controller`/`Jellyfin.Model` NuGet packages becomes GPLv3 by inheritance, and that a closed-source plugin for public distribution "is not permitted." Already satisfied by this project's `LICENSE`. | N/A (existing) | Source: [jellyfin/jellyfin-plugin-template](https://github.com/jellyfin/jellyfin-plugin-template/) (HIGH — official). |

#### Differentiators

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| Hosting the manifest on a dedicated git ref (e.g. a `manifest` or `manifest-release` branch, or GitHub Pages) instead of a path under the default branch | Keeps the manifest file's history independent of source-code commits and gives a shorter, memorable raw URL. This is exactly what 9p4/jellyfin-plugin-sso does (`raw.githubusercontent.com/9p4/jellyfin-plugin-sso/manifest-release/manifest.json`), and what the `Kevinjil/jellyfin-plugin-repo-action` and `LizardByte/jellyfin-plugin-repo` GitHub Actions automate. | MEDIUM (a new CI job/branch, plus a rule to keep it append-only) | Source: [jellyfin.org/docs/general/server/plugins](https://jellyfin.org/docs/general/server/plugins/) lists the 9p4 URL directly (HIGH); [Kevinjil/jellyfin-plugin-repo-action](https://github.com/Kevinjil/jellyfin-plugin-repo-action) and [LizardByte/jellyfin-plugin-repo](https://github.com/LizardByte/jellyfin-plugin-repo) (MEDIUM — community tooling, actively maintained). Not required: the current design (a release-asset `manifest.json` at a stable URL per `PROJECT.md`) satisfies the same requirement without a new CI dependency. Worth adopting only if manually maintaining the accumulating multi-version manifest by hand becomes error-prone. |
| Listing the plugin on `jellyfin.org/docs/general/server/plugins` (community-repo list) or `awesome-jellyfin` | Free discoverability and an implicit trust signal — administrators browsing that list already trust Jellyfin's own docs page enough to paste the URL in. | LOW (a PR to a docs repo, after v1.0.0 is stable) | Source: [jellyfin.org/docs/general/server/plugins](https://jellyfin.org/docs/general/server/plugins/) (HIGH) and [awesome-jellyfin](https://github.com/awesome-jellyfin/awesome-jellyfin/) (MEDIUM). Explicitly out of scope per `PROJECT.md` ("Submission to the official Jellyfin plugin repository — the chosen route is a self-hosted manifest"); the community-list submission is a lighter-weight version of the same idea and is a reasonable v1.x follow-up, not a v1.0.0 blocker. |
| GitHub tag protection / rulesets restricting who can push a `v*` tag | Adds a trust signal (only the maintainer can trigger a release) independent of whether CI ran. Doesn't gate on CI status — GitHub's `required_status_checks` ruleset rule applies to branch pull requests, not to tag pushes. | LOW (repo setting) | Source: [GitHub rulesets docs](https://docs.github.com/) (HIGH for the general mechanism); confirmed the CI-gating limitation via community discussion of the same problem: [semantic-release GitHub Actions discussion #2557](https://github.com/semantic-release/semantic-release/discussions/2557) and a real-world workaround pattern of polling `gh run list` for the tagged SHA's check results ([thedannywahl/pantoken release.yml](https://github.com/thedannywahl/pantoken/blob/83f3c6a8c9109bb02cbbba410d3686fa9d5e6cf4/.github/workflows/release.yml)) (MEDIUM — community pattern, not Jellyfin-specific). |

#### Anti-Features

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|------------------|-------------|
| Submitting to the official `repo.jellyfin.org` catalog | Feels like "the real" way to be trustworthy; appears first in the Jellyfin dashboard's default repository list | Requires upstream review/acceptance on a timeline this project does not control, and is explicitly out of scope per `PROJECT.md` | Self-hosted manifest at a stable URL, documented in the README exactly like every other third-party repo Jellyfin's own docs already list |
| A manifest that only lists the current/latest version | Simpler to generate, smaller file | Breaks Jellyfin's own update-check flow, which compares the installed version against every version in the manifest to decide whether an update exists, and removes any downgrade path for an administrator on an older tag | Append each release's `VersionInfo` to the one persistent manifest, as already decided in `PROJECT.md` |
| A private "release" repository separate from the private "source" repository | Seems like a way to keep source private while still publishing releases | Doubles maintenance, and Jellyfin still needs the release *assets* to be unauthenticated regardless of which repo hosts them — the source's privacy is orthogonal | Make the single existing repository public, after the secret-history scan `PROJECT.md` already requires |

**Complexity/Dependency note:** the manifest checksum correctness item (plain-hex MD5) and the "public repo + public release assets" item are the two failure modes most likely to make the plugin silently unavailable in the catalog with no error shown to the administrator (Jellyfin logs a `JsonException`/checksum mismatch server-side, not in the UI) — both should be verified with an end-to-end check (add the manifest URL to a real Jellyfin instance and confirm install succeeds) before tagging v1.0.0, not just unit-tested.

---

### 2. Dashboard settings page: load/save failure reporting and migration status

#### Table Stakes

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| `.catch()` (or try/catch) on every `ApiClient.getPluginConfiguration()` / `ApiClient.updatePluginConfiguration()` call, showing the error to the administrator rather than leaving the page blank or silent | This is CONCERNS.md item 5 verbatim ("the settings page shows a message when loading or saving the settings fails"). Other Jellyfin plugin config pages that were fixed for exactly this gap follow the same shape: wrap the load/save promise chain in `.catch(error => ...)` and render `error.message` (or a JSON body's message) into a status element. `configPage.html`'s current `pageshow` chain has a `.finally` but no `.catch` (`.planning/codebase/CONCERNS.md`). | LOW–MEDIUM (JS-only change plus the automated test CONCERNS.md item 11 calls out as missing) | Source: comparable plugin fix pattern — [jfdirect-plugin configPage.html](https://git.ishanjain.me/ishan/jfdirect-plugin/src/tag/v0.0.14/Configuration/configPage.html) (`.then(...).catch((error) => { ... error.message ... })`) (MEDIUM — real community plugin, not Jellyfin-official, but the exact failure pattern this plugin has). |
| `Dashboard.processPluginConfigurationUpdateResult()` (or an equivalent success toast) after a successful save | This is the standard Jellyfin dashboard convention for "settings saved" feedback — used across the official plugin template and multiple community plugins, so administrators already recognize the toast. | LOW (already likely used for the success path; verify it's called only after save actually succeeds) | Source: [example-plugin configPage.html](https://gitea.tourolle.paris/dtourolle/jellyfin-srfPlay/src/commit/ea89b92414ee7bea817937e52a90943ac502be13/example-plugin/Configuration/configPage.html) (MEDIUM) and [jfdirect-plugin](https://git.ishanjain.me/ishan/jfdirect-plugin/src/tag/v0.0.14/Configuration/configPage.html) (MEDIUM) — both call this exact API after `updatePluginConfiguration` resolves. |
| Migration list reflects a **finished** run, not just a queued one — poll or re-fetch until the task's `LastExecutionResult.Status` is no longer mid-run, instead of a single fixed-delay reload | CONCERNS.md documents the current 3-second fixed reload as a known gap: `POST /EmbyAuth/Migration/Run` returns 204 as soon as the task is *queued*, and a run that takes longer than 3 seconds leaves the list stale until the page reopens. Jellyfin's own `GET /ScheduledTasks/{taskId}` returns `State` (`Idle`/`Running`/`Cancelling`), `CurrentProgressPercentage`, and `LastExecutionResult.Status` (`Completed`/`Failed`/`Cancelled`/`Aborted`) specifically so a caller can poll for completion instead of guessing a delay. | MEDIUM (poll `GET /ScheduledTasks/EmbyAuthMoveUsersToDefault` — or the plugin's own `GET /EmbyAuth/Migration` — every N seconds until `State` returns to `Idle`, then do one final list refresh; cap the polling with a visible timeout) | Source: [Jellyfin System Tasks API docs](https://jellyfin-jellyfin.mintlify.app/api/system/tasks) (HIGH — matches the generated SDK) and [TaskInfo (TS SDK)](https://typescript-sdk.jellyfin.org/interfaces/generated-client.TaskInfo.html) (HIGH). This directly targets CONCERNS.md item 5. |
| Error message text follows the existing `textContent`-only rule (`.claude/rules/plugin.md:50`), never `innerHTML`, so a name or error string from the server can't inject markup | Already an established repository rule for the migration list; the same rule must extend to the new load/save error text since both render server-controlled strings. | LOW | Internal repository rule, not third-party research — carried forward from `.claude/rules/plugin.md` (cited in `.planning/codebase/CONCERNS.md`). |
| Error text never repeats the Emby API key or password, matching the existing "never log a secret" rule | `EmbyAuthenticationProvider` already avoids echoing configured values in its error log (`docs/settings.md:12`); the same discipline applies to whatever text the config page shows for a *save* failure, since a validation error could otherwise be tempted to echo back the invalid URL/key. | LOW | Source: `.planning/codebase/INTEGRATIONS.md` — "Invalid settings make the plugin refuse every login... without repeating the configured values." |

#### Differentiators

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| A `LastError` field persisted on `PluginConfiguration` and surfaced on `pageshow`, independent of the current page session | Lets an administrator who reloads the dashboard (or a different admin) see that the *last* save or a background operation failed, not just failures during the current page visit. This is the exact pattern `jfdirect-plugin` added specifically to fix a class of "silent failure" bug reports (`config.LastError`, read on `pageshow`, cleared before the next save attempt). | MEDIUM (a new config field, a setter on failure paths, and a read on page load) | Source: [jfdirect-plugin "added errors interface" commit](https://git.ishanjain.me/ishan/jfdirect-plugin/commit/e18758d848da487b29309098d9e5604c7c5b90b1) (MEDIUM — small community plugin, but the commit is a direct precedent for this exact problem). Not required for v1.0.0: CONCERNS.md item 5 only asks for in-session load/save error reporting and a migration list that reflects a finished run — both achievable without persisting `LastError` across sessions. Consider only if support requests show admins missing failures between visits. |
| Live migration progress via the `ScheduledTasksInfo` WebSocket event instead of polling `GET /ScheduledTasks/{taskId}` | Jellyfin's own dashboard uses a WebSocket listener (`ScheduledTasksWebSocketListener`) that pushes `TaskInfo` on every progress tick and on completion, avoiding a polling loop entirely. | HIGH (subscribing to the Jellyfin web socket from a plugin config page is unusual and undocumented for plugin authors; the officially supported integration point for a plugin's own config page is the REST API, not the session WebSocket) | Source: [ScheduledTasksWebSocketListener.cs](https://fossies.org/linux/jellyfin/Jellyfin.Api/WebSocketListeners/ScheduledTasksWebSocketListener.cs) (HIGH, primary source, but describes the *server's* dashboard, not a documented plugin-page pattern). Not recommended for v1.0.0 — the complexity/undocumented-API risk is disproportionate to a task that (per CONCERNS.md item 13-15) is expected to run in well under a minute for realistic user counts; polling `GET /ScheduledTasks/{taskId}` on a short interval is the supported, low-risk equivalent. |

#### Anti-Features

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|------------------|-------------|
| A generic client-side retry loop that silently re-tries a failed save without telling the administrator | Feels more "resilient" | Hides a real configuration problem (e.g. Jellyfin server down, invalid session) behind apparent inactivity, and contradicts CONCERNS.md's actual ask — a *visible* error message, not automatic recovery | Show the error immediately; let the administrator decide whether to retry |
| Replacing the fixed-delay migration reload with unlimited/uncapped polling that never gives up | Simpler than picking a timeout | An admin-only page hanging a spinner forever on a stuck task looks broken, and Jellyfin already reports `State: "Cancelling"` or a `Failed`/`Aborted` `LastExecutionResult.Status` that a capped poll can surface instead | Poll with a bounded number of attempts/timeout, then show "still running — check Dashboard > Advanced > Scheduled Tasks" with a link, rather than looping indefinitely |

---

### 3. Public plugin release process (versioning, changelog, CI-gated release)

#### Table Stakes

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Semantic versioning (`MAJOR.MINOR.PATCH[.BUILD]`) | Explicit Jellyfin plugin best practice, and required by `PluginManifest.Version`/`VersionInfo.VersionNumber` parsing (`System.Version`, compared with `<=`/`>` for compatibility and update checks). Already the project's convention (`v1.0.0.0` tags per `docs/development.md`). | N/A (existing) | Source: [jellyfin-jellyfin.mintlify.app/concepts/plugins](https://jellyfin-jellyfin.mintlify.app/concepts/plugins) — "Use semantic versioning (MAJOR.MINOR.PATCH)" (HIGH); general convention: [semver.org](https://semver.org/) (HIGH, canonical spec). |
| A per-version changelog, in the manifest's `changelog` field and (conventionally) also in the repo as `CHANGELOG.md` | The manifest `changelog` field is what the Jellyfin catalog UI shows next to each installable version — an empty or generic changelog gives an administrator nothing to judge whether the update is safe. `jprm`'s own metadata schema requires a `changelog` value per build. | LOW | Source: [oddstr13/jellyfin-plugin-repository-manager Metadata.md](https://github.com/oddstr13/jellyfin-plugin-repository-manager/blob/master/Metadata.md) — `changelog` marked required (MEDIUM — third-party tooling, but widely used in the Jellyfin plugin ecosystem); general convention: [keepachangelog.com](https://keepachangelog.com/) (HIGH, canonical, describes the human-readable `CHANGELOG.md` convention this manifest field usually mirrors). |
| A release is built and published **only from a commit that already passed the full check suite** (lint, unit, script, and e2e tests) | This is CONCERNS.md item 7 verbatim. `release.yml` currently re-runs `mise run test` but not `mise run lint` or `mise run e2e`, and the trigger is any pushed `v*` tag with no check that the tagged commit is the one CI already validated on `main`. | MEDIUM | GitHub's `required_status_checks` ruleset rule enforces this only for *branch* pull requests, not for a tag push — confirmed via community discussion of the identical problem: [semantic-release GitHub Actions discussion #2557](https://github.com/semantic-release/semantic-release/discussions/2557) (MEDIUM) and [GitHub Actions workflow conflicts with branch ruleset on tag publish](https://stackoverflow.com/questions/79789230/github-actions-workflow-conflicts-with-branch-ruleset-when-publishing-npm-packag) (MEDIUM). The two practical patterns observed: (a) re-run the same lint/test/e2e jobs inside `release.yml` before packaging (simplest, matches this project's existing single-maintainer scale), or (b) have `release.yml` query the GitHub Checks API for the tagged SHA and fail if the required workflows aren't `success` (`gh run list --commit "$SHA" --workflow ci.yml`, seen in [thedannywahl/pantoken's gate job](https://github.com/thedannywahl/pantoken/blob/83f3c6a8c9109bb02cbbba410d3686fa9d5e6cf4/.github/workflows/release.yml)). Recommend (a): it needs no extra token/permissions and directly satisfies "published only from a commit that passed lint, unit tests, script tests, and e2e tests" without depending on `ci.yml`'s prior run for the same SHA. |
| The release job's elevated permission (`contents: write`) is scoped to only the release job, not the whole workflow | Already true per `.planning/codebase/INTEGRATIONS.md` ("Only this job has `contents: write`"). Least-privilege is a standard GitHub Actions security expectation for any public repo, not Jellyfin-specific. | N/A (existing) | Corroborated by `zizmor --persona=pedantic` findings already catalogued in CONCERNS.md (informational-level, not blocking). |
| A GitHub release with the zip and `manifest.json` attached, using `--generate-notes` or an equivalent human-readable summary | Administrators evaluating whether to trust an update read the GitHub release page, not just the manifest `changelog` string — this is the "install steps, changelog" trust signal named in the milestone context. Already implemented per `.planning/codebase/INTEGRATIONS.md`. | N/A (existing) | — |

#### Differentiators

| Feature | Value Proposition | Complexity | Notes |
|---------|-------------------|------------|-------|
| A `concurrency` group on `release.yml` so two tag pushes (or a re-tag) can't race and publish a corrupted/partial release | `zizmor --persona=pedantic` already flags the missing `concurrency` setting on `release.yml` (CONCERNS.md, Tooling Gaps) as a low-severity finding. | LOW | Internal finding, not third-party research; carried forward from CONCERNS.md. |
| Verified/signed releases (e.g. Sigstore/`cosign`, SLSA provenance attestation) | Growing convention in some ecosystems for supply-chain trust (seen in the `pantoken` example: `actions/attest-build-provenance`). | HIGH (new signing infrastructure, and Jellyfin's `InstallationManager` has no signature-verification step at all — it only checks the MD5 checksum embedded in the manifest itself) | Source: [thedannywahl/pantoken release.yml](https://github.com/thedannywahl/pantoken/blob/83f3c6a8c9109bb02cbbba410d3686fa9d5e6cf4/.github/workflows/release.yml) (MEDIUM — general CI pattern, not Jellyfin-specific). Not worth building: Jellyfin itself has no mechanism to consume or check a signature, so this would add supply-chain rigor that no part of the actual install path can verify or benefit from. |
| Automated version bump + changelog generation (e.g. `semantic-release`, `changesets`, `knope`) from conventional commits | Removes the manual "set three version properties in `Directory.Build.props`" step in `docs/development.md`. | MEDIUM–HIGH (new tooling dependency, commit-message discipline, and — per the research above — extra complexity specifically to keep it compatible with branch protection, since the automated commit/tag push needs a token that can bypass or satisfy the ruleset) | Source: [semantic-release GitHub Actions docs](https://semantic-release.org/recipes/ci-configurations/github-actions/) (HIGH for the tool's own documented behavior). Not recommended for v1.0.0: this is a single-maintainer repo with an already-documented three-step manual release procedure; the tooling and branch-protection interplay documented above (needing a GitHub App token or PAT to push past required checks) is disproportionate overhead for the current release cadence. Reasonable v2+ consideration if release frequency grows. |

#### Anti-Features

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|------------------|-------------|
| Skipping e2e tests in the release workflow "because CI on `main` already ran them" | Faster release, avoids running Docker-based e2e twice | This is precisely the gap CONCERNS.md item 7 flags — nothing verifies the *tagged* commit is the same one that passed CI on `main`, and a tag can be pushed to any commit, including one CI never saw | Re-run the full check suite (lint, unit, script, e2e) inside `release.yml` before packaging, or verify the tagged SHA's prior CI status via the API |
| Auto-publishing a release on every push to `main` | Removes the manual tag step | Turns every merged PR into a public release, which is inappropriate for a plugin where administrators expect a deliberate, versioned, changelog-backed release, not continuous deployment | Keep the explicit `v*` tag trigger; gate it on the check suite instead of removing the manual step |

---

### 4. Migration plugin behavior (comparable projects, scoped to open CONCERNS.md items)

This section only covers what bears on CONCERNS.md's open bugs/fragile-area items — not a general survey of migration-plugin features, since the migration design itself (`MoveAfterFirstLogin`/`KeepEmbyInCharge`/`JellyfinPasswordFirst`, `AccountAccessPolicy`) is already built and out of this research's scope per the milestone context.

#### Table Stakes (validated against comparable plugins)

| Feature | Why Expected | Complexity | Notes |
|---------|--------------|------------|-------|
| Never grant administrator rights or elevate permissions through the migration/auth path | Already true for this plugin ("The plugin never gives administrator rights, and never turns remote access on for an existing account" — `docs/settings.md`). Community precedent shows why this matters: the SSO plugin's linking behavior *did* let a role-mapping misconfiguration silently strip admin rights from the only admin account, requiring users to edit the SQLite database directly to recover. | N/A (existing) | Source: [9p4/jellyfin-plugin-sso issue #212](https://github.com/9p4/jellyfin-plugin-sso/issues/212) (MEDIUM — real, still-open community issue) and its `providers.md` warning: *"make sure you have another admin account... permission might get overwritten"* (MEDIUM). This validates the existing design choice rather than proposing new scope. |
| Never silently overwrite an existing account's access/permissions during a migration event the user didn't initiate (e.g. a link, a Quick Connect event) | Same precedent as above, plus a second SSO issue where `AuthenticationProviderId` was overwritten on every login, permanently forcing the account onto the new provider with no supported way back except a raw API call. This plugin already avoids the analogous trap: `MoveToDefaultLoginMethod` only fires in `MoveAfterFirstLogin` mode and only after `EmbyVerifiedPasswords.Matches` succeeds (`.planning/codebase/INTEGRATIONS.md`), and Quick Connect logins are explicitly blocked from moving a user by the same fingerprint check. | N/A (existing; validates current design) | Source: [9p4/jellyfin-plugin-sso issue #5](https://github.com/9p4/jellyfin-plugin-sso/issues/5) (MEDIUM). |
| A documented, admin-triggered "undo"/reversal path when a migration side effect needs correcting | The SSO project's own bug reports repeatedly asked for a supported way to move a user back off the linking provider; it eventually shipped an `Unregister` API endpoint for exactly this. This plugin's equivalent need — a user who got moved back onto the Emby login method by a concurrent session (CONCERNS.md, "A concurrent session can put a user back on the Emby login method") — is already handled: such a user reappears in the Migration list and the next migration run moves them again. | N/A (existing; validates current design, ties to CONCERNS.md item 2's "wrong shutdown step" fix) | Source: [9p4/jellyfin-plugin-sso Unregister endpoint](https://github.com/9p4/jellyfin-plugin-sso) (MEDIUM). |
| A pre-shutdown checklist that accounts for users who cannot complete the migration path before the source server goes away | `docs/migration.md`'s "Shut down Emby" procedure (list ready users, chase non-ready users, force a Jellyfin password for stragglers, confirm the list is empty) matches the shape of every comparable "cut over from an external auth source" runbook: identify not-yet-migrated accounts, give them one more chance, force a fallback for stragglers, verify zero remaining before removing the old dependency. | N/A (existing) | No single third-party source documents this exact runbook for an Emby-to-Jellyfin case (none exists — this plugin is the identified prior art), so this is corroborated by the LDAP/SSO plugins' shared pattern of "the linked/legacy account stays reachable until an explicit unlink/migrate step," rather than a direct citation. Confidence: MEDIUM (pattern-level, not literal). |

#### Anti-Features

| Feature | Why Requested | Why Problematic | Alternative |
|---------|---------------|------------------|-------------|
| Automatically re-linking or re-verifying a user against Emby on every login after migration, "just in case" | Feels safer, catches drift | Defeats the entire point of the migration (moving off Emby so it can be shut down), and re-introduces the same "external server must be reachable" dependency the plugin exists to remove | Verify once via the fingerprint mechanism already built, then move to Default and stop depending on Emby |
| A generic "identity migration framework" reusable for other source servers (Plex, Emby *and* others) beyond this milestone's Emby-specific scope | Looks like a bigger, more general product | Not requested in `PROJECT.md`; adds abstraction with no second consumer, violates the "no premature abstraction" default and expands scope well beyond the milestone's stated Active requirements | Keep the plugin Emby-specific, as scoped |
| Bulk permission/role syncing from the source server on every login (mirroring SSO's `EnableAuthorization`-style continuous role mapping) | Superficially similar to `AccountAccessPolicy`'s one-time remote-access copy | This plugin's `AccountAccessPolicy` is intentionally a one-time, login-time policy for *new* accounts and a narrow remote-access-only adjustment for existing ones — continuous role syncing is exactly the mechanism that caused the SSO plugin's admin-permission-loss bug | Keep `AccountAccessPolicy` scoped as already designed; do not add continuous permission sync |

---

## Feature Dependencies

```
Public repository + secret-history scan (PROJECT.md Active)
    └──requires──> Manifest checksum correctness (plain hex MD5)
    └──requires──> Public release assets (no GitHub auth needed to fetch)
                       └──enables──> Administrator adds manifest URL and installs from catalog (PROJECT.md Active)

Release gated on full CI suite (CONCERNS.md item 7)
    └──requires──> release.yml runs lint + unit + script + e2e (not just unit/script as today)
    └──enables──> "v1.0.0 tagged and published through the release workflow" (PROJECT.md Active)

Settings page load/save error handling (CONCERNS.md item 5)
    └──independent of──> Migration list reflecting a finished run (CONCERNS.md item 5, second half)
                              └──requires──> ScheduledTasks API polling (GET /ScheduledTasks/{taskId} or the plugin's own GET /EmbyAuth/Migration) replacing the fixed 3-second reload

Version-bump pin coverage (CONCERNS.md item 8)
    └──enhances──> Manifest targetAbi correctness (table stakes #1, "targetAbi set correctly")
```

### Dependency Notes

- **Public repository requires manifest checksum correctness and public release assets:** all three are prerequisites Jellyfin's `InstallationManager` enforces mechanically (MD5 match, unauthenticated fetch); shipping the repository-visibility change without first confirming the checksum algorithm is right would produce a plugin that's "in the catalog" but every install fails with a server-side-only error.
- **Release gating requires extending `release.yml`, not `ci.yml`:** CI already runs the full suite on PRs and pushes to `main` (`.planning/codebase/INTEGRATIONS.md`); the gap is specifically that the *release* trigger (a pushed tag) doesn't re-verify or re-run that suite for the tagged commit.
- **Settings-page error handling and migration-status polling are independent fixes** that happen to live in the same file (`configPage.html`) and the same CONCERNS.md item — they can be planned/executed as two changes without ordering constraints between them, though testing both together (CONCERNS.md item 11, "settings page JavaScript has automated tests") is efficient to do once.
- **Version-bump pin coverage enhances (not blocks) manifest correctness:** a bump that misses a pin produces a build/test failure today (per CONCERNS.md, `tests/scripts/package.bats` already asserts the fixed `targetAbi`), so this is a maintainability fix, not a v1.0.0 blocker in itself — but it directly protects the "targetAbi set correctly" table-stakes item above for every *future* release after v1.0.0.

## MVP Definition

`PROJECT.md`'s Active requirements already define this milestone's scope in detail; this section maps that scope onto the feature landscape above rather than re-deriving it.

### Launch With (v1.0.0)

- [ ] Manifest with plain-hex-MD5 checksums, correct `targetAbi`, and every released version retained — table stakes, feature area 1
- [ ] Public repository and public release assets, after the secret-history scan — table stakes, feature area 1
- [ ] README states the exact repository URL and install steps — table stakes, feature area 1
- [ ] Settings page shows a message on load/save failure — table stakes, feature area 2 (CONCERNS.md item 5)
- [ ] Migration list reflects a finished run (poll until `Idle`/`Completed`, not a fixed 3-second reload) — table stakes, feature area 2 (CONCERNS.md item 5)
- [ ] `release.yml` runs the full check suite (lint, unit, script, e2e) before packaging — table stakes, feature area 3 (CONCERNS.md item 7)
- [ ] Version-bump rule names every pin, including the test project and `package.bats`'s `targetAbi` values — table stakes, feature area 3 (CONCERNS.md item 8)
- [ ] `zizmor --persona=pedantic` findings fixed or suppressed with a written reason — table stakes, feature area 3 (CONCERNS.md item 9)

### Add After Validation (v1.x)

- [ ] Submit the manifest URL to `jellyfin.org`'s community-repo list or `awesome-jellyfin` — trigger: v1.0.0 has run stably on at least one real (non-maintainer) install
- [ ] `concurrency` group on `release.yml` — trigger: the low-severity `zizmor --persona=pedantic` backlog is revisited
- [ ] Dedicated manifest branch/GitHub Pages hosting via `Kevinjil/jellyfin-plugin-repo-action` or similar — trigger: hand-maintaining the multi-version manifest becomes error-prone

### Future Consideration (v2+)

- [ ] Automated changelog/version-bump tooling (`semantic-release`/`changesets`/`knope`) — defer: disproportionate to a single-maintainer, low-frequency release cadence today
- [ ] Signed/attested releases (Sigstore, SLSA provenance) — defer: Jellyfin's install path has no mechanism to check a signature, so it wouldn't move the needle on end-user trust
- [ ] `LastError` persisted across dashboard sessions — defer: not requested by CONCERNS.md item 5's actual scope; revisit only if support reports show admins missing failures between visits

## Feature Prioritization Matrix

| Feature | User Value | Implementation Cost | Priority |
|---------|------------|---------------------|----------|
| Manifest checksum/targetAbi correctness | HIGH | LOW | P1 |
| Public repo + public release assets | HIGH | LOW | P1 |
| Settings page load/save error message | HIGH | LOW–MEDIUM | P1 |
| Migration list reflects finished run (poll, not fixed delay) | HIGH | MEDIUM | P1 |
| Release gated on full CI suite | HIGH | MEDIUM | P1 |
| Version-bump pin coverage | MEDIUM | LOW | P1 |
| `zizmor --persona=pedantic` fixes/suppressions | LOW–MEDIUM | LOW | P1 |
| README/community-repo-list submission | MEDIUM | LOW | P2 |
| `concurrency` group on `release.yml` | LOW | LOW | P2 |
| Dedicated manifest-hosting Action | LOW | MEDIUM | P3 |
| Automated release tooling | LOW | HIGH | P3 |
| Signed/attested releases | LOW | HIGH | P3 |
| `LastError` persisted across sessions | LOW | MEDIUM | P3 |

**Priority key:** P1: must have for v1.0.0 (already reflected in `PROJECT.md` Active requirements). P2: should have, natural v1.x follow-up. P3: nice to have, defer past this milestone.

## Competitor / Comparable-Plugin Feature Analysis

| Feature | 9p4 / Flowfin SSO plugin | Official LDAP-Auth plugin | This plugin's approach |
|---------|--------------------------|----------------------------|-------------------------|
| Repository hosting | Dedicated `manifest-release` branch, raw.githubusercontent.com URL | Listed directly in Jellyfin's own official manifest (built-in) | Self-hosted `manifest.json` on GitHub releases at a stable URL (chosen; `PROJECT.md`) |
| Existing-account handling on first "external" login | Links by matching display name/username; historically overwrote permissions (issue #212, now being addressed with an opt-in overwrite toggle per the Flowfin fork) | Matches by username; not independently re-verified in this research | Matches by exact Emby username (case-insensitive); never touches admin rights; only a narrow remote-access adjustment per `AccountAccessPolicy` |
| Reversal / unlink path | Added a dedicated `Unregister` API endpoint after repeated bug reports | Not researched (out of this milestone's scope) | Existing migration list + task already re-catches a user pushed back onto the Emby login method (CONCERNS.md item 2's target behavior) |
| Release channel discipline | Explicit "beta channel only until first stable release" messaging in the README (Flowfin fork) | N/A (official, bundled with Jellyfin's default repo) | Single `v*`-tag release flow; v1.0.0 is this project's first public tag |

## Sources

**Jellyfin core / official (HIGH confidence unless noted):**
- [jellyfin-jellyfin.mintlify.app/concepts/plugins](https://jellyfin-jellyfin.mintlify.app/concepts/plugins) — manifest schema, best practices (semver, changelog, targetAbi)
- [jellyfin.org/docs/general/server/plugins](https://jellyfin.org/docs/general/server/plugins/) — official + third-party repository URLs administrators actually add
- [jellyfin/jellyfin — MediaBrowser.Common/Plugins/PluginManifest.cs](https://github.com/jellyfin/jellyfin/blob/9239b121/MediaBrowser.Common/Plugins/PluginManifest.cs)
- [jellyfin/jellyfin — Emby.Server.Implementations/Updates/InstallationManager.cs](https://github.com/jellyfin/jellyfin/blob/master/Emby.Server.Implementations/Updates/InstallationManager.cs) — checksum algorithm (MD5, unprefixed hex), unauthenticated fetch, targetAbi filtering
- [typescript-sdk.jellyfin.org — VersionInfo](https://typescript-sdk.jellyfin.org/interfaces/generated-client.VersionInfo.html) / [TaskInfo](https://typescript-sdk.jellyfin.org/interfaces/generated-client.TaskInfo.html) — generated from the server OpenAPI spec
- [Jellyfin System Tasks API docs](https://jellyfin-jellyfin.mintlify.app/api/system/tasks) — `GET /ScheduledTasks/{taskId}`, `State`, `CurrentProgressPercentage`, `LastExecutionResult`
- [Jellyfin.Api/WebSocketListeners/ScheduledTasksWebSocketListener.cs](https://fossies.org/linux/jellyfin/Jellyfin.Api/WebSocketListeners/ScheduledTasksWebSocketListener.cs) — server-side live task-progress push (not a documented plugin-page integration point)
- [Jellyfin.Api/Controllers/DashboardController.cs](https://github.com/jellyfin/jellyfin/blob/9239b121/Jellyfin.Api/Controllers/DashboardController.cs) — how a plugin's config page is served
- [jellyfin/jellyfin-plugin-template](https://github.com/jellyfin/jellyfin-plugin-template/) — GPLv3 inheritance, targetAbi/package-version matching

**Community tooling and comparable plugins (MEDIUM confidence):**
- [Kevinjil/jellyfin-plugin-repo-action](https://github.com/Kevinjil/jellyfin-plugin-repo-action), [LizardByte/jellyfin-plugin-repo](https://github.com/LizardByte/jellyfin-plugin-repo) — manifest-hosting automation patterns
- [oddstr13/jellyfin-plugin-repository-manager Metadata.md](https://github.com/oddstr13/jellyfin-plugin-repository-manager/blob/master/Metadata.md) — `jprm` metadata schema, required `changelog` field
- [9p4/jellyfin-plugin-sso](https://github.com/9p4/jellyfin-plugin-sso) issues [#212](https://github.com/9p4/jellyfin-plugin-sso/issues/212) and [#5](https://github.com/9p4/jellyfin-plugin-sso/issues/5), and [Flowfin/jellyfin-plugin-sso](https://github.com/Flowfin/jellyfin-plugin-sso) — comparable auth/migration plugin pitfalls (permission overwrite, provider lock-in)
- [jfdirect-plugin configPage.html/commits](https://git.ishanjain.me/ishan/jfdirect-plugin/src/tag/v0.0.14/Configuration/configPage.html) — `.catch()` and `LastError` config-page error-handling precedent
- [thedannywahl/pantoken release.yml](https://github.com/thedannywahl/pantoken/blob/83f3c6a8c9109bb02cbbba410d3686fa9d5e6cf4/.github/workflows/release.yml), [semantic-release GitHub Actions discussion #2557](https://github.com/semantic-release/semantic-release/discussions/2557) — CI-gated release patterns and the branch-protection/tag-push limitation
- [semver.org](https://semver.org/), [keepachangelog.com](https://keepachangelog.com/) — canonical general conventions (not Jellyfin-specific)
- [LumaDock — Jellyfin plugin repositories not working](https://lumadock.com/tutorials/jellyfin-plugin-repositories-not-working) — third-party operational guidance corroborating official docs

**Not independently verifiable / LOW confidence (excluded from recommendations above):** whether GitHub's tag-protection rulesets can be configured to require a specific *workflow's* prior success before a tag push is accepted (as opposed to only restricting *who* can push the tag) was not confirmed against current GitHub documentation in this research pass; the recommendation above (re-run the check suite inside `release.yml`) sidesteps this uncertainty entirely rather than relying on it.

---
*Feature research for: Jellyfin 12.1 authentication/migration plugin, public v1.0.0 milestone*
*Researched: 2026-09-17*
