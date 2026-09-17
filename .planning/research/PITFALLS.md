# Pitfalls Research

**Domain:** Jellyfin authentication plugin — preparing a brownfield C#/.NET 10 plugin for a public v1.0.0 (public repository, plugin-manifest catalog, release CI, load testing, and closing known test/bug gaps)
**Researched:** 2026-09-17
**Confidence:** MEDIUM-HIGH (mixed: several pitfalls confirmed by reading this repo's code and official GitHub/Jellyfin/EF Core sources; a few are general patterns from community sources, marked accordingly)

Every pitfall below states its evidence: **confirmed** (read in this repo's code or an authoritative source), or **reported** (a community source describes the pattern; treat as a warning to test for, not a proven fact about this repo).

## Critical Pitfalls

### Pitfall 1: Treating history rewrite as a fix for a private-to-public flip

**What goes wrong:**
A maintainer finds a secret in git history, runs `git filter-repo` or BFG, force-pushes, and flips the repository to public — believing the secret is gone. It is not: any fork, clone, PR reference, or GitHub's own cached view still holds the original blob, and GitHub Support will only intervene for a genuine secret, not general history cleanup.

**Why it happens:**
History rewriting looks like a complete fix because the default branch no longer shows the file. Forks and PR references are invisible from the repository's own UI, so they are easy to forget. This repository currently has no forks (it is private), so the *decision point* — audit before any fork can exist — is now, not after going public.

**How to avoid:**
- Run a full-history secret scan (not a working-tree scan) before flipping visibility: `git rev-list --all --objects | git cat-file --batch` piped through a token-pattern grep, or a dedicated tool (`gitleaks git .` / `trufflehog git file://.`).
- Also check GitHub Actions logs and artifacts, old releases, issue/PR bodies and comments, and the wiki — these become public with the repository and are not part of a git-history scan (confirmed: GitHub's own docs state "Actions history and logs will be visible to everyone" on the visibility-change page).
- If a real secret is found: rotate/revoke it first (revocation neutralizes it regardless of history state), then decide whether a history rewrite is still worth the disruption. GitHub's own guidance: "history rewriting … may not be warranted" once the secret is revoked.
- If the repository is judged clean, keep the current repository (no need to recreate it) — the maintainer's chosen path in this milestone (fix in place, then flip visibility) is consistent with community guidance when the audit is clean.
- This is the one step that cannot be fixed after publication — do it before the visibility toggle, not after.

**Warning signs:**
- Any `.env`, `*-config.json`, `*.pem`, or editor-backup file (`*~`, `.bak`, `.orig`) that is `git ls-files`-tracked.
- A `git grep` scan that returns zero hits when you know a value is present — check the regex is not silently broken (POSIX ERE via `git grep -E` does not support `\b`; use `-P` or drop the anchor).
- CI logs from a **failed** run — error paths often print unmasked values that success paths never reach.

**Phase to address:** Public release phase, as the first step (before manifest work, before flipping visibility).

Sources: [GitHub Docs — Setting repository visibility](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/managing-repository-settings/setting-repository-visibility) (confirmed, official); [GitHub Docs — Removing sensitive data from a repository](https://github.com/github/docs/blob/main/content/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository.md) (confirmed, official); [trailofbits open-sourcing skill](https://github.com/trailofbits/skills/blob/main/plugins/open-sourcing/skills/open-sourcing/SKILL.md) (reported, practitioner checklist).

---

### Pitfall 2: A `v*` tag push does not run lint or e2e before release

**What goes wrong:**
`release.yml:6-9` triggers on any pushed `v*` tag and runs `check-tag`, `mise run test` (unit + script tests only), and `mise run package` — it never runs `mise run lint` or `mise run e2e`. A tag pushed from a commit that never went through `ci.yml` (for example, a local tag on an unpushed branch, or a tag on a commit whose PR CI run was cancelled) can produce a public release zip that was never linted or exercised against a real Jellyfin/Emby pair.

**Why it happens:**
Tag-triggered release workflows are usually written assuming "if it's tagged, it already passed CI on `main`." Nothing enforces that assumption at the workflow level — GitHub does not correlate a tag's commit with a prior successful `ci-success` run unless a ruleset requires it.

**How to avoid:**
- Add a required check before publishing: either a tag protection ruleset restricting who/what can push `v*` tags, or a workflow step that calls the GitHub API to confirm the tagged commit has a passing `ci-success` check run, and fails the release job otherwise.
- This is exactly requirement item (7) in `.planning/PROJECT.md`: "A release is published only from a commit that passed lint, unit tests, script tests, and e2e tests." Confirmed unimplemented today (`CONCERNS.md` "Releases skip lint and e2e").
- Do not simply add `mise run lint` and `mise run e2e` to `release.yml` as a substitute — that re-runs checks instead of verifying they already passed on the actual merge commit, and adds ~30 minutes to every release for e2e alone (`ci.yml:49` timeout is 30 minutes).

**Warning signs:**
- A release exists whose tag's commit has no corresponding `ci-success` run in the Actions history, or whose `ci-success` run shows a failure/cancellation.
- `git log` shows the tag pointing at a commit not reachable from `main`.

**Phase to address:** Release and tooling phase — before v1.0.0 is tagged, since this is the exact gate the milestone needs for a trustworthy first public release.

Sources: `.github/workflows/release.yml:6-9`, `:32-41` (confirmed, this repo); `.github/workflows/ci.yml:5-9`, `:64-81` (confirmed, this repo); `.planning/codebase/CONCERNS.md:124-127` (confirmed, this repo).

---

### Pitfall 3: `GITHUB_TOKEN`-authenticated tag pushes never trigger `on: push: tags`

**What goes wrong:**
If any future automation (a version-bump bot, a release script run from another workflow) pushes the `v*` tag using the default `GITHUB_TOKEN`, GitHub silently suppresses the resulting `push` event to prevent infinite trigger loops. `release.yml` never fires; the tag exists with no release, no zip, and no manifest update — a silent no-op, not a visible failure.

**Why it happens:**
This is an intentional GitHub anti-recursion guard, but it is not visible in the workflow file or the Actions UI — the tag push "succeeds" and nothing indicates a workflow should have run.

**How to avoid:**
- This repository currently tags manually (`git tag vX.Y.Z && git push`) with the maintainer's own credentials, which is unaffected — flag this only if a future phase adds an automated tag-and-push step (for example, a version-bump PR merge that auto-tags).
- If that is ever added, the tag push must use a PAT or GitHub App token, not `GITHUB_TOKEN`, or must trigger `release.yml` explicitly via `workflow_dispatch`/`gh workflow run` instead of relying on the tag-push event.

**Warning signs:** A `v*` tag appears in the repository with no corresponding entry on the Releases page and no run in the Actions tab for `release.yml`.

**Phase to address:** Not applicable to the current manual-tag workflow; flag as a constraint if a future milestone automates tagging.

Sources: [github/spec-kit#1736](https://github.com/github/spec-kit/pull/1736) (confirmed, real-world fix); [googleapis/release-please#1142](https://github.com/googleapis/release-please/issues/1142) (confirmed, GitHub-acknowledged behavior).

---

### Pitfall 4: `zizmor --persona=pedantic` findings get suppressed instead of fixed, or fixed with an unhelpful reason

**What goes wrong:**
The default `zizmor --offline` run is already clean (`CONCERNS.md`: "No findings to report", 7 suppressed). The pedantic run surfaces 7 more (5 informational, 2 low) including missing `concurrency:` on `release.yml` and no comment on the `contents: write` grant. Requirement (9) says each pedantic finding must be "fixed or suppressed with a written reason" — the failure mode is writing `# zizmor: ignore[finding-id]` with no reason, or with a reason that goes stale (for example, referencing a pinned SHA that Dependabot later updates, breaking the association between comment and pin).

**Why it happens:**
Pedantic findings are, by definition, code smells rather than proven exploits (zizmor's own docs: "include code smells, even if not likely exploitable"). It is tempting to blanket-suppress the noisy ones without documenting why each specific case is safe.

**How to avoid:**
- For `cache-poisoning`: this repo does not appear to use `actions/cache` or a cache-aware setup action in `release.yml` (worth confirming), so this finding class is unlikely to fire — but if any caching action is added later to speed up the release job, either disable caching there explicitly (`cache: false`, as `release.yml` already does for `jdx/mise-action`) or accept the finding with a reason tied to the actual cache scope, not the pin comment.
- For `template-injection`: audit every `${{ ... }}` expansion inside a `run:` block across both workflows for attacker-controlled contexts (`github.event.*`, PR titles/branch names) — none currently appear to consume PR/issue-controlled input, but re-check after any workflow edit.
- Put the suppression reason on its own line near the finding, not tied to a version pin comment that a bot will rewrite.

**Warning signs:** `zizmor --persona=pedantic .github/workflows` shows a finding with no adjacent `# zizmor: ignore[...]` comment, or a suppression comment that repeats the tool's own description instead of stating why *this* workflow is safe.

**Phase to address:** Release and tooling phase.

Sources: [zizmor Audit Rules](https://docs.zizmor.sh/audits/) (confirmed, official docs); `.planning/codebase/CONCERNS.md:109-116` (confirmed, this repo, measured).

---

### Pitfall 5: A `manifest.json` checksum in the wrong case, or an unpinned `sourceUrl`, silently breaks install/update for some Jellyfin builds

**What goes wrong:**
`scripts/package.sh:76-77` computes the checksum with `openssl dgst -md5 -r` (lowercase hex). Jellyfin's plugin manifest schema documents `checksum` as an MD5 hex string (some real-world manifests, including the `mintlify.wiki/jellyfin` reference, show a `sha256:` prefix form for other manifests) — a manual edit or a future switch to a different hash tool that emits uppercase hex, or a checksum algorithm mismatch between what `package.sh` computes and what a manually-edited manifest entry claims, breaks silently: Jellyfin either rejects the download as corrupt or (worse) never surfaces why an install fails.

**How to avoid:**
- Keep the checksum generation single-sourced in `scripts/package.sh` (already the case) and never hand-edit `manifest.json` checksums.
- `tests/scripts/package.bats:54-65` already asserts the checksum matches the built zip — keep this test, and extend it if the manifest ever needs a `sha256:`-prefixed form for compatibility with the reference examples seen in Jellyfin's own docs.
- `check-tag` already fails the release before packaging if the tag doesn't match `Directory.Build.props` (confirmed, `scripts/package.sh:41-47`); this closes the most common source of tag/version drift.

**Warning signs:** A user reports "plugin failed to download" or "checksum mismatch" after an otherwise successful release; `openssl dgst` output case changes between tool versions.

**Phase to address:** Release and tooling / public-install phase.

Sources: `scripts/package.sh:76-90` (confirmed, this repo); [Jellyfin plugin manifest reference](https://mintlify.wiki/jellyfin/jellyfin/concepts/plugins) (reported, third-party mirror of Jellyfin docs, shows `sha256:` form used elsewhere — treat as an alternate valid form, not a requirement).

---

### Pitfall 6: `targetAbi` is a minimum, not a maximum — a manifest with no upper bound offers incompatible future versions

**What goes wrong:**
Jellyfin's `PluginManager` treats `targetAbi` as the *minimum* compatible server version, not an exact or maximum match (confirmed via Jellyfin's own source and a maintainer comment: "The targetAbi property is only used to specify a minimum version, not a maximum. So a plugin for 10.8.z is allowed to be installed with 10.9. This is intentional behavior"). Because this plugin is pinned to Jellyfin 12.1.0 packages only (`README.md:16`, `.csproj:10-15`) and untested against any other Jellyfin version, a future Jellyfin 12.2 or 13.0 server will still see this plugin as installable — and it may load with subtly broken behavior rather than a clean refusal, since `NotSupported` status only triggers when the running server is *older* than `targetAbi`, not newer.

**How to avoid:**
- Document this limit explicitly wherever the manifest or README states compatibility: "tested and supported on Jellyfin 12.1.x only; newer major/minor versions may install but are unverified."
- Do not rely on `targetAbi` alone to gate compatibility for administrators; this is a real, currently-open Jellyfin core limitation (issue #11331, still open with a proposed `maximumAbi` field not yet merged as of this research).
- If a future Jellyfin version bump changes plugin-loading assumptions, re-test before assuming the old zip still works — the plugin catalog will offer it regardless.

**Warning signs:** A user on a newer Jellyfin server installs this plugin from the manifest without issue, then reports `Status: NotSupported` was *not* shown, or reports subtle authentication failures on an untested Jellyfin version.

**Phase to address:** Public-install phase (documentation), not a code fix — this is a Jellyfin core behavior outside this plugin's control.

Sources: [jellyfin/jellyfin#11331](https://github.com/jellyfin/jellyfin/issues/11331) (confirmed, official Jellyfin repo, open issue with maintainer confirmation "Working as intended"); `.planning/codebase/CONCERNS.md:131-133` (confirmed, this repo).

---

### Pitfall 7: `raw.githubusercontent.com` and GitHub Pages both cache — a just-published manifest or release can appear stale for minutes

**What goes wrong:**
If the manifest is ever served from `raw.githubusercontent.com` (a branch file) rather than from a GitHub Release asset, requests can return cached content for roughly five minutes per edge/IP with no cache-busting mechanism available (confirmed via a maintainer/Stack Overflow answer and reproduced testing: "raw addresses impose a cache of about 5 minutes per IP, which cannot be bypassed in any way"). GitHub Pages, if chosen instead, serves through a CDN with its own TTL (observed elsewhere as `cache-control: max-age=600`, i.e. up to 10 minutes) plus edge propagation delay, and a Pages deploy can itself fail without an obvious signal in the plugin catalog (it just keeps serving the previous manifest).

**How to avoid:**
- `.planning/PROJECT.md` already frames the manifest as living "on a stable URL" with zips as GitHub release files — prefer a GitHub Release asset (`manifest.json` uploaded alongside the zip, as `release.yml` already does at `gh release create ... artifacts/release/manifest.json`) or a raw file served from a tag/release ref rather than a moving branch ref, since a release asset is immutable per-release and does not need cache-busting for *that* version's content.
- If the "one manifest lists every version" requirement means the manifest itself must be mutable at a stable URL (it must be — each new release adds a version to the same file), it cannot live only as a release asset per version; it needs a single location that gets overwritten. Decide explicitly between: (a) a `raw.githubusercontent.com` URL pointing at a maintained branch file that the release workflow commits to, or (b) GitHail Pages. Either way, budget for a 5–10 minute propagation window after each release before verifying the manifest update, and do not add a cache-busting query string to the *administrator-facing* URL (Jellyfin's own HTTP client will not send one, so testing with one proves nothing about what users see).
- If a verification step is added to the release workflow (polling the live manifest URL to confirm the new version appears), size its timeout above the observed cache TTL plus propagation margin — a real-world fix elsewhere raised a 300s poll to 900s for exactly this reason.

**Warning signs:** A release completes successfully but the manifest URL still shows the previous version for several minutes; a Pages deploy job fails (or is skipped) with no accompanying alert, silently freezing the catalog at the last successful deploy.

**Phase to address:** Public-install phase — this decision (where the manifest lives, mutable vs. release-pinned) needs to be made explicitly before v1.0.0, since `.planning/PROJECT.md` Key Decisions table still shows "Public repository with one stable manifest that lists every version" as Outcome "— Pending."

Sources: [Stack Overflow — Avoiding cached content from raw.githubusercontent.com](https://stackoverflow.com/questions/64792450/avoiding-getting-cached-content-from-raw-githubusercontent-com) (confirmed, includes GitHub-staff-adjacent explanation of the ~5 min cache); [everything-presence-pro-grid#352](https://github.com/clintongormley/everything-presence-pro-grid/pull/352) (reported, real-world Pages-CDN-TTL fix, different project but directly analogous pattern); `.planning/PROJECT.md:81` (confirmed, this repo, describes the third-party convention).

---

### Pitfall 8: `EmbyVerifiedPasswords` fingerprint semantics get changed without re-verifying the core value

**What goes wrong:**
The core value is: "no password that Emby did not verify ever opens an account." Two of the "Active" requirement items ((1) failed fingerprint write, (4) failed fingerprint read) touch the exact mechanism that enforces this — the in-memory dictionary and the on-disk file that `Matches` checks before any move to Default or any saved-password login. A fix that changes *when* the in-memory dictionary is populated (for example, moving the `Record` call to only add the entry after `File.Move` succeeds, per `CONCERNS.md`'s suggested fix approach) changes the actual security property, not just the log message — if done carelessly, it could make a user un-movable to Default even after Emby verified them, or worse, leave the record only in memory long enough to be read by a concurrent `Matches` call before the file write confirms.

**Why it happens:** The three failure-group items (bugs, docs mismatch, missing test) look like a self-contained cleanup, but `EmbyVerifiedPasswords` is the one shared gate that every login path in this plugin depends on. A change here has the widest blast radius in the codebase.

**How to avoid:**
- Write the test first (per repo rule), specifically one that forces a write failure and asserts the resulting `Matches` behavior for the *decided* semantics — not just that "some" log line appears.
- Keep the fix scoped to matching docs/log to the actual (already-correct-for-security) behavior, per `.planning/PROJECT.md`'s own Key Decision: "A failed fingerprint write keeps the in-memory record; fix the docs and log, add a test. Emby did verify the password, so the rule holds." Do not use this phase to "fix" the in-memory-first ordering unless the requirements review explicitly decides to change the security semantics — that decision is currently marked Pending and needs the maintainer's confirmation, per the same table.
- Any change to `EmbyVerifiedPasswords.Record`/`Load`/`Matches` needs an e2e test that forces the underlying file operation to fail (a locked file, a read-only directory) in addition to the existing unit tests, since this is exactly the kind of Jellyfin/Emby-adjacent behavior the repo rule requires e2e coverage for.

**Warning signs:** A unit test for the write-failure path passes by asserting only the log message, not the subsequent `Matches` result for that user across a simulated restart.

**Phase to address:** Bug-fix phase covering `EmbyVerifiedPasswords` (items 1 and 4).

Sources: `.planning/codebase/CONCERNS.md:11-45` (confirmed, this repo); `.planning/PROJECT.md:37-38`, `:101-102` (confirmed, this repo — states the core value and the pending decision explicitly).

---

### Pitfall 9: Fixing the blank-password window with a full `UpdateUserAsync` reintroduces the concurrent-overwrite bug it's meant to fix

**What goes wrong:**
`CONCERNS.md` documents the blank-password window between `CreateUserAsync` (commits an account on Default with no password) and the second call that saves the hash (`EmbyAuthenticationProvider.cs:189`). A naive fix — saving the password hash in the *same* call as account creation, or retrying the save with a fresh full-entity `UpdateUserAsync` — can reintroduce the exact class of bug that `DefaultLoginMethod.MoveAsync` was written to avoid: a full `UpdateUserAsync` overwrites the whole row, so if `CreateUserAsync` and the password-save race against any other write to that same user (unlikely for a brand-new user, but the delete-on-failure path already shows this account is not fully isolated), a full entity save can silently drop concurrent changes. Jellyfin core itself has an open, confirmed bug in 10.11.x (`jellyfin#16353`) where two near-simultaneous writes to the same `Users` row race on the EF Core `RowVersion` concurrency token and throw `DbUpdateConcurrencyException`, permanently blocking further logins for that user until a restart or a manual `RowVersion` reset — this plugin runs on Jellyfin 12.1, and while the specific issue is filed against 10.11.x, the underlying pattern (EF Core `RowVersion` concurrency tokens on the shared in-memory `User` singleton, mixed with short-lived `DbContext` instances from `IDbContextFactory`) is a Jellyfin-core mechanism this plugin's code directly touches via `IDbContextFactory<JellyfinDbContext>` in `MoveToDefaultLoginMethod`, `EmbyLoginMethodUsers`, and the migration task.

**Why it happens:** The natural fix for "there's a window without a password" is "close the window by writing the password sooner or in fewer steps," which nudges toward fewer, larger writes — the opposite of what avoids concurrency races.

**How to avoid:**
- Prefer the narrowest possible write for the password-hash save, mirroring the existing `DefaultLoginMethod.MoveAsync` pattern (`ExecuteUpdateAsync` on a `Where` predicate that only matches the intended row-state), rather than a full-entity `UpdateUserAsync` — this avoids `RowVersion` staleness entirely, since `ExecuteUpdateAsync` performs a direct `UPDATE ... WHERE ...` rather than an optimistic-concurrency-checked entity save.
- Whatever fix is chosen, verify the account-creation path with a concurrency-focused unit/e2e test (two near-simultaneous first logins for the same new name), and check what Jellyfin 12.1's `UserManager.AuthenticateUser`/`CreateUserAsync` actually does under contention — this repo's own `CONCERNS.md` already flags this as unverified.
- Note: two concurrent first logins for an *unknown* username are naturally serialized by Jellyfin's shared `Guid.Empty` lock key (`.claude/rules/plugin.md:14`, confirmed) — so the primary risk is not two logins racing each other, but the create→save→(delete-on-failure) sequence racing against something *else* touching the same row (an admin action, a scheduled task, Jellyfin's own background maintenance). Scope the concurrency test accordingly.

**Warning signs:** A test that creates a user and immediately triggers a second, unrelated write to that user (e.g., a policy update) intermittently throws `DbUpdateConcurrencyException` or silently loses one of the two writes.

**Phase to address:** Bug-fix phase covering account creation (item 3).

Sources: [jellyfin/jellyfin#16353](https://github.com/jellyfin/jellyfin/issues/16353) (confirmed, official Jellyfin repo, open issue with root-cause analysis and in-progress core PR #16354); `src/Jellyfin.Plugin.EmbyAuth/DefaultLoginMethod.cs:31-41` (confirmed, this repo, shows the narrow-update pattern already in use elsewhere in this codebase); `.planning/codebase/CONCERNS.md:28-36` (confirmed, this repo); `.claude/rules/plugin.md:14` (confirmed, this repo).

---

### Pitfall 10: `ExecuteUpdateAsync` cannot be unit-tested with EF Core's InMemory provider at all

**What goes wrong:**
`DefaultLoginMethod.MoveAsync` (`DefaultLoginMethod.cs:38`) uses `ExecuteUpdateAsync`, and any new code written to close the account-creation blank-password window (Pitfall 9) is likely to use the same pattern. `ExecuteUpdateAsync`/`ExecuteDeleteAsync` require SQL translation and are unsupported on the EF Core InMemory provider — calling them against an `InMemoryDatabase`-backed context throws (confirmed: multiple EF Core team responses state "these new extensions only work on relational providers... InMemory is not a relational provider," and a dedicated GitHub issue asks for a clearer error because the default failure message is a generic "could not be translated" error that misleads developers into debugging the wrong thing). Item (10) in the Active requirements calls for unit tests on `MoveToDefaultLoginMethod`, `MoveEmbyUsersToDefaultTask`, and other `JellyfinDbContext`-dependent types that don't exist yet — reaching for the InMemory provider (the simplest-looking option, and the one implied by this project's "no mocking library, hand-written test doubles" convention in `TestDoubles.cs`) will silently fail for any code path touching `DefaultLoginMethod` or the migration task's `ExecuteUpdateAsync`/`ExecuteDeleteAsync`-style calls.

**Why it happens:** InMemory is the path of least resistance for a first EF Core-touching unit test in a project that has never needed one before (`git diff --stat 0dc87e4 ecee1ed -- tests/...` is empty; no `JellyfinDbContext` test double exists yet, per `TESTING.md`). The failure surfaces as a confusing translation error rather than an obvious "wrong provider" message.

**How to avoid:**
- Use the SQLite in-memory provider (`Microsoft.Data.Sqlite` + `UseSqlite(openConnection)`, keeping one open `SqliteConnection` alive for the test's lifetime) for any test that exercises `DefaultLoginMethod.MoveAsync`, `MoveEmbyUsersToDefaultTask`, or `EmbyLoginMethodUsers` — this is a real relational engine and supports `ExecuteUpdateAsync`.
- Remember SQLite does not enforce foreign keys by default; run `PRAGMA foreign_keys = ON;` on the connection if any future test needs FK enforcement (not currently a concern for this plugin's schema use, but worth stating as a standing rule for whoever adds the fixture).
- Do not assume SQLite in-memory behavior is identical to Jellyfin's actual production database. Jellyfin 12.1 supports both SQLite and other relational backends depending on configuration (unverified for this specific version — check `Jellyfin.Database.Implementations` configuration before assuming SQLite is the only production target); e2e tests against a real Jellyfin container remain the source of truth for `ExecuteUpdateAsync` correctness, per this repo's own rule ("a change that depends on Jellyfin or Emby behavior needs an e2e test").
- A known EF Core/SQLite-specific `ExecuteUpdateAsync` bug exists for `Where` clauses that reference *navigation properties* rather than raw foreign-key scalar properties (`dotnet/efcore#38010`: SQLite doesn't support a table alias in `UPDATE ... AS` syntax, producing `SQLite Error 1: 'no such column'`). `DefaultLoginMethod.MoveAsync`'s current `Where` clause only compares scalar properties (`user.Id`, `user.AuthenticationProviderId`, `user.Password`), so it is not affected today — but any future `ExecuteUpdateAsync` call that filters through a navigation property must use the FK scalar directly, not the navigation, to stay SQLite-compatible.

**Warning signs:** A new unit test for `DefaultLoginMethod` or the migration task throws `InvalidOperationException: The LINQ expression '...' could not be translated` — this is the InMemory-provider tell, not a bug in the query itself.

**Phase to address:** Test-gap phase (item 10), specifically when writing the first unit tests for `JellyfinDbContext`-dependent types.

Sources: [dotnet/efcore#29376](https://github.com/dotnet/efcore/issues/29376) (confirmed, official EF Core repo, maintainer confirms InMemory will not support these methods); [dotnet/efcore#31320](https://github.com/dotnet/efcore/issues/31320) (confirmed, official EF Core repo); [dotnet/efcore#38010](https://github.com/dotnet/efcore/issues/38010) (confirmed, official EF Core repo, open as of this research); `src/Jellyfin.Plugin.EmbyAuth/DefaultLoginMethod.cs:31-41` (confirmed, this repo); `.planning/codebase/TESTING.md:14`, `:110-113` (confirmed, this repo — no mocking library, no `JellyfinDbContext` test double today).

---

### Pitfall 11: A login-path load test that doesn't account for Jellyfin's per-account lockout counter measures nothing useful

**What goes wrong:**
Requirement items (13)-(15) call for a load test of concurrent first logins and Emby latency. Jellyfin's `UserManager` increments `InvalidLoginAttemptCount` on every failed authentication and enforces `LoginAttemptsBeforeLockout` (default 3, confirmed in Jellyfin's own `UserManager.cs` — "The default number of login attempts is 3"). A naive load-test script that retries a *failed* login (for example, a deliberately wrong password used to measure worst-case latency, or a race where two concurrent requests for the same new user both momentarily fail before one succeeds) will trip real account lockout after 3 attempts, and every subsequent iteration of that load test then measures "how fast does Jellyfin reject a locked-out user" instead of the intended login latency — silently invalidating the whole benchmark.

**Why it happens:** Load-testing scripts default to reusing one or a small number of accounts across many iterations for simplicity. Auth-flow load testing specifically needs a *pool* of distinct accounts to avoid tripping the exact mechanism meant to stop repeated failed attempts.

**How to avoid:**
- Load-test each scenario (slow Emby, concurrent first logins, cache expiry, fingerprint writes) against a large pool of distinct usernames, never the same account repeatedly, and only with *correct* passwords when measuring the "happy path" success latency.
- Separately and deliberately test the lockout path (wrong password N times) as its own scenario with its own accounts, not mixed into the throughput numbers for successful logins.
- e2e conventions already require unique per-file test users (`.claude/rules/e2e.md:19` — confirmed) and note the 60-second Emby user-list cache means users must be created once in `setup_suite.bash`, not per-test — apply the same rule to load-test account provisioning: pre-create the full pool before the cache window starts, so cache misses during the load test measure Emby round-trips, not directory-refresh cold-starts.

**Warning signs:** Load-test throughput numbers degrade sharply partway through a run with no corresponding Emby-side latency change; Jellyfin logs show `InvalidLoginAttemptCount` climbing for the same account IDs the load test reuses.

**Phase to address:** Performance/load-test phase.

Sources: `.planning/codebase/CONCERNS.md:66-84` (confirmed, this repo — the three unmeasured bottlenecks); [jellyfin/jellyfin UserManager.cs](https://github.com/jellyfin/jellyfin/blob/fb763c47/Jellyfin.Server.Implementations/Users/UserManager.cs) (confirmed, official repo, shows `LoginAttemptsBeforeLockout` default-3 handling); [QASkills.sh — Account Lockout Testing](https://qaskills.sh/blog/account-lockout-testing-brute-force-thresholds) (reported, general pattern — corroborates the account-pool mitigation).

---

### Pitfall 12: Docker resource limits distort load-test numbers non-linearly, especially near aggressive quotas

**What goes wrong:**
The e2e/load-test harness already runs Jellyfin, Emby, and the logging nginx proxy in Docker Compose (`e2e/compose.yaml`). If a load test reuses this same Compose setup without explicit, generous CPU/memory allocation, the CFS (Completely Fair Scheduler) throttling behavior inside cgroups can produce results that reflect Docker's scheduling artifacts rather than the plugin's actual login-path latency — and the distortion is *non-linear*: a container capped near its steady-state usage sees negligible throttling, but a container capped even modestly below its burst need can spend a large fraction of wall-clock time blocked by the scheduler rather than doing useful work (independently reproduced benchmarks show a 10%-of-one-core limit against a workload wanting 50% produced a 100% throttle rate and 27% of total time spent throttled, while a 50%-of-one-core limit against the same workload produced under 1% throttling).

**Why it happens:** Default `docker compose` invocations (as in this repo's `e2e/compose.yaml`, which sets no CPU/memory limits per `.planning/codebase/TESTING.md` review) run unconstrained, which is fine for correctness e2e tests but hides the exact class of scheduling artifact that a load test is supposed to expose — and if limits are added later "to make it realistic," setting them too tight silently caps and distorts the very numbers the load test exists to produce.

**How to avoid:**
- If the load test runs in Docker (whether the existing e2e Compose stack or a new one), either run unconstrained (no `--cpus`/`--memory`, or very generous ones matching the host) and treat results as an upper bound, or explicitly document the applied CPU/memory limits alongside every reported number so a reader can judge whether throttling, not the plugin, produced a given latency.
- If comparing before/after a code fix, keep the container resource configuration identical between runs — a load test's *relative* numbers (did this change help?) are more trustworthy than its *absolute* numbers (is this fast enough in production?) when run in a constrained CI runner.
- Prefer running the load test on the CI runner's actual available cores rather than an artificially small `--cpus` value chosen for "realism" — GitHub-hosted runners already provide a fixed, modest core count, which is a closer analog to a typical self-hosted Jellyfin box than an arbitrarily tighter Docker limit.

**Warning signs:** Load-test latency numbers vary by more than the expected noise margin between otherwise-identical runs; `docker stats` or `cat /sys/fs/cgroup/.../cpu.stat` (`nr_throttled`/`nr_periods`) shows a high throttle rate during the test window.

**Phase to address:** Performance/load-test phase.

Sources: [opscart/container-isolation-benchmarks](https://github.com/opscart/container-isolation-benchmarks) (reported, independent reproducible benchmark with methodology and control test); `e2e/compose.yaml` (confirmed, this repo, no resource limits set today per `.planning/codebase/TESTING.md:235-237`).

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|-----------------|------------------|
| Suppressing a `zizmor --persona=pedantic` finding with a generic comment instead of a case-specific reason | Faster to close item (9) | The next Dependabot-triggered pin update can silently break the association between comment and finding, and a future reviewer can't tell if the suppression is still valid | Never — write the specific reason every time |
| Using EF Core InMemory provider for the first `JellyfinDbContext`-touching unit tests because it needs no fixture | Fast to write, no `SqliteConnection` lifecycle management | Any test touching `ExecuteUpdateAsync` silently cannot be written this way; the codebase then carries a mixed test-provider convention that confuses future contributors | Never for code paths using `ExecuteUpdateAsync`/`ExecuteDeleteAsync`; acceptable only for pure object-graph assertions with zero bulk-operation coverage needs |
| Loosening the manifest URL to a mutable branch file on `raw.githubusercontent.com` instead of committing to a fixed release-asset + branch-update split | Simpler release script (one write target) | Every release inherits a 5-minute-per-IP cache window with no bypass, and the maintainer has to explain "wait a few minutes" to every reporting administrator | Acceptable only if documented in `docs/` and if release verification tolerates the delay |
| Running the full load test only in the existing unconstrained e2e Compose stack, never against any resource-limited configuration | No new tooling needed | Numbers may not transfer to a self-hosted Jellyfin box that shares hardware with other services (a common real deployment for a migration-target plugin) | Acceptable for a first pass; document the environment explicitly per requirement's "measured numbers" language |

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|-----------------|-------------------|
| Emby (`EmbyClient`) | Assuming Emby's session logout always fires — a response with a token but no username skips `SignOutAsync` today (`EmbyClient.cs:88-92`, confirmed) | Fix (6) explicitly: end the Emby session whenever a token is present, regardless of whether a username parsed |
| GitHub Releases / manifest | Treating `sourceUrl` as permanently stable once published — a repository rename or a release re-tag changes the URL | Keep `RELEASE_URL_BASE` derived from the actual repository slug at build time (already done, `package.sh:23`), and never manually edit a published `sourceUrl` |
| raw.githubusercontent.com / GitHub Pages | Polling the manifest URL immediately after a release and concluding "the release is broken" when it's just cache lag | Poll with a timeout that exceeds the observed CDN TTL plus a propagation margin, and only escalate if the manifest is still stale after that window |
| Jellyfin plugin catalog (`targetAbi`) | Assuming a mismatched `targetAbi` always blocks install | It only blocks install on an *older* server than `targetAbi`; it never blocks a *newer* server (Jellyfin issue #11331) — document the supported range separately from what the manifest technically allows |

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|-----------------|
| Reusing a handful of load-test accounts across many login iterations | Throughput craters partway through a run with no Emby-side cause | Provision a large pool of distinct test users before the run; keep lockout scenarios in a separate test | After 3 failed attempts per account (Jellyfin default `LoginAttemptsBeforeLockout`) |
| Docker CPU limit set well below the container's burst need | Latency numbers vary wildly between "identical" runs; scheduler throttling in `cpu.stat` | Run unconstrained or with generous limits; keep limits identical across compared runs | Any `--cpus` quota well under observed p95 usage (reproduced: 10% quota against a 50%-hungry workload → 100% throttle rate) |
| Fingerprint file rewrite holding the same lock `Matches` uses (`EmbyVerifiedPasswords.cs:45-64`, confirmed) | Migration status/API calls block behind concurrent Emby logins under load | Measured, not assumed — requirement (13-15) already calls for this; do not ship a "fix" without a before/after number | Under concurrent Emby logins at a scale not yet measured in this repo |
| `EmbyUserDirectory` cache-miss stampede (no single-flight guard, `EmbyUserDirectory.cs:58-64`, confirmed) | Duplicate Emby user-list requests when the 60s cache expires under concurrent logins | Measure under load; a single-flight guard (one in-flight refresh shared by all waiters) is the standard fix if the load test shows it matters | At cache expiry boundaries with many concurrent logins |

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| A public-facing fix for the blank-password window that widens the write instead of narrowing it (Pitfall 9) | Reintroduces a concurrent-overwrite class of bug on the exact account-creation path that guards the core value | Prefer `ExecuteUpdateAsync`-style narrow writes over full-entity `UpdateUserAsync`, and add a concurrency test before merging |
| Changing `EmbyVerifiedPasswords` write-ordering as a side effect of the docs/log fix (item 1) | Could change *when* a password becomes "verified" from the plugin's perspective, weakening the core value's window of protection | Scope the fix to docs/log/test only unless the requirements review explicitly authorizes a semantics change (currently Pending per `PROJECT.md`) |
| Publishing the repository before the full-history secret scan is genuinely complete (not just a working-tree check) | Irreversible exposure — API keys, internal URLs, or test data become public forever, regardless of later rewrite attempts | Run the full-history scan described in Pitfall 1 as a hard gate, independent of and before any other public-release work |
| Assuming `targetAbi` protects users from installing this plugin on an incompatible future Jellyfin version | Jellyfin's own catalog will offer an old-`targetAbi` plugin to a newer server without warning (confirmed core behavior) | State the tested-version boundary in docs; do not rely on the manifest field alone |

## UX Pitfalls

| Pitfall | User Impact | Better Approach |
|---------|-------------|-------------------|
| Administrator adds the manifest URL right after a release and sees the old version, or sees nothing new | Support confusion ("the release didn't work") when it is actually cache/CDN lag | Document the expected propagation window in the release notes or `docs/`, and/or verify propagation in the release workflow before declaring success |
| Settings page shows no error when `getPluginConfiguration`/`updatePluginConfiguration` fails (`configPage.html:85-93`, `:110-118`, confirmed — items 5 and 11 in Active requirements) | Administrator believes settings saved when they didn't, or the migration section silently fails to load | Add `.catch` handling with a visible message before v1.0.0, per requirement item (5) |
| "Run migration now" reloads the list once at a fixed 3 seconds regardless of actual run duration (`configPage.html:100-101`, confirmed) | Administrator sees stale "ready" users after a long-running migration and may re-run unnecessarily | Poll until the task completes, or show a spinner/"running" state instead of a fixed-delay single reload |

## "Looks Done But Isn't" Checklist

- [ ] **Release workflow**: Passes today because it runs `mise run test`, but never verifies the tagged commit passed `mise run lint` or `mise run e2e` on `main` — verify with a deliberately-bad tag pushed from an untested branch in a scratch fork before trusting it for v1.0.0.
- [ ] **`zizmor` clean bill of health**: The default (non-pedantic) run already shows "No findings," which can read as "nothing to do" — verify the 7 *suppressed* findings and the 7 *pedantic* findings both have current, specific, per-finding justifications, not blanket historical suppressions.
- [ ] **Manifest "lists every version"**: A manifest that technically has multiple `versions[]` entries can still be wrong if the release workflow ever overwrites the array instead of appending — verify with a test that builds two sequential releases and asserts both versions are present in the final manifest (not just that `package.sh` produces one version per invocation, which is what `tests/scripts/package.bats` currently checks).
- [ ] **Load test "done"**: A load test that runs once and reports numbers without a resource-limit disclosure or an account-pool strategy has not actually measured what requirement items (13)-(15) ask for — verify the methodology, not just that numbers exist.
- [ ] **EF Core unit tests for `JellyfinDbContext`-dependent types**: A test suite that reports green after adding tests for `DefaultLoginMethod`/`MoveEmbyUsersToDefaultTask` may have used the InMemory provider and skipped the actual `ExecuteUpdateAsync` code path entirely (via a mock or a code path that never calls it) — verify the test actually exercises a real SQL `UPDATE` against SQLite, not a stub.

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|----------------|------------------|
| A secret is found in git history *after* the repository is already public | HIGH | Rotate/revoke the secret immediately (this is the only step that fully neutralizes it); then decide whether a history rewrite + fork/PR coordination is still worth the disruption, per GitHub's own guidance that rewriting may not be warranted once revoked |
| A `v*` tag was pushed and released from an untested commit | MEDIUM | Delete the GitHub Release and the tag (`gh release delete`, `git push --delete origin <tag>`), fix the release workflow's gate, re-tag from a verified commit. Note: the zip/manifest may already have been fetched by an administrator — treat this as a "yank," not a silent fix |
| A manifest checksum mismatch ships in a release | LOW-MEDIUM | Rebuild and re-upload the corrected `manifest.json` as a release asset (the zip itself is unaffected if only the checksum was wrong); administrators who already installed are unaffected, only future installs/updates are blocked until fixed |
| A load test's numbers turn out to be Docker-throttling artifacts, not real plugin latency | LOW | Re-run with corrected resource configuration; the load-test code itself doesn't need to change, only its documented environment and the conclusions drawn from it |

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|-------------------|---------------|
| History-rewrite-as-fix illusion (1) | Public release phase, first step | Full-history scan tool exits clean; Actions logs/artifacts/issues manually reviewed |
| Tag push skips lint/e2e (2) | Release and tooling phase | A deliberately-untested tag fails to produce a release |
| `GITHUB_TOKEN` tag-push suppression (3) | Not applicable now; flag if automation is added later | N/A until automated tagging exists |
| Pedantic zizmor suppressions without reasons (4) | Release and tooling phase | Every suppression comment states a specific, current reason |
| Manifest checksum/case mismatch (5) | Release and tooling / public-install phase | `package.bats` checksum assertion stays green across a hash-tool version bump |
| `targetAbi` minimum-not-maximum (6) | Public-install phase (docs only) | README/docs state the tested-version boundary explicitly |
| raw.githubusercontent.com / Pages caching (7) | Public-install phase | Manifest hosting decision made explicitly; propagation window documented or verified in workflow |
| `EmbyVerifiedPasswords` semantics drift (8) | Bug-fix phase (items 1, 4) | New test asserts `Matches` behavior across a simulated restart after a forced write/read failure |
| Blank-password-window fix reintroduces concurrency bug (9) | Bug-fix phase (item 3) | Concurrency test on account creation; narrow-write pattern used, not full `UpdateUserAsync` |
| `ExecuteUpdateAsync` untestable on InMemory provider (10) | Test-gap phase (item 10) | New `JellyfinDbContext` tests use SQLite in-memory, not InMemory provider |
| Lockout counter contaminates load test (11) | Performance/load-test phase (items 13-15) | Load test uses a distinct-account pool; lockout scenario tested separately |
| Docker resource limits skew load test (12) | Performance/load-test phase (items 13-15) | Resource configuration documented alongside every reported number; throttle rate checked via `cpu.stat` |

## Sources

- [GitHub Docs — Setting repository visibility](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/managing-repository-settings/setting-repository-visibility) — official, confirmed
- [GitHub Docs — Removing sensitive data from a repository](https://github.com/github/docs/blob/main/content/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository.md) — official, confirmed
- [trailofbits/skills — open-sourcing SKILL.md](https://github.com/trailofbits/skills/blob/main/plugins/open-sourcing/skills/open-sourcing/SKILL.md) — practitioner checklist, reported
- [voitta-ai/skillz — pre-open-source-credential-audit](https://github.com/voitta-ai/skillz/blob/master/skills/pre-open-source-credential-audit/SKILL.md) — practitioner checklist, reported
- [zizmor — Audit Rules](https://docs.zizmor.sh/audits/) — official, confirmed
- [zizmorcore/zizmor GitHub repo](https://github.com/zizmorcore/zizmor) — official, confirmed
- [zizmorcore/zizmor#1940 — cache-poisoning false positive discussion](https://github.com/zizmorcore/zizmor/issues/1940) — official, confirmed
- [github/spec-kit#1736 — GITHUB_TOKEN tag push suppression fix](https://github.com/github/spec-kit/pull/1736) — real-world fix, confirmed
- [googleapis/release-please#1142 — tag push doesn't trigger workflows](https://github.com/googleapis/release-please/issues/1142) — GitHub-acknowledged, confirmed
- [jellyfin/jellyfin#11331 — targetAbi minimum-not-maximum](https://github.com/jellyfin/jellyfin/issues/11331) — official Jellyfin repo, confirmed, open
- [jellyfin/jellyfin#4688 — plugin catalog lists invalid ABI versions](https://github.com/jellyfin/jellyfin/issues/4688) — official Jellyfin repo, confirmed
- [jellyfin/jellyfin#16353 — AuthenticateByName DbUpdateConcurrencyException race](https://github.com/jellyfin/jellyfin/issues/16353) — official Jellyfin repo, confirmed, open with in-progress fix
- [jellyfin/jellyfin#16934 — AuthenticateUser lock deadlock](https://github.com/jellyfin/jellyfin/issues/16934) — official Jellyfin repo, confirmed, fixed in 10.11.11
- [jellyfin/jellyfin — UserManager.cs source](https://github.com/jellyfin/jellyfin/blob/fb763c47/Jellyfin.Server.Implementations/Users/UserManager.cs) — official, confirmed
- [Stack Overflow — avoiding cached content from raw.githubusercontent.com](https://stackoverflow.com/questions/64792450/avoiding-getting-cached-content-from-raw-githubusercontent-com) — confirmed, reproduced testing cited
- [everything-presence-pro-grid#352 — Pages CDN TTL verification fix](https://github.com/clintongormley/everything-presence-pro-grid/pull/352) — real-world analogous fix, reported
- [dotnet/efcore#29376 — bulk update/delete not supported on InMemory](https://github.com/dotnet/efcore/issues/29376) — official EF Core repo, confirmed
- [dotnet/efcore#31320 — unclear error for ExecuteUpdate on InMemory](https://github.com/dotnet/efcore/issues/31320) — official EF Core repo, confirmed
- [dotnet/efcore#38010 — ExecuteUpdateAsync SQLite alias bug with navigation properties](https://github.com/dotnet/efcore/issues/38010) — official EF Core repo, confirmed, open
- [dotnet-guide.com — EF Core InMemory vs SQLite testing pitfalls](https://www.dotnet-guide.com/articles/ef-core-inmemory-provider-pitfalls/) — reported, corroborating detail
- [QASkills.sh — Account Lockout Testing](https://qaskills.sh/blog/account-lockout-testing-brute-force-thresholds) — reported, general pattern
- [OWASP WSTG — Testing for Weak Lock Out Mechanism](https://github.com/OWASP/wstg/blob/master/document/4-Web_Application_Security_Testing/04-Authentication_Testing/03-Testing_for_Weak_Lock_Out_Mechanism.md) — official OWASP reference, confirmed
- [opscart/container-isolation-benchmarks](https://github.com/opscart/container-isolation-benchmarks) — reproducible independent benchmark, reported
- This repository: `.planning/PROJECT.md`, `.planning/codebase/CONCERNS.md`, `.planning/codebase/TESTING.md`, `.claude/rules/plugin.md`, `.github/workflows/ci.yml`, `.github/workflows/release.yml`, `scripts/package.sh`, `src/Jellyfin.Plugin.EmbyAuth/DefaultLoginMethod.cs`, `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs` — confirmed by direct read, commit `ecee1ed`/`f4e3178` era

---
*Pitfalls research for: Jellyfin authentication plugin public v1.0.0 release*
*Researched: 2026-09-17*
