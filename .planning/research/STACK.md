# Stack Research

**Domain:** Jellyfin 12.1 authentication plugin (C#/.NET 10) — testing, load testing, secret scanning, and public plugin distribution for a v1.0.0 release
**Researched:** 2026-09-17
**Confidence:** HIGH for versions and package facts (cited to NuGet/GitHub); MEDIUM for the two workflow designs (release gating, manifest publishing), because no single official Jellyfin doc prescribes one exact pattern

This file only covers what the milestone's six target capabilities add. It does not re-recommend anything already pinned in `.mise.toml`, `Directory.Build.props`, or the two `.csproj` files — see `.planning/codebase/STACK.md` for the existing stack.

## Recommended Stack

### Core Technologies

| Technology | Version | Purpose | Why Recommended |
|------------|---------|---------|-----------------|
| NSubstitute | 6.2.0 | Mock `IUserManager` and `ITaskManager` in unit tests | These two Jellyfin interfaces have dozens of members; a hand-written stub (the repo's existing pattern in `TestDoubles.cs`) would mean maintaining a large throw-by-default implementation that breaks on every Jellyfin interface change. NSubstitute is interface-only, MIT-licensed, has no telemetry or sponsorship controversy (unlike Moq's 2023 SponsorLink incident), and its own CI already targets .NET 10 and runs on Microsoft.Testing.Platform — the same runner this repo uses. Confidence: HIGH ([release](https://github.com/nsubstitute/NSubstitute/releases/tag/v6.2.0), [CHANGELOG](https://github.com/nsubstitute/NSubstitute/blob/main/CHANGELOG.md)). |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.11 | Give `DefaultLoginMethod.MoveAsync` and `EmbyLoginMethodUsers.ListAsync` a real `JellyfinDbContext` backed by a relational database in unit tests | `Jellyfin.Database.Implementations` 12.1.0 itself depends on `Microsoft.EntityFrameworkCore.Relational >= 10.0.11` ([NuGet](https://www.nuget.org/packages/Jellyfin.Database.Implementations/12.1.0)); pinning the test project's SQLite provider to that same floor version keeps the test database engine on the EF Core version Jellyfin actually ships. Confidence: HIGH. |
| Node.js | 24.21.0 (Active LTS "Krypton") | Run automated tests for `configPage.html`'s inline JavaScript | Active LTS as of 2026-09-17, supported until 2028-04-30 ([nodejs/Release](https://github.com/nodejs/release/blob/main/README.md), [endoflife.date](https://endoflife.date/nodejs)). Node's built-in `node:test` runner and `node:assert` need no extra test-framework dependency, and `mise` already has a first-party `node` plugin, so `jdx/mise-action` installs it in CI the same way it installs `dotnet` today — no new GitHub Action. Confidence: HIGH. |
| jsdom | 29.1.1 | DOM environment so `node:test` can load and execute `configPage.html` outside a browser | Jsdom's `runScripts: 'dangerously'` option executes the page's inline `<script>` tag as written, so tests can load the real `configPage.html` file with no change to how Jellyfin serves it (Jellyfin embeds it as one `EmbeddedResource`, and the JS is inline, not a separate `<script src>` file). Confidence: HIGH for the version ([npm](https://www.npmjs.com/package/jsdom)); MEDIUM for the "no HTML restructuring needed" claim — verify jsdom actually fires `pageshow` synthetically before writing tests, since that event does not fire automatically on `document.write`/load in jsdom the way it does in a real browser. |
| k6 | 2.2.0 | Generate the load test traffic: slow Emby responses, concurrent first logins, cache-expiry timing | Single static Go binary, `mise`-installable (`ubi:grafana/k6` backend), scriptable in JavaScript so scenarios (ramping VUs, thresholds on p95 latency) are readable, and it needs no server component — it runs as a host CLI against the already-Docker-exposed `JELLYFIN_PORT`, the same way `bats e2e` already talks to the compose stack over the host network. Released 2026-08-10, current as of research date. Confidence: HIGH ([release](https://github.com/grafana/k6/releases/tag/v2.2.0)). |
| gitleaks | 8.30.1 | One-time full-history secret scan before the repository goes public, then an ongoing pre-commit/CI guard | Single Go binary, MIT-licensed, `mise`-installable (`aqua:gitleaks/gitleaks`), and `gitleaks git --source .` scans the full commit history by default — exactly what "scan before going public" needs, not just the working tree. Confidence: MEDIUM — GitHub's release page and a third-party mirror disagree on the exact publish date for 8.30.1 (one shows 2026-03-21, mise's tool index shows 8.30.0 as its newest cached entry); re-run `gitleaks version` / check `github.com/gitleaks/gitleaks/releases` at implementation time to confirm no newer patch exists. |

### Supporting Libraries

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| toxiproxy | 2.12.0 | Inject controllable latency/timeouts between the plugin's configured `EmbyServerUrl` and the real Emby container | The load test needs to simulate "Emby is slow" deterministically and reset it between scenarios; toxiproxy exposes an HTTP control API for adding/removing a `latency` toxic mid-run, which a k6 setup/teardown function can call directly. Use it only in the load-test compose overlay, not in `e2e/compose.yaml` — the existing e2e nginx proxy stays focused on request-body logging. Confidence: MEDIUM — latest tagged release is from 2025-03-18 ([releases](https://github.com/shopify/toxiproxy/releases)), over a year old at research date; the project appears low-churn rather than abandoned (32 releases, an active fork of maintainers still merging PRs), but confirm there's no newer unreleased fix needed before depending on it for CI. |
| actions/configure-pages, actions/upload-pages-artifact, actions/deploy-pages | Resolve exact SHA at implementation time | Publish the merged, multi-version `manifest.json` to GitHub Pages as a stable URL | Needed only if GitHub Pages (rather than a plain `manifest` branch + `raw.githubusercontent.com`) is the chosen hosting for the public manifest — see Alternatives Considered. Confidence: LOW on exact version/SHA — search results disagree between each action's own README example (`@v4`, `@v3`) and a real-world workflow using `@v6`/`@v5`; do not copy either without first running `gh api repos/actions/deploy-pages/releases/latest` (and the same for the other two) to get the true current tag and its commit SHA, per this repo's SHA-pinning rule. |

### Development Tools

| Tool | Purpose | Notes |
|------|---------|-------|
| `mise` `node` plugin | Pins the Node.js version for the JS test suite the same way `dotnet`, `bats`, etc. are pinned | Add `node = "24.21.0"` to `.mise.toml`; `jdx/mise-action` in CI then installs it automatically, no separate `actions/setup-node` step. |
| `node:test` + `node:assert` (built-in) | Test runner and assertions for `configPage.html` | Ships with Node — no `vitest`/`jest`/`mocha` dependency. Matches this repo's existing preference for hand-written doubles over test frameworks (see `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs`) and keeps the new JS toolchain to a single `npm` dependency (`jsdom`). |
| `node:test`'s built-in `mock` object | Spy on `ApiClient.getJSON`, `ApiClient.ajax`, `Dashboard.showLoadingMsg`, etc. | Available via `import { mock } from 'node:test'` since Node 20; no `sinon` needed. Stub `window.ApiClient` / `window.Dashboard` as plain objects with `mock.fn()` implementations before loading the page into jsdom. |
| `npm` + `package-lock.json` | Reproducible install of the one new dependency (`jsdom`) | This repo has no npm dependency today; committing a lockfile keeps the JS toolchain as pinned as everything else here. Add an `npm ci` step to a new (or the existing) mise `test` task. |
| `gitleaks` pre-commit hook | Stop new secrets from landing after the repository is public | Add a `gitleaks` entry to `.pre-commit-config.yaml` alongside the existing `lint`/`test` hooks, and a `gitleaks git --source . --redact` step to the CI `lint` job (`mise run lint`) so a PR that reintroduces a secret fails CI, not just a contributor's local hook. |

## Installation

```bash
# .mise.toml — add alongside the existing pins
[tools]
node = "24.21.0"

# Test project (from tests/Jellyfin.Plugin.EmbyAuth.Tests/)
dotnet add package NSubstitute --version 6.2.0
dotnet add package Microsoft.EntityFrameworkCore.Sqlite --version 10.0.11

# JS test toolchain (new tests/js/ directory, its own package.json)
npm install --save-dev jsdom@29.1.1

# Host CLIs pinned in .mise.toml, installed by `mise install`
# k6 = "2.2.0"
# gitleaks = "8.30.1"
```

## Alternatives Considered

| Recommended | Alternative | When to Use Alternative |
|-------------|-------------|--------------------------|
| NSubstitute for `IUserManager`/`ITaskManager` | Hand-written throw-by-default stubs, matching the repo's existing `TestDoubles.cs` pattern | If the maintainer wants zero new NuGet dependencies on principle. Feasible for `ITaskManager` (small interface, only `QueueIfNotRunning<T>` is used), impractical for `IUserManager` (dozens of members) unless the plugin's own usage is narrow enough that a partial stub covering only `CreateUserAsync`/`UpdateUserAsync`/`DeleteUserAsync` (the three methods `EmbyAuthenticationProvider` actually calls) is acceptable and the team is willing to keep extending it whenever a new call site is added. |
| Microsoft.EntityFrameworkCore.Sqlite (`:memory:` connection) for `DefaultLoginMethod`/`EmbyLoginMethodUsers` tests | Microsoft.EntityFrameworkCore.InMemory | Never for these two classes: `DefaultLoginMethod.MoveAsync` calls `ExecuteUpdateAsync`, which the InMemory provider does not support and has no plan to support — it throws `InvalidOperationException` ("could not be translated"), not a helpful error ([dotnet/efcore#30521](https://github.com/dotnet/efcore/issues/30521), confirmed by the EF team: "`ExecuteUpdate` is not supported for the in-memory provider, and we have no plans to add support"). InMemory remains fine for any *future* test that only reads/writes via `SaveChangesAsync` and needs no relational SQL translation. |
| k6 for load generation | `bats` + backgrounded `curl` loops (no new tool) | If the load test only needs "does it work under N concurrent logins" without latency percentiles or ramping. Rejected here because CONCERNS.md's three performance bottlenecks (Emby-call serialization inside Jellyfin's login lock, user-list cache-expiry stampede, fingerprint-file write contention) all need measured p50/p95 numbers, not just pass/fail, and hand-rolled `curl` loops in bash cannot report percentiles or ramp virtual users cleanly. |
| Node built-in `node:test` + jsdom | Vitest (or Jest) + jsdom/happy-dom | If the team anticipates a much larger JS test suite later (watch mode, coverage reports, snapshot testing) where Vitest's DX pays for its dependency weight. For one HTML page's inline script, the built-in runner is proportionate and needs no bundler config. |
| gitleaks | trufflehog | If the team wants *verified* credentials (trufflehog tests candidate secrets against live provider APIs) rather than pattern/entropy detection. Not needed here — the goal is "does any commit contain something that looks like a secret," not "which of these leaked keys still work," and trufflehog's verification step needs network egress during the scan, which is unnecessary risk for a one-time history audit. |
| GitHub Pages (`actions/deploy-pages`) for the manifest URL | A committed `manifest.json` on a plain branch, served via `raw.githubusercontent.com` | Simpler (no Pages job, no extra Actions), and several third-party Jellyfin plugin repos do exactly this. Rejected as the primary recommendation because `raw.githubusercontent.com` is not a CDN with the same caching/availability guarantees as Pages, and a real-world example found during research (`GeiserX/smart-covers`) specifically moved *away* from an inline Pages deploy after a transient `deploy-pages` failure stranded their catalog on a stale version — the fix was decoupling the Pages publish into its own workflow triggered by a `manifest.json` change, which is the pattern this file recommends below. Either is acceptable; Pages is the more common convention in this ecosystem ([Kevinjil/jellyfin-plugin-repo-action](https://github.com/Kevinjil/jellyfin-plugin-repo-action), [LizardByte/jellyfin-plugin-repo](https://github.com/LizardByte/jellyfin-plugin-repo)). |
| Extend `scripts/package.sh` with `jq` to merge into an existing published `manifest.json` | Adopt `Kevinjil/jellyfin-plugin-repo-action` (or `LizardByte/jellyfin-plugin-repo`) wholesale | Use one of these actions instead if the team wants the manifest rebuilt from *all* GitHub releases on every run (self-healing if a manifest commit is ever lost) rather than incrementally merged. Rejected as the default recommendation here because `scripts/package.sh` already generates a correct single-version `meta.json`/`manifest.json` with `jq` and `openssl`, has bats tests (`tests/scripts/package.bats`), and pulling in a third-party composite action (a different maintainer, a different language, a broader trust surface) to do a job three more lines of `jq` can do conflicts with "justify new dependencies." Revisit this if the jq-merge approach turns out to drift from the real GitHub release list over time. |
| `gh api repos/{owner}/{repo}/commits/{sha}/check-runs` polling loop in the release workflow, gating `gh release create` on `ci-success` | GitHub tag protection rulesets with "require status checks" | Rulesets do not support this: GitHub's ruleset docs describe "require status checks before merging" as a branch-merge concept; a pushed tag has no merge step for a required check to gate, so tag rulesets can only restrict *who* may create/delete a tag, not require CI to have passed on the tagged commit first ([GitHub docs](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets)). A workflow-level poll of `check-runs` for the tag's commit SHA — filtering out the release workflow's own check — is the pattern real-world repos use for exactly this gate (see the `orch8-io/engine` and `momics/iroh-http` examples found during research). Confidence: MEDIUM — this is inferred from example workflows, not an official GitHub-documented recipe. |

## What NOT to Use

| Avoid | Why | Use Instead |
|-------|-----|--------------|
| Moq | The 2023 SponsorLink incident (Moq silently added a telemetry-collecting dependency in a patch release) makes it a poor fit for a repo whose own rules forbid ever logging secrets or adding surprise network calls from test code. | NSubstitute |
| Microsoft.EntityFrameworkCore.InMemory | Does not support `ExecuteUpdateAsync`/`ExecuteDeleteAsync` at all, by the EF team's explicit design decision, and the failure mode (a generic "could not be translated" exception) does not even point at the real cause. `DefaultLoginMethod.MoveAsync` is built entirely around `ExecuteUpdateAsync`. | Microsoft.EntityFrameworkCore.Sqlite with an open `:memory:` connection |
| `WebApplicationFactory<T>` / ASP.NET Core `TestServer` for `EmbyAuthController` | `EmbyAuthController` is a plain `ControllerBase` with three constructor-injected services and no middleware, routing, or `HttpContext` dependency in its two actions (`GetMigrationStatus`, `RunMigration`). Spinning up a hosting pipeline to unit test it adds startup cost and a class of flakiness (port binding, middleware ordering) for no return — the e2e suite already covers it end-to-end, including the real `[Authorize(Policy = Policies.RequiresElevation)]` 403 behavior. | Instantiate `new EmbyAuthController(dbContextFactory, verifiedPasswords, taskManager)` directly, call the action methods, and assert on the returned `ActionResult<T>` — the same in-process, no-host style already used for every other unit test in this repo. |
| Vitest / Jest for the `configPage.html` tests | Adds a bundler-adjacent dependency tree (and, for Jest, a jsdom version pin that can lag current jsdom) to test one page's inline script, when Node's built-in `node:test` + `mock` already covers running the script and spying on `ApiClient`/`Dashboard` calls. | `node:test` + `jsdom` |
| trufflehog for the one-time pre-public history scan | Its strongest feature — verifying candidate secrets against live provider APIs — is unneeded risk here: it needs outbound network calls to third-party APIs during the scan, and if any of the "leaked" values happen to still be live, that's an unwanted side effect during an audit whose only goal is detection before going public. | gitleaks (`gitleaks git --source .`) |
| A GitHub tag protection ruleset as the *only* release gate | As detailed in Alternatives Considered, rulesets cannot require a status check to have passed before a tag exists — they only restrict who can push/delete the tag. Relying on this alone would let a `v*` tag trigger a release from a commit that never ran CI. | The `gh api .../check-runs` poll pattern in `release.yml`, described above |

## Stack Patterns by Variant

**If the maintainer decides the settings-page JS should also get a coverage report:**
- Add `c8` (the V8-native coverage tool that already ships as the engine behind `node --test --experimental-test-coverage`, no config file needed) instead of reaching for Istanbul/nyc.
- Because Node's built-in test runner already has a `--experimental-test-coverage` flag wired to V8's coverage counters — no separate instrumentation step.

**If the load test later needs to run inside CI (not just locally) with the full Docker Compose stack:**
- Keep k6 as a `mise`-pinned host CLI (as recommended above), not the `grafana/k6` Docker image, so it can reach the compose stack's already-published host ports (`EMBY_PORT`, `JELLYFIN_PORT`) the same way `bats e2e` does, with no extra Docker network wiring.
- Add a `[tasks.load-test]` entry to `.mise.toml` mirroring the existing `[tasks.e2e]` — same `emby_ready`/`jellyfin_ready` wait pattern from `e2e/helpers.bash`, different tool.

**If the public manifest needs to support pre-release ("nightly") builds later:**
- Do not solve this now — `scripts/package.sh check-tag` and the `v*` tag trigger are both built around one tag meaning one release. Revisit the manifest-merge `jq` step's `status` field (`"Active"` vs a pre-release marker) only if that requirement is explicitly added to a later milestone.

## Version Compatibility

| Package A | Compatible With | Notes |
|-----------|------------------|-------|
| NSubstitute 6.2.0 | net10.0, xunit.v3 4.0.1, Microsoft.Testing.Platform | NSubstitute is test-framework-agnostic; its own CI added .NET 10 to its test matrix and migrated its own tests to Microsoft.Testing.Platform mode, the strongest available signal of compatibility with this repo's exact runner choice. |
| Microsoft.EntityFrameworkCore.Sqlite 10.0.11 | `Jellyfin.Database.Implementations` 12.1.0 (`Microsoft.EntityFrameworkCore.Relational >= 10.0.11`) | Matching the floor version avoids a scenario where the test project's EF Core assembly is older than the one `JellyfinDbContext` itself was compiled against. |
| Node.js 24.21.0 | jsdom 29.1.1 | jsdom's own npm listing warns "the latest versions of jsdom require newer Node.js versions" without stating the exact floor in the fetched excerpt — confirm the `engines` field in jsdom's `package.json` at implementation time; Node 24 (current Active LTS) is very unlikely to be too old, but this is unverified from the search results alone. |
| k6 2.2.0 | Docker Compose stack from `e2e/compose.yaml` | No direct dependency — k6 talks to Jellyfin over HTTP on the host-published port, the same integration point `e2e/helpers.bash`'s `api`/`status` functions use. |

## Sources

- [NSubstitute v6.2.0 release notes](https://github.com/nsubstitute/NSubstitute/releases/tag/v6.2.0) — version, .NET 10 support
- [NSubstitute CHANGELOG.md](https://github.com/nsubstitute/NSubstitute/blob/main/CHANGELOG.md) — target framework history, Microsoft.Testing.Platform migration
- [Jellyfin.Database.Implementations 12.1.0 on NuGet](https://www.nuget.org/packages/Jellyfin.Database.Implementations/12.1.0) — EF Core Relational floor version, Polly dependency, net10.0 target
- [JellyfinDbContext.cs source](https://github.com/jellyfin/jellyfin/blob/master/src/Jellyfin.Database/Jellyfin.Database.Implementations/JellyfinDbContext.cs) — constructor shape (`IJellyfinDatabaseProvider`, `IEntityFrameworkCoreLockingBehavior`), confirms `ExecuteUpdateAsync` bypasses `SaveChangesAsync`/locking behavior entirely
- [IEntityFrameworkCoreLockingBehavior.cs (Fossies mirror)](https://fossies.org/linux/jellyfin/src/Jellyfin.Database/Jellyfin.Database.Implementations/Locking/IEntityFrameworkCoreLockingBehavior.cs) — interface shape for a hand-written test double
- [dotnet/efcore#30521 — ExecuteUpdate not supported on InMemory](https://github.com/dotnet/efcore/issues/30521) — EF team's explicit "no plans to add support" statement
- [dotnet/efcore#38010 / #38056 — SQLite ExecuteUpdate with navigation properties](https://github.com/dotnet/efcore/issues/38010) — confirms the known SQLite `ExecuteUpdate` limitation is scoped to navigation-property joins, not the simple scalar-column `Where`/`SetProperty` pattern `DefaultLoginMethod.MoveAsync` uses
- [Microsoft.EntityFrameworkCore.Sqlite 10.0.12 on NuGet](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Sqlite/10.0.12) — current patch train alongside the recommended 10.0.11 floor
- [nodejs/Release README](https://github.com/nodejs/release/blob/main/README.md) — LTS schedule
- [Node.js on endoflife.date](https://endoflife.date/nodejs) — 24.21.0 as latest patch, dated 2026-09-08
- [jsdom on npm](https://www.npmjs.com/package/jsdom) — version 29.1.1, published 2026-04-30
- [grafana/k6 v2.2.0 release](https://github.com/grafana/k6/releases/tag/v2.2.0) — version, 2026-08-10
- [gitleaks releases](https://github.com/gitleaks/gitleaks/releases) — v8.30.1
- [mise registry.toml](https://github.com/jdx/mise/blob/d10c7ca13e20a62eb8768ddbae7e4dc1a63f3ceb/registry.toml) — confirms `gitleaks` and `k6` are both first-class `mise` tools
- [Shopify/toxiproxy releases](https://github.com/shopify/toxiproxy/releases) — v2.12.0, 2025-03-18
- [Kevinjil/jellyfin-plugin-repo-action](https://github.com/Kevinjil/jellyfin-plugin-repo-action) — the ecosystem-standard manifest-generation action, evaluated and not adopted (see Alternatives Considered)
- [LizardByte/jellyfin-plugin-repo](https://github.com/LizardByte/jellyfin-plugin-repo) — a second real-world example of the GitHub Pages manifest pattern
- [GeiserX/smart-covers pages.yml](https://github.com/GeiserX/smart-covers/blob/3b762c174c20dbc6c72d5c4d05415014e6454535/.github/workflows/pages.yml) — the decoupled-Pages-deploy rationale cited above
- [GitHub docs — Available rules for rulesets](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets) — required status checks are a merge concept, not a tag-push concept
- [orch8-io/engine commit 0f8cfcb](https://github.com/orch8-io/engine/commit/0f8cfcb978de8ef92ce34bf6822cd9aab5d0ba2d) and [momics/iroh-http publish.yml](https://github.com/momics/iroh-http/blob/915b1a990df74966bc70f9ee41507d2d092ce989/.github/workflows/publish.yml) — real-world `check-runs` polling patterns for gating a release workflow on the tagged commit's CI status

---
*Stack research for: Jellyfin Emby Auth plugin — testing, load testing, secret scanning, and public distribution (v1.0.0 milestone)*
*Researched: 2026-09-17*
