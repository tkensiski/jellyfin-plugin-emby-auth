# Phase 4: Emby Traffic Under Load and Failure - Research

**Researched:** 2026-09-20
**Domain:** Jellyfin 12.1 authentication plugin (C#/.NET 10) — Emby session hygiene, concurrency correctness, settings-failure e2e coverage, and a k6/toxiproxy load-test harness with four measured bottlenecks
**Confidence:** HIGH for in-repo code claims (all files read directly this session) and for the two load-test tool pins (verified live against GitHub/GHCR this session); MEDIUM for the numeric pass/fail thresholds, which are a planning decision this research informs but does not set; LOW/ASSUMED for the Jellyfin-core behavior claims that this session did not itself re-verify against Jellyfin source (they were verified with file:line quotes during the `/gsd-discuss-phase` session and are recorded verbatim in `04-CONTEXT.md`)

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

- **D-01:** `SignOutAsync` moves ahead of the user-name check in `EmbyClient.AuthenticateAsync`. Today the method returns at `EmbyClient.cs:88-92` when the response carries no user name, so a response that carries an `AccessToken` and no name never reaches the sign-out at `:94`. After the change every readable token ends its session, and the login is still refused. The refusal itself does not change: `AuthenticateAsync` already returns `null` for that response, and `EmbyAuthenticationProvider.cs:96-97` already turns `null` into `AuthenticationException`. AUTH-05 is a session-hygiene requirement, not an access-control one.
- **D-02:** A success response whose body cannot be parsed is documented, and no code tries to recover the token from it. The catch at `EmbyClient.cs:81-85` runs before anything is deserialized, so the token is inside the body that failed to parse. The maintainer's rule: "it's up to Emby to return a good payload." `docs/how-it-works.md` states the limit — one Emby session can stay open for such a response, and the plugin cannot end it — instead of claiming the plugin ends every session Emby opens.
- **D-03:** The losing login stays refused, and no production behavior changes. Exactly one account, no HTTP 500: ROADMAP criterion 2 already holds against today's code, so TEST-05 is a regression test that locks the behavior in. Rejected: re-resolving the account by name and saving the password onto it, which contradicts Phase 1 D-02 ("no account lookup by name, no retry").
- **D-04:** The Error entry that the losing login produces is reworded. `LogCreateAccountFailed` (`EmbyAuthenticationProvider.cs:259-260`) tells the administrator to rename the user on Emby, which is the wrong remedy for a benign race. One catch and one Error entry stay; the message states both causes and the remedy for each. No detection logic and no second branch, because Jellyfin throws `ArgumentException` for a duplicate name and for an invalid name alike.
- **D-05:** TEST-05 covers both levels: e2e for the real Jellyfin invariants ("through Jellyfin"), unit for the deterministic duplicate-name path via `FakeUserManager.CreateUserAsync` throwing `ArgumentException`.
- **D-06:** The end-to-end test asserts invariants only: exactly one Jellyfin account for that name, every response is 200 or 401, and none is 500. It does **not** require that a refusal occurred.
- **D-07:** The end-to-end test covers all four invalid cases that `EmbyAuthSettings.FindProblem` produces (`EmbyAuthSettings.cs:52-69`): blank Emby server URL, non-absolute-http(s) URL, URL with user info, blank API key. Each case saves the settings on the running server, shows a login on the Emby login method is refused, and shows the matching problem sentence in the Jellyfin log at Error level.
- **D-08:** The user-info case embeds a credential matching the pattern `90-jellyfin-log.bats:16` already greps for, e.g. `http://user:leak-emby-pass@emby-proxy:8096`. The existing whole-log check then proves the URL never reaches the log.
- **D-09:** Two new e2e files, `60-concurrent-logins.bats` and `70-invalid-settings.bats`, each owning its own Jellyfin accounts in `setup_file` and its own `reset_plugin_config`.
- **D-10:** Headline numbers are measured at the scale this plugin actually serves — a home server, tens of Emby users, a burst of about ten concurrent first logins — plus one larger stress run to locate the cliff. The realistic run decides the verdicts; the stress run is one extra scenario.
- **D-11:** Each bottleneck gets a written pass-or-fail threshold in the plan **before** anything is measured. A measurement that misses its threshold is fixed in this phase; one that meets it is accepted in the docs with its number. No fix is pre-committed.
- **D-12:** A new `docs/performance.md` carries the method, the environment each number was measured in, the threshold, the measured figure, and the verdict per item. `docs/how-it-works.md` stays a behavior document.
- **D-13:** The per-login write cost (Phase 1 D-08 deferral) is measured as a fourth item, with its own threshold and verdict. No fix authorized in advance. If a fix is taken, note the trade: skipping writes means verifying the saved hash first, and PBKDF2 verify is deliberately expensive. **Reversibility:** costly if a fix is taken — it changes the login path AUTH-01 governs.

### Claude's Discretion

- The load-test harness: what drives the load, and where it runs.
- How Emby is made slow or faulty: a fault-injection proxy beside `emby-proxy`, or a delay built into `emby-proxy.conf`.
- The burst size for D-06's end-to-end test, and whether the Jellyfin-log reader D-07 needs becomes a shared helper in `e2e/helpers.bash` or stays inline.
- Which new Emby user names `e2e/setup_suite.bash` gains for the two new files.
- Whether `docs/performance.md` is linked from `README.md` and the `docs/` index.
- Exact wording throughout, within the rule that no message repeats the Emby URL or the API key.
- Test file names and the split across files.

### Deferred Ideas (OUT OF SCOPE)

- Make the losing concurrent login succeed by re-resolving the account by name and saving the password onto it.
- Branch the race-loser log on the duplicate-name case and drop it to Information.
- Buffer the Emby response body and extract the token when JSON parsing fails.
- Require a refusal in the concurrency e2e test.
- Skip the hash and fingerprint write when the password did not change — measured here (D-13), not pre-authorized.
- `HybridCache` for the Emby user list.
- Retiring the fingerprint file — carried from Phase 3, blocked upstream.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| AUTH-05 | Every readable Emby access token ends its Emby session, including a no-user-name response; a success response the plugin cannot parse hides its token, and `docs/how-it-works.md` states that limit | §Code Examples "Emby sign-out reorder"; §Common Pitfalls "Reordering an early return can flip a test's own assertion" |
| TEST-05 | A test covers concurrent first logins through Jellyfin | §Architecture Patterns "Concurrency test, two levels"; §Existing Test Coverage below — a unit test already asserts most of this path |
| TEST-06 | An e2e test covers invalid settings on a running server: refused logins, Error-level log line | §Code Examples "Invalid-settings e2e case table"; verified `EmbyAuthPlugin.UpdateConfiguration` never validates the URL/API key |
| PERF-01 | A load test runs from a mise task against the Compose stack with a separate account pool, measuring five things | §Standard Stack (k6 + toxiproxy); §Architecture Patterns "Load-test data flow" and "Measurement method per bottleneck" |
| PERF-02 | A written pass/fail threshold per item, before the run; each item fixed or accepted with its number and environment | §Common Pitfalls (account-pool exhaustion, Docker CFS throttling); §Open Questions (the four numeric thresholds are a planning, not a research, decision) |
</phase_requirements>

## Summary

This phase is four small, independent pieces of work sharing one new tool. AUTH-05 is a four-line reorder in `EmbyClient.AuthenticateAsync` with an existing test seam (`StubHttpMessageHandler`) that only needs a new response fixture. TEST-05's unit half is **already substantially covered**: `EmbyAuthenticationProviderTests.cs:196-207` (`RefusesTheLogin_WhenCreateUserFailsBecauseJellyfinRejectsTheName`) already makes `FakeUserManager.CreateUserAsync` throw `ArgumentException` and asserts the login is refused with no delete attempted — this is D-05's unit-level proof; the new work is renaming/annotating it as the race-loser case and adding an assertion for D-04's reworded log message. TEST-05's e2e half and TEST-06 are both new `bats` files following the existing `NN-topic.bats` convention exactly. `EmbyAuthPlugin.UpdateConfiguration` (verified `:76-88`) validates only the migration/password-set target, never the URL or API key, so TEST-06's four invalid-settings cases can all be saved through the live settings API with no code change required to make them reach the login path.

The load test (PERF-01/PERF-02) is the heaviest new surface. Two tools need pinning: **k6 2.2.0** (verified live via the GitHub API this session, current stable, `mise`-installable via `aqua:grafana/k6`) drives HTTP load against the Docker Compose stack's published ports, the same way `bats e2e` already does. **toxiproxy 2.12.0** (verified live via GitHub API and `docker pull`/`docker manifest inspect` this session — the image `ghcr.io/shopify/toxiproxy:2.12.0` exists, is multi-arch, and pulls cleanly) sits as a new hop between the existing logging `emby-proxy` and Emby, adding zero latency by default so it is transparent to every existing e2e test, and configured via its plain HTTP admin API (no `toxiproxy-cli` dependency needed — `curl` already does everything the load test needs, avoiding a third tool pin).

Of the four measured bottlenecks, two (Emby calls inside the login lock; duplicate user-list requests at cache expiry) are genuinely network/lock-timing phenomena and must be measured against the real Docker stack. The other two (fingerprint writes under the lock; the per-login Jellyfin account save) are better measured differently: the fingerprint-write cost can be isolated with a pure in-process microbenchmark against the real `EmbyVerifiedPasswords` class and a real temp file — no Docker needed at all, reusing the existing `ConcurrentRecords_AreAllKept` pattern (`EmbyVerifiedPasswordsTests.cs:110-120`) with timing added. The Jellyfin account-save cost cannot be isolated the same way, because `FakeUserManager.UpdateUserAsync` is a no-op; it must be measured through the real stack as (total observed login latency) minus (Emby round-trip time, obtained by adding `$request_time` to the existing nginx proxy's log format) minus (the fingerprint-write number from the microbenchmark). This subtraction is a derived metric, not a direct one — flagged as an open methodology question for the plan to resolve explicitly, since D-13 requires this item's own threshold and verdict.

Two research findings from `04-CONTEXT.md` and `.planning/codebase/CONCERNS.md` govern account-pool sizing for the load test, beyond the generic "avoid the 3-attempt lockout" pool-sizing already known from `.planning/research/PITFALLS.md:216-232`: (1) the "Emby calls inside the login lock" scenario needs a **fresh, never-before-used Emby username per iteration** — once a name has completed one first login it becomes a known user, whose logins take a per-user lock and no longer exercise the shared `Guid.Empty` contention this scenario measures, so the account pool must have at least as many unused names as the total request count for that scenario, not just enough to dodge lockout; (2) the "cache-expiry stampede" scenario needs the opposite — a batch of **already-existing** accounts on the Emby login method, since only known-user logins run in parallel (unknown-name logins serialize and cannot produce concurrent cache-miss callers).

**Primary recommendation:** Reuse the two-level test pattern already in this repo (deterministic unit test + real-server e2e test) for TEST-05/TEST-06 with no new test infrastructure; add k6 (mise-pinned, host CLI) and toxiproxy (a new, always-present, zero-latency-by-default Compose service) for the load test; measure bottlenecks 1 and 2 through the real stack, and bottlenecks 3 and 4 through a combination of an in-process microbenchmark and a derived subtraction from stack-measured login latency.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Emby session sign-out (AUTH-05) | API / Backend (`EmbyClient`) | — | `EmbyClient` is already the sole component that talks to Emby; no tier change |
| Concurrent-login correctness (TEST-05) | API / Backend (Jellyfin core `UserManager` lock) | Database / Storage (the `Users` row insert) | The lock and the race are entirely inside Jellyfin core; the plugin only reacts to the exception Jellyfin's `CreateUserAsync` throws |
| Invalid-settings refusal (TEST-06) | API / Backend (`EmbyAuthSettings`, `EmbyAuthenticationProvider.GetSettings`) | CDN/Static (the settings page, unaffected — it has no client-side validation to add) | Validation already exists at login time; TEST-06 adds coverage, not a new validation tier |
| Load generation (PERF-01) | New: Test/CI tooling tier (k6, host CLI) | — | Not a production tier; k6 talks to Jellyfin's existing HTTP API from outside the container network, exactly like `bats e2e` |
| Fault injection (PERF-01) | New: Test/CI tooling tier (toxiproxy, Compose service) | — | Sits in the existing Docker network between `emby-proxy` and `emby`; no production code touches it |
| Bottleneck 1 & 2 measurement (PERF-01/02) | Database / Storage is not involved — API/Backend request handling under Jellyfin's lock | — | Both are Jellyfin-core lock/timing phenomena, not plugin-code hot paths |
| Bottleneck 3 measurement (fingerprint writes) | Database / Storage (local file, `EmbyVerifiedPasswords`) | — | Pure file I/O under a `Lock`; no network tier involved at all |
| Bottleneck 4 measurement (account save) | Database / Storage (Jellyfin's own `JellyfinDbContext`/SQLite) | API / Backend (the `UpdateUserAsync` call site) | The cost lives inside Jellyfin core's EF Core save path, which the plugin only calls, never implements |

## Standard Stack

### Core

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| k6 | 2.2.0 | Generates load-test traffic against Jellyfin's HTTP login endpoint | `[VERIFIED: GitHub API, this session — GET /repos/grafana/k6/releases/latest → tag_name "v2.2.0", published_at 2026-08-10T14:01:35Z]`. `mise registry k6` confirms the `aqua:grafana/k6` backend `[VERIFIED: local mise registry query, this session]`. Single static binary, scriptable, reports percentiles natively — no hand-rolled `curl`-loop percentile math needed. |
| toxiproxy | 2.12.0 | Injects controllable latency between Emby and the plugin, to simulate "Emby is slow" deterministically | `[VERIFIED: GitHub API, this session — GET /repos/Shopify/toxiproxy/releases/tags/v2.12.0 exists, release assets include `toxiproxy-server`/`toxiproxy-cli` binaries for all platforms]`. Image `ghcr.io/shopify/toxiproxy:2.12.0` `[VERIFIED: docker manifest inspect + docker pull, this session — multi-arch manifest list includes linux/amd64 and linux/arm64; pull succeeded, digest `sha256:9378ed52a28bc50edc1350f936f518f31fa95f0d15917d6eb40b8e376d1a214e`]`. No newer release exists (latest tag is still v2.12.0, published 2025-03-18) — low-churn, not abandoned (32 prior releases). |

**No new C# NuGet package is needed.** TEST-05's unit test reuses `FakeUserManager` (already supports `CreateUserThrows`) and `CapturingLogger<T>` (already exists). TEST-06 needs no new C# test at all — `EmbyAuthSettingsTests.cs` and `EmbyAuthenticationProviderTests.cs:118-136` (`RefusesTheLogin_WithTheSettingsProblem_WhenTheSettingsAreInvalid`) already cover settings validation at the unit level; TEST-06 is purely an e2e addition.

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `curl` (already a repo dependency, used throughout `e2e/helpers.bash`) | n/a | Drives toxiproxy's plain HTTP admin API (`POST /proxies/{name}/toxics`) to add/remove a latency toxic before/after each load-test scenario | Preferred over adding a fourth pinned tool (`toxiproxy-cli`); the admin API is documented JSON-over-HTTP and the repo already has the `api`/`status` helper pattern in `e2e/helpers.bash` to model this on |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| k6 | `bats` + backgrounded `curl` loops | No new tool, but no percentiles, no ramping VUs, and hand-rolled timing math for four separate bottlenecks — rejected per `.planning/research/STACK.md`'s own analysis, still valid |
| toxiproxy (proxy hop) | A delay built into `emby-proxy.conf` via a custom nginx module or a small hand-rolled proxy | No new dependency, but stock `nginx:1.30.5-alpine` `[VERIFIED: e2e/compose.yaml:11]` has no delay directive, so this means writing and maintaining custom proxy code for exactly the job a purpose-built, actively-maintained tool already does — rejected as more maintenance for the same result |
| `curl` for toxiproxy's admin API | `toxiproxy-cli` (the official CLI, also mise-installable via the same `aqua:Shopify/toxiproxy` release) | `toxiproxy-cli` is more ergonomic for interactive use, but adds a fourth pinned tool for functionality `curl -X POST` already provides in one line; use `toxiproxy-cli` only if the load-test scripts grow complex enough that raw JSON construction becomes error-prone |

**Installation:**
```bash
# .mise.toml — add alongside the existing pins
[tools]
k6 = "2.2.0"

# e2e/compose.yaml — new service, always present, zero latency by default
#   image: ghcr.io/shopify/toxiproxy:2.12.0
```

**Version verification (re-run at plan/implementation time — both tools were last confirmed 2026-09-20):**
```bash
curl -s https://api.github.com/repos/grafana/k6/releases/latest | grep tag_name
curl -s https://api.github.com/repos/Shopify/toxiproxy/releases/latest | grep tag_name
```

## Package Legitimacy Audit

The `gsd-tools query package-legitimacy check` seam covers npm/PyPI/crates ecosystems. k6 and toxiproxy are Go binaries distributed as GitHub releases and OCI images, not registry packages, so that automated gate does not apply here. A manual audit was performed instead, using the same signals (age, source repository, release cadence, prior adoption):

| Package | Registry/Source | Age | Adoption Signal | Source Repo | Verdict | Disposition |
|---------|------------------|-----|------------------|--------------|---------|-------------|
| k6 (`grafana/k6`) | GitHub Releases + `aqua:grafana/k6` (mise) | Project since 2017; latest tag 2026-08-10 | Grafana Labs-maintained; `.planning/research/STACK.md` already vetted it HIGH confidence | `github.com/grafana/k6` | OK | Approved |
| toxiproxy (`Shopify/toxiproxy`) | GitHub Releases + `ghcr.io/shopify/toxiproxy` | Project since 2014; latest tag 2025-03-18 (32nd release) | Shopify-maintained, widely used for chaos-testing in the Go/Ruby ecosystems; image pull verified this session | `github.com/Shopify/toxiproxy` | OK | Approved |

**Packages removed due to `[SLOP]` verdict:** none.
**Packages flagged as suspicious `[SUS]`:** none. Both tools are maintained by well-known organizations (Grafana Labs, Shopify) with long release histories; neither was discovered via an unverified web search — both were cross-checked live against GitHub's API and the GHCR registry this session.

## Architecture Patterns

### System Architecture Diagram (load-test data flow)

```
┌─────────────┐   HTTP (host port)   ┌───────────────────────────┐
│  k6 (host)  │ ───────────────────► │  Jellyfin (container)     │
│  or bats    │                      │  EmbyAuthenticationProvider│
└─────────────┘                      └───────────┬───────────────┘
                                                  │ HTTP (Docker network)
                                                  ▼
                                      ┌───────────────────────────┐
                                      │  emby-proxy (nginx)       │  ← unchanged; still logs
                                      │  logs request+body        │    every request+body for
                                      └───────────┬───────────────┘    emby_login_requests
                                                  │ HTTP
                                                  ▼
                                      ┌───────────────────────────┐
                                      │  toxiproxy (NEW)          │  ← 0 toxics by default:
                                      │  admin API :8474          │    transparent passthrough.
                                      └───────────┬───────────────┘    Load test's "slow Emby"
                                                  │ HTTP                scenario POSTs a latency
                                                  ▼                     toxic here before the run
                                      ┌───────────────────────────┐    and removes it after.
                                      │  emby (container)         │
                                      └───────────────────────────┘
```

For the two bottlenecks that don't need this network path at all (fingerprint writes; conceptually, the account-save subtraction math), see "Measurement method per bottleneck" below.

### Recommended Project Structure

```
e2e/
├── 60-concurrent-logins.bats     # NEW — TEST-05 e2e half (D-09)
├── 70-invalid-settings.bats      # NEW — TEST-06 (D-09)
├── compose.yaml                  # MODIFIED — add the toxiproxy service (always present, 0 toxics)
├── toxiproxy.json                # NEW — initial proxy definition (name "emby", listen :8096, upstream emby:8096)
├── emby-proxy.conf                # MODIFIED (load test only, see below) — nginx proxy_pass target becomes
│                                   #   toxiproxy:8096 instead of emby:8096 so Jellyfin's traffic always
│                                   #   flows through the fault-injection hop; add $request_time to log_format
│                                   #   for bottleneck-4's derived measurement
└── ...(existing files unchanged)

load-test/                        # NEW top-level dir (or tests/load/ — planner's naming choice)
├── scenarios/
│   ├── slow-emby.js              # k6 script: first logins through toxiproxy with a latency toxic
│   ├── concurrent-first-logins.js
│   ├── cache-expiry.js           # known-user logins paced across the 60s CacheDuration boundary
│   └── account-save.js           # known-user logins, cache warm, Emby fast — for bottleneck 4's subtraction
├── account-pool.sh                # provisions the separate Emby test-account pool before any k6 run
└── report.md or .json output      # feeds docs/performance.md numbers

docs/
└── performance.md                # NEW (D-12) — method, environment, threshold, measured figure, verdict
```

### Pattern 1: Two-level concurrency test (TEST-05, per D-05)

**What:** A deterministic unit test proves the exception-handling path; a real e2e test proves Jellyfin's actual lock produces the invariant.
**When to use:** Whenever "prove a race is handled" needs both a fast, deterministic regression guard and a real-server invariant check, and the real concurrency mechanism (here, Jellyfin's own lock) is outside the plugin's own code.
**Example — the unit half already exists** (`tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyAuthenticationProviderTests.cs:196-207`, read this session):
```csharp
[Fact]
public async Task RefusesTheLogin_WhenCreateUserFailsBecauseJellyfinRejectsTheName()
{
    var userManager = new FakeUserManager { CreateUserThrows = new ArgumentException("bad name") };
    var handler = new StubHttpMessageHandler().Then(AliceUserList).Then(AliceAuthenticateResponse);
    var provider = CreateProvider(handler, userManager, out _);

    await Assert.ThrowsAsync<AuthenticationException>(() => provider.Authenticate("alice", "alice-pass", null));

    Assert.Equal(["CreateUserAsync"], userManager.Calls);
    Assert.DoesNotContain("DeleteUserAsync", userManager.Calls);
}
```
This is **already** D-05's deterministic proof — the same `ArgumentException` Jellyfin's `CreateUserAsync` throws for both a duplicate name (the race) and an invalid name (a different cause) per D-04's own analysis. The remaining unit-level work for this phase is (a) deciding whether to rename this test to signal it is also the race-loser proof, or add a second, identically-shaped test with a race-framed name and a doc comment cross-referencing D-03/D-04, and (b) extending it (or a sibling test using `CapturingLogger<EmbyAuthenticationProvider>`, following the pattern at `EmbyAuthenticationProviderTests.cs:246-267`) to assert the exact reworded D-04 message text.

**Example — the e2e half is new**, following the exact `NN-topic.bats` shape already used by `40-emby-outage.bats` (fd-3-safe backgrounding per the bats note in `04-CONTEXT.md`, corroborated by `[CITED: bats-core docs — file descriptor 3, and bats-core#419]`):
```bash
#!/usr/bin/env bats
# Concurrent first logins for one new Emby user. setup_suite.bash starts the servers.

setup_file() {
	load helpers
	reset_plugin_config
}

setup() {
	load helpers
}

@test "concurrent first logins for a new Emby user create exactly one account, with no HTTP 500" {
	local burst=10 i pids=() results
	for ((i = 0; i < burst; i++)); do
		( login_status "$JELLYFIN" burst1 burst1-emby-pass 3>&- ) >"$BATS_TEST_TMPDIR/result-$i" &
		pids+=($!)
	done
	for pid in "${pids[@]}"; do
		wait "$pid"
	done

	results="$(cat "$BATS_TEST_TMPDIR"/result-*)"
	# Every response is 200 or 401, never 500 (D-06).
	[[ "$results" =~ ^[24010]+$ ]] || { echo "Unexpected status codes: $results" >&2; return 1; }
	if grep -q '5' <<<"$results"; then
		echo "A login returned HTTP 500: $results" >&2
		return 1
	fi

	# Exactly one Jellyfin account exists for burst1 (D-06).
	local count
	count="$(api GET "$JELLYFIN/Users" "$JF_TOKEN" | jq '[.[] | select(.Name == "burst1")] | length')"
	[ "$count" = "1" ]
}
```
`[ASSUMED]` — this exact script is illustrative, not final; the planner/executor decides the precise regex/parsing approach, the exact burst size (D-10 suggests ~10, matching the load test's realistic scale), and whether `burst1-emby-pass` needs to exist on Emby before the burst (yes — per `.claude/rules/e2e.md:18`, create it in `setup_suite.bash`, one new name for this file, per the discretion item on naming).

**The fd-3 hang risk is real and specific to `run`, not to backgrounding in general.** `bats`'s own `run` helper captures output through file descriptor 3; a backgrounded subshell that inherits fd 3 keeps it open past the parent test's own completion, and bats hangs waiting for fd 3 to close. The sketch above avoids `run` entirely for the backgrounded calls (redirecting to a file instead) and closes fd 3 explicitly (`3>&-`) inside each subshell — both defenses, per the two external sources cross-referenced in `04-CONTEXT.md`.

### Pattern 2: TEST-06's four cases against a real settings API (D-07)

**What:** Save each of `EmbyAuthSettings.FindProblem`'s four invalid cases through the live `POST /Plugins/{id}/Configuration` endpoint, then assert refusal + Error log line.
**Verified this session** — `EmbyAuthPlugin.UpdateConfiguration` (`src/Jellyfin.Plugin.EmbyAuth/EmbyAuthPlugin.cs:76-88`):
```csharp
public override void UpdateConfiguration(BasePluginConfiguration configuration)
{
    var userManager = _serviceProvider.GetRequiredService<IUserManager>();
    var problem = MigrationTargetValidation.FindProblem(configuration as PluginConfiguration, userManager.GetAuthenticationProviders());
    if (problem is not null)
    {
        var logger = _serviceProvider.GetRequiredService<ILogger<EmbyAuthPlugin>>();
        LogConfigurationRefused(logger, problem);
        throw new ArgumentException(problem, nameof(configuration));
    }

    base.UpdateConfiguration(configuration);
}
```
This method validates **only** the migration/password-set target (`MigrationTargetValidation.FindProblem`) — it never calls `EmbyAuthSettings.TryCreate`/`FindProblem`. So all four of D-07's cases (blank URL, non-http(s) URL, URL with user info, blank API key) save successfully through this API with no code change, and the refusal + Error log only appear later, at the next login attempt, inside `EmbyAuthenticationProvider.GetSettings` (`:164-173`, verified this session):
```csharp
private EmbyAuthSettings GetSettings()
{
    if (EmbyAuthSettings.TryCreate(configurationSource(), out var settings, out var problem))
    {
        return settings;
    }

    LogSettingsInvalid(logger, problem);
    throw new AuthenticationException(problem);
}
```
This confirms `.planning/codebase/CONCERNS.md`'s existing note: "Invalid settings show up only at login" — exactly the behavior D-07's e2e test exercises, needing zero production-code change.

**The four cases, table form:**

| Case | `EmbyAuthSettings.FindProblem` condition (`EmbyAuthSettings.cs:44-93`, read this session) | Expected Error log sentence (`EmbyAuthenticationProvider.cs:268-269`) |
|------|------|------|
| Blank Emby server URL | `string.IsNullOrWhiteSpace(configuration.EmbyServerUrl)` | "...refuses all logins... The Emby server URL is not set. Set it in Dashboard > Plugins > Emby Auth." |
| Non-absolute-http(s) URL | `Uri.TryCreate` fails or scheme is neither http nor https | "...The Emby server URL is not a valid http or https URL...." |
| URL with user info | `url.UserInfo.Length > 0` | "...The Emby server URL must not contain a user name or password...." |
| Blank Emby API key | `string.IsNullOrWhiteSpace(configuration.EmbyApiKey)` | "...The Emby API key is not set...." |

The user-info case (D-08) should use a URL matching `90-jellyfin-log.bats:16`'s pattern (`[a-z]+-(emby-pass|jf-pass|jf-random|admin-pass)(-[0-9]+)?`), e.g. `http://user:leak-emby-pass@emby-proxy:8096` — this makes the existing whole-log scan (which runs last, `90-jellyfin-log.bats`) prove the plugin never logs the configured URL, at no extra assertion cost, per D-08.

### Pattern 3: Load-test data flow and account-pool sizing rules

**What:** k6 (host CLI) drives HTTP traffic against Jellyfin's published port; toxiproxy injects latency for the "slow Emby" scenario only; a dedicated, never-reused-across-scenario account pool avoids both Jellyfin's lockout counter and a subtler contamination specific to this plugin's lock behavior.
**Two pool-sizing rules, not one** (this is the key non-obvious finding beyond the generic lockout-avoidance rule already documented in `.planning/research/PITFALLS.md:216-232`):

1. **"Emby calls inside the login lock" and "concurrent first logins" scenarios need names Emby has never seen log in before**, one name per request in the scenario, never reused. Rationale, cross-verified against `04-CONTEXT.md`'s canonical Jellyfin-source quotes (`[CITED: 04-CONTEXT.md canonical_refs, quoting Jellyfin v12.1 UserManager.cs:573,579-581]`, not independently re-read from Jellyfin source this session): Jellyfin resolves the user **before** taking its lock, and locks on `user?.Id ?? Guid.Empty`. An unknown name always locks on the shared `Guid.Empty` key — this is the condition the "Emby calls inside the login lock" bottleneck needs. The instant a name completes one successful first login, it becomes a known user with its own row and its own lock key, and every subsequent login for that name runs in parallel with everything else, no longer exercising this bottleneck. **Sizing:** the pool for this scenario must contain at least as many distinct, never-before-used Emby usernames as the total number of requests planned for the scenario (not just "enough to avoid 3-attempt lockout" — a single successful login retires a name from this scenario permanently).
2. **The "cache-expiry duplicate requests" scenario needs the opposite: a batch of already-existing accounts** on the Emby login method, created and logged in once before the timed portion of the scenario starts. Rationale (same citation basis): only known-user logins can run concurrently with each other (each takes its own lock key); unknown-name logins serialize and therefore cannot produce the concurrent cache-miss callers this scenario needs to observe a duplicate-request stampede.

**Measurement method per bottleneck (this is the one design decision this research most strongly urges the plan to make explicit, because D-13 requires bottleneck 4 to have its own threshold and verdict, and it cannot be measured the same way as bottleneck 3):**

| Bottleneck | Needs the Docker stack? | Measurement approach |
|---|---|---|
| 1. Emby calls inside the login lock | Yes | k6 drives a burst of first logins (pool rule 1) through toxiproxy with a latency toxic active; record total burst completion wall-time and per-login p50/p95, both with and without the toxic |
| 2. Duplicate user-list requests at cache expiry | Yes | k6 (or bats) drives known-user logins (pool rule 2) paced across the 60-second `EmbyUserDirectory.CacheDuration` boundary (verified constant, `EmbyUserDirectory.cs:39`); count `GET /Users` lines in the `emby-proxy` nginx access log (the existing `log_format requests` already logs `$request_method $uri`, so `grep -c '^GET /Users '` against the proxy's log directly counts these, the same technique `emby_login_requests` already uses for `POST /Users/AuthenticateByName`) and compare against the theoretical minimum (`ceil(scenario_duration_seconds / 60)`) |
| 3. Fingerprint writes under the lock | **No** | A pure in-process microbenchmark: construct a real `EmbyVerifiedPasswords` against a real temp file (exactly as `EmbyVerifiedPasswordsTests.cs` already does), fire N concurrent `Record()` calls with `Task.Run`/`Task.WhenAll` (the existing `ConcurrentRecords_AreAllKept` pattern, `EmbyVerifiedPasswordsTests.cs:110-120`), and add a `Stopwatch` around each call to report p50/p95 latency. No Emby, no Jellyfin container, no k6 needed for this one item |
| 4. Per-login Jellyfin account save | Yes, and only **derived**, not direct | `FakeUserManager.UpdateUserAsync` is a no-op (`TestDoubles.cs:162-172`, verified this session), so this cost can only be observed through a real Jellyfin container. Add `$request_time` to `emby-proxy.conf`'s `log_format` (nginx directive, one line, load-test-only concern — this measures nginx's own request-processing time toward Emby, giving the Emby-side latency per call). Then: `account_save_estimate ≈ (k6-observed total login latency for a known-user, cache-warm, Emby-fast repeat login) − (sum of matched $request_time entries for that login's Emby calls) − (bottleneck-3's microbenchmark number, as the fingerprint-write component of the same post-Emby-response code path)`. This is a **derived, approximate** number, not a directly measured one — flag explicitly in `docs/performance.md` per D-12's "method" column, and treat it as `[ASSUMED]` methodology pending planner sign-off |

### Anti-Patterns to Avoid

- **Reusing load-test accounts across scenarios or iterations:** trips Jellyfin's 3-attempt lockout (`.planning/research/PITFALLS.md:216-232`, and independently confirms this repo's own e2e convention of one-name-set-per-file, `.claude/rules/e2e.md:19`) — provision every account up front, in `setup_suite`-style, before the 60-second Emby user-list cache window starts.
- **Running the load test with a Docker `--cpus` quota "for realism" without checking `cpu.stat`:** a container capped modestly below its burst need spends a large fraction of wall time throttled, non-linearly distorting exactly the numbers this phase exists to produce (`.planning/research/PITFALLS.md:236-252`, reproduced-benchmark citation). `e2e/compose.yaml` currently sets **no** CPU/memory limits `[VERIFIED: e2e/compose.yaml, read this session — no `deploy.resources` or `cpus`/`mem_limit` keys on any service]`; keep it that way for the load test, or apply generous limits and document them, never a tight one "to be realistic."
- **Trying to measure bottleneck 4 with the same fake `IUserManager` used everywhere else in the unit suite:** it is a no-op double by design (`TestDoubles.cs:162-172`), so it cannot report a real SQLite/EF Core save cost. Only a real Jellyfin container can.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Injecting latency into an HTTP hop for a "slow Emby" scenario | A custom nginx Lua block or a hand-written proxy in `emby-proxy.conf` | toxiproxy (`ghcr.io/shopify/toxiproxy:2.12.0`) | Stock `nginx:1.30.5-alpine` has no delay directive; toxiproxy is purpose-built, actively maintained, and controlled by a documented HTTP API `curl` already handles |
| Load-test percentile math | Bash arrays + `sort`/`awk` over `curl -w '%{time_total}'` output | k6's built-in metrics and thresholds | `.planning/research/STACK.md`'s own analysis already rejected the hand-rolled version for exactly this reason; still valid |
| Proving Jellyfin serializes unknown-name logins on one lock key | A custom mutex or semaphore inside the plugin, "just to be sure" | Nothing — this is Jellyfin core's own behavior; the plugin's only job is to catch the `ArgumentException` the loser gets, which it already does | Building a plugin-side lock would duplicate a lock Jellyfin already holds, in a different process boundary than the plugin controls, achieving nothing |

**Key insight:** every piece of new machinery this phase needs (fault injection, load generation, percentile reporting) already exists as a small, well-audited, purpose-built tool; the only genuinely custom code this phase writes is the two new `.bats` files, the k6 scenario scripts, and one in-process C# microbenchmark for the fingerprint-write number.

## Common Pitfalls

### Pitfall 1: The account-creation window's C# unit test coverage is already mostly done — re-verify before writing new tests

**What goes wrong:** A plan that assumes TEST-05's unit half needs to be built from scratch duplicates `EmbyAuthenticationProviderTests.cs:196-207`, which already exists and already asserts the exact `ArgumentException`-from-`CreateUserAsync` path with no delete attempted.
**Why it happens:** `.planning/codebase/CONCERNS.md`'s "No test coverage" note for this path predates Phase 1's execution; the concerns doc is a snapshot from 2026-09-16, and the test was added afterward.
**How to avoid:** Read `EmbyAuthenticationProviderTests.cs` in full before scoping TEST-05's unit-test task; the remaining work is a rename/annotation plus a new assertion for D-04's reworded message, not a new test from zero.
**Warning signs:** A plan task titled "write the first unit test for the race-loser path" without a citation to the existing test.

### Pitfall 2: A load-test account pool sized only against the lockout limit measures the wrong thing for bottleneck 1

**What goes wrong:** `.planning/research/PITFALLS.md:216-232` already documents the generic lockout-avoidance rule (pool size > iterations, distinct accounts). Applying only that rule to the "Emby calls inside the login lock" scenario under-provisions the pool: a name that logs in successfully once is no longer a first login for any later request, so reusing a "used" name from this scenario's own pool silently switches that request onto the parallel, per-user-lock code path instead of the serialized `Guid.Empty` path — the scenario keeps running without erroring, but a growing fraction of its requests stop exercising the bottleneck being measured.
**Why it happens:** The generic account-pool rule ("use a big pool, don't reuse across scenarios") reads as sufficient; the finer point — that success, not just failure-count, retires a name from *this specific* scenario — is not in any of the existing research files and required tracing Jellyfin's lock-key selection logic (recorded with quotes in `04-CONTEXT.md`, `[CITED: 04-CONTEXT.md canonical_refs]`).
**How to avoid:** Size this scenario's pool to the total planned request count for the scenario, one name per request, never reused even within the scenario.
**Warning signs:** The measured "Emby calls inside the login lock" number improves mysteriously partway through a long run with no code change — a sign the pool ran out of fresh names and later iterations quietly became known-user, parallel-lock logins.

### Pitfall 3: `EmbyAuthPlugin.UpdateConfiguration` validating the migration target can mask a URL/API-key typo in a manually-constructed TEST-06 payload

**What goes wrong:** `UpdateConfiguration` (`EmbyAuthPlugin.cs:76-88`, verified this session) calls `MigrationTargetValidation.FindProblem` **before** anything URL/API-key related is even considered — if a TEST-06 test payload also happens to carry an invalid `MigrationTarget` (for example, by starting from a config the test never fully reset), the save fails for the *wrong* reason (a 400 from the plugin's own target check) before the URL/API-key case under test is ever exercised, and the test's assertion on the expected refusal sentence at *login* time never gets there because the settings never actually change.
**Why it happens:** `reset_plugin_config` (`e2e/helpers.bash:159-162`) sets a valid `MigrationTarget`; a TEST-06 test that builds its payload with `jq` starting from a *stale* fetched config (rather than freshly calling `reset_plugin_config` first) could carry forward an already-broken target from a prior test's mutation.
**How to avoid:** Each of D-07's four cases should call `reset_plugin_config` (or an equivalent full-valid-baseline reset) immediately before mutating only the one field under test, exactly as `set_plugin_config`'s jq-filter pattern already does elsewhere in this repo (`e2e/helpers.bash:164-170`).
**Warning signs:** A TEST-06 case asserts on the wrong problem sentence, or the settings save itself returns an unexpected HTTP status instead of succeeding.

### Pitfall 4: bats `run` on a backgrounded command hangs the whole file, not just one test

**What goes wrong:** `04-CONTEXT.md` already flags this from an external source: a backgrounded command that keeps bats's internal file descriptor 3 open (because it was wrapped in `run`, or inherited fd 3 without closing it) makes bats hang indefinitely after the test that started it, not just fail.
**Why it happens:** bats uses fd 3 internally to capture `run`'s output; a background job normally inherits all open file descriptors from its parent shell, including fd 3, and a shell that never exits (or a job bats doesn't `wait` for before the test function returns) keeps that descriptor open past the point bats expects it to close.
**How to avoid:** For D-06's burst, redirect background output to files instead of wrapping in `run`, and explicitly close fd 3 in the backgrounded subshell (`( ... ) 3>&- &`), then `wait` for every PID before the test function returns.
**Warning signs:** `mise run e2e` (or the new load-test task) hangs with no error, rather than failing — this is the tell that a background job leaked fd 3, not a logic bug in the assertions.

### Pitfall 5: Docker CFS throttling — already documented, restated here for D-12's environment-recording requirement

**What goes wrong / how to avoid:** Fully covered in `.planning/research/PITFALLS.md:236-252` (Pitfall 12) and directly cited in `04-CONTEXT.md` D-12's rationale for requiring the environment alongside every number. `e2e/compose.yaml` currently applies no CPU/memory limits `[VERIFIED, this session]` — this is actually the *safe* default per that pitfall's own recommendation (run unconstrained, or with generous limits, and document either choice), so no compose change is needed for this reason alone; `docs/performance.md` must simply record whatever the actual measuring machine's core count and Docker configuration were (`nproc`/`sysctl -n hw.ncpu`, `docker info`), not assume a specific value.
**Warning signs:** Two runs of the same scenario on the same machine produce latency numbers that differ by more than expected noise — check `cat /sys/fs/cgroup/.../cpu.stat` for `nr_throttled` before concluding the plugin regressed.

## Code Examples

### Emby sign-out reorder (AUTH-05, D-01/D-02)

Current code (`src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs:87-95`, read this session):
```csharp
var embyUserName = login?.User?.Name;
if (string.IsNullOrEmpty(embyUserName))
{
    LogResponseWithoutUserName(logger, baseUrl);
    return null;
}

await SignOutAsync(client, baseUrl, embyUserName, login?.AccessToken, cancellationToken).ConfigureAwait(false);
return new EmbyLogin(embyUserName, login?.User?.Policy?.EnableRemoteAccess ?? false);
```
`[ASSUMED]` — illustrative reorder, not the final diff (executor's call on exact shape):
```csharp
var embyUserName = login?.User?.Name;
await SignOutAsync(client, baseUrl, embyUserName ?? username, login?.AccessToken, cancellationToken).ConfigureAwait(false);
if (string.IsNullOrEmpty(embyUserName))
{
    LogResponseWithoutUserName(logger, baseUrl);
    return null;
}

return new EmbyLogin(embyUserName, login?.User?.Policy?.EnableRemoteAccess ?? false);
```
`SignOutAsync` already no-ops when `accessToken` is null/empty (`EmbyClient.cs:167-170`, verified), so this reorder is safe for every existing response shape; the only behavior change is that a token-bearing, no-user-name response now also signs out, using the **typed** `username` parameter (never a secret) as the log-message fallback since `embyUserName` is null in that case — this keeps the "never log a secret" rule intact without inventing a new placeholder value.

**The existing test this reorder must update** (`tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyClientTests.cs:179-192`, verified this session):
```csharp
[Theory]
[InlineData("{}")]
[InlineData("""{"User":{"Name":""},"AccessToken":"t"}""")]
[InlineData("not json")]
public async Task Login_ReturnsNull_WhenEmbyResponseHasNoUserName(string json)
{
    var handler = new StubHttpMessageHandler().Then(() => Json(HttpStatusCode.OK, json));

    var login = await CreateClient(handler).AuthenticateAsync(EmbyUrl, "alice", Password, CancellationToken.None);

    Assert.Null(login);
    Assert.Single(handler.Requests);
    AssertNoSecretsLogged();
}
```
Per `04-CONTEXT.md`'s own analysis: this one `[Theory]` must split, because after the fix the three rows no longer agree. `{}` and `"not json"` have no `AccessToken` field, so they still send exactly one request (matching D-02: an unparsable/tokenless response cannot sign out). The row `{"User":{"Name":""},"AccessToken":"t"}` **does** carry a token and must move to a new test asserting **two** requests (the login call, then the sign-out) — this is the row the fix is actually for, and D-01/D-02's split is confirmed directly against the pinned test file, not just against `04-CONTEXT.md`'s description of it.

### Toxiproxy initial config (no toxics, transparent by default)

```json
[
  {
    "name": "emby",
    "listen": "0.0.0.0:8096",
    "upstream": "emby:8096",
    "enabled": true
  }
]
```
Mounted via `toxiproxy-server -host 0.0.0.0 -config /etc/toxiproxy/toxiproxy.json` (the server's documented `-config` flag seeds proxies at startup with zero toxics, i.e. pure passthrough — every existing e2e test is unaffected). `emby-proxy.conf`'s `proxy_pass` then points at `http://toxiproxy:8096` instead of `http://emby:8096`, so Jellyfin's traffic always flows Jellyfin → `emby-proxy` (logs, unchanged) → `toxiproxy` (new hop) → `emby`.

### Adding a latency toxic for the "slow Emby" scenario, via `curl` (no `toxiproxy-cli` dependency)

```bash
curl -sS -X POST http://127.0.0.1:${TOXIPROXY_PORT:-18474}/proxies/emby/toxics \
  -H 'Content-Type: application/json' \
  -d '{"type":"latency","attributes":{"latency":3000,"jitter":100}}'
# ... run the scenario ...
curl -sS -X DELETE "http://127.0.0.1:${TOXIPROXY_PORT:-18474}/proxies/emby/toxics/latency_downstream"
```
`[ASSUMED]` — the toxic name (`latency_downstream`) follows toxiproxy's documented default naming (`{type}_{stream}`); confirm the exact name toxiproxy assigns at implementation time by reading its own creation response, which includes the generated `name` field.

### Fingerprint-write microbenchmark (bottleneck 3), extending the existing concurrency test pattern

Existing pattern (`tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyVerifiedPasswordsTests.cs:110-120`, described in `.planning/codebase/TESTING.md:294`, not re-read verbatim this session — cited from the testing-conventions doc):
```csharp
// ConcurrentRecords_AreAllKept runs 50 Record() calls with Task.Run and Task.WhenAll,
// then a new store instance must match all 50 users.
```
`[ASSUMED]` — extend with a `Stopwatch` per call and report p50/p95, e.g.:
```csharp
var timings = new ConcurrentBag<TimeSpan>();
await Task.WhenAll(Enumerable.Range(0, concurrency).Select(i => Task.Run(() =>
{
    var sw = Stopwatch.StartNew();
    store.Record(userIds[i], hashes[i]);
    timings.Add(sw.Elapsed);
})));
// report timings.OrderBy(...) percentiles
```
This needs no Docker, no Emby, and no Jellyfin container — only a real temp file, matching the existing test's own setup.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|---------------|--------|
| `emby-proxy` is the only hop between Jellyfin and Emby (logging only) | `emby-proxy` → `toxiproxy` → `emby` (fault injection added as a new, always-present hop) | This phase | Every existing e2e test is unaffected (0 toxics = passthrough); only load-test scripts touch toxiproxy's admin API |
| Three "unmeasured" performance bottlenecks, per `.planning/codebase/CONCERNS.md` (2026-09-16 snapshot) | Four measured bottlenecks (Phase 4 discussion added the per-login account save, D-13) | This phase | `docs/performance.md` becomes the new source of truth; `CONCERNS.md`'s "unmeasured" language is superseded, not deleted (repo convention: corrections are appended, not rewritten) |

**Deprecated/outdated:** `.planning/codebase/CONCERNS.md`'s "all items here are unmeasured. No load test exists" (line 70) is superseded by this phase's `docs/performance.md`, once written — do not delete the CONCERNS.md line; per this repo's own convention (`.planning/codebase/CONCERNS.md:9`), a later-proven-wrong entry keeps its original text and gains a dated correction note, which the 2026-09-20 discussion already began doing for this exact section (see the two "Correction" notes already in `CONCERNS.md:77` and `:83` and `:89`).

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|----------------|
| A1 | The bottleneck-4 ("per-login Jellyfin account save") measurement must be derived (total latency minus Emby round-trip minus bottleneck-3's number) rather than directly measured | Architecture Patterns, Pattern 3 table | If the plan instead tries to measure it directly and finds no way to isolate it from Emby latency or fingerprint-write cost, this could stall PERF-02's fourth threshold; mitigate by confirming this derivation approach with the maintainer before the plan locks it in |
| A2 | toxiproxy's generated toxic name follows the `{type}_{stream}` convention (`latency_downstream`) | Code Examples, "Adding a latency toxic" | A wrong assumed name only breaks the toxic-removal `curl` call between scenarios; low risk, easily caught by reading the creation response at implementation time |
| A3 | 10 concurrent first logins is an adequate e2e burst size for D-06's invariant test (matching D-10's load-test scale) | Architecture Patterns, Pattern 1 | If Jellyfin's actual lock contention window is shorter than the time it takes to fire 10 backgrounded `curl` calls from bats, the test may not reliably produce genuine overlap; the plan should confirm this empirically during implementation, not assume it holds from research alone |
| A4 | The Jellyfin-core lock-key and `ArgumentException` behavior quoted in `04-CONTEXT.md` (verified by the orchestrator against `jellyfin/jellyfin` tag `v12.1` during the discussion session) still holds — this research session did not re-read Jellyfin's own source, only this repo's plugin code and `04-CONTEXT.md`'s recorded quotes | User Constraints; Architecture Patterns, Pattern 3 | If Jellyfin's lock behavior changed between the discussion session and plan/execution time (e.g., a Jellyfin image tag bump), the account-pool sizing rules in Pattern 3 and Pitfall 2 would need re-verification against the new `jellyfin/jellyfin` image tag pinned in `e2e/compose.yaml` |

## Open Questions

1. **The exact numeric pass/fail threshold for each of the four bottlenecks (D-11's core requirement).**
   - What we know: the methodology for measuring each (Architecture Patterns, Pattern 3 table), the scale to measure at (D-10: ~10 concurrent first logins, tens of Emby users, one larger stress run), and that a single-flight guard for bottleneck 2 "cuts requests, not latency" so its threshold must be phrased as a request-count bound, not a time bound (per `04-CONTEXT.md`'s own cited external measurement: 50→1 requests, 215ms vs 208ms wall time unchanged).
   - What's unclear: the actual numbers. These depend on the measuring machine (this research found no reason to assume a specific host) and cannot be set before a first exploratory run.
   - Recommendation: the plan should schedule a short, throwaway calibration run first (not the final measured run) purely to observe realistic baseline numbers on the actual target machine, then write the four thresholds into the plan from that baseline before the real, recorded run — this preserves D-11's "write the threshold before you see the result it judges" discipline while still grounding the threshold in a real number rather than a guess.

2. **Whether the load test runs in CI at all, or only on demand (explicitly left to Claude's discretion in `04-CONTEXT.md`).**
   - What we know: the `e2e` CI job already has a 30-minute timeout (`ci.yml:49`, verified this session); adding a load test to CI risks exceeding it, and CI runners are noisy-neighbor environments that make Pitfall 5 (CFS throttling / non-reproducible numbers) worse, not better.
   - What's unclear: whether the maintainer wants the four numbers re-verified on every PR (catching a regression automatically) or only on demand (treating them as a point-in-time audit, refreshed manually).
   - Recommendation: on-demand only (`mise run load-test`, no CI job), consistent with the "realistic home-server scale" framing in D-10 and this plugin's single-maintainer deployment context; revisit only if a future phase's CI budget and hosting stability change.

3. **Where the shared Jellyfin-log-reading helper for TEST-06 lives (explicitly left to Claude's discretion).**
   - What we know: `90-jellyfin-log.bats:9` already has an inline `docker compose ... logs jellyfin` pattern; TEST-06 needs to read the log four times (once per invalid-settings case) within one new file.
   - What's unclear: whether four inline calls within `70-invalid-settings.bats` are acceptable, or whether a shared helper (e.g., `jellyfin_log_contains PATTERN`) belongs in `e2e/helpers.bash` since it would be the second file needing this capability.
   - Recommendation: a small helper in `e2e/helpers.bash` (one function, reused four times within the same new file) is proportionate even though only one file needs it today — it keeps the four assertions consistent and shortens the new test file; either choice is a naming/organization decision with no behavior difference, per the discretion note in `04-CONTEXT.md`.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| Docker | e2e stack, load-test stack | ✓ | 29.4.0 `[VERIFIED, this session]` | — |
| Docker Compose | e2e stack, load-test stack | ✓ | v5.1.2 `[VERIFIED, this session]` | — |
| dotnet SDK | build, unit tests, microbenchmark | ✓ | 10.0.401 `[VERIFIED, this session]`, matches `.mise.toml` pin | — |
| bats | e2e tests, new `60-`/`70-` files | ✓ | 1.14.0 `[VERIFIED, this session]`, matches `.mise.toml` pin | — |
| jq | e2e/load-test helpers | ✓ | 1.8.2 `[VERIFIED, this session]`, matches `.mise.toml` pin | — |
| k6 | load test | ✗ (not yet pinned) | — | Add `k6 = "2.2.0"` to `.mise.toml`; confirmed installable via `aqua:grafana/k6` `[VERIFIED, this session]` |
| toxiproxy image | fault injection | ✗ (not yet in `e2e/compose.yaml`) | — | `ghcr.io/shopify/toxiproxy:2.12.0`, confirmed pullable this session; add as a new Compose service |
| Node.js | settings-page tests (unaffected by this phase) | ✓ | v24.19.0 local shell `[VERIFIED, this session]` (mise pins 24.21.0 — close enough that this is not a blocker; mise controls the CI/task-invoked version regardless of the ambient shell version) | — |

**Missing dependencies with no fallback:** none — both new tools (k6, toxiproxy) have confirmed, working installation paths.
**Missing dependencies with fallback:** none needed; both pins verified live this session.

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit v3 (`xunit.v3` 4.0.1) on Microsoft.Testing.Platform, for the C# unit tests and the new fingerprint-write microbenchmark; bats 1.14.0 for e2e; k6 2.2.0 (new) for the load test |
| Config file | `global.json` (test runner selection); `.mise.toml` (tool pins); no k6 config file needed — scenarios are plain `.js` files |
| Quick run command | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` (unit); `bats e2e/60-concurrent-logins.bats` / `bats e2e/70-invalid-settings.bats` (one e2e file at a time) |
| Full suite command | `mise run test` (unit + script + JS); `mise run e2e` (all e2e files); `mise run load-test` (new — the four measured scenarios) |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|--------------------|--------------|
| AUTH-05 | Sign-out fires whenever a token is present, incl. no-user-name response | unit | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter Login_ReturnsNull_WhenEmbyResponseHasNoUserName` (after the theory splits) | ❌ Wave 0 — split the existing theory (`EmbyClientTests.cs:179-192`) |
| TEST-05 (unit) | Race-loser path refused, no delete attempted | unit | `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx --filter RefusesTheLogin_WhenCreateUserFailsBecauseJellyfinRejectsTheName` | ✅ Already exists (`EmbyAuthenticationProviderTests.cs:196-207`) |
| TEST-05 (e2e) | Concurrent first logins: exactly one account, no HTTP 500 | e2e | `bats e2e/60-concurrent-logins.bats` | ❌ Wave 0 — new file (D-09) |
| TEST-06 | Invalid settings on a running server: refused + Error log | e2e | `bats e2e/70-invalid-settings.bats` | ❌ Wave 0 — new file (D-09) |
| PERF-01 | Four numbers, mise task, separate account pool | load test | `mise run load-test` | ❌ Wave 0 — new mise task, new scenario scripts, new account-pool script |
| PERF-02 | Threshold before run; fix or accept with number+environment | docs + load test | (documentation gate, not a command) | ❌ Wave 0 — `docs/performance.md` |

### Sampling Rate

- **Per task commit:** `dotnet test --solution Jellyfin.Plugin.EmbyAuth.slnx` for C# changes; the relevant single `bats e2e/NN-*.bats` file for e2e changes.
- **Per wave merge:** `mise run test` and `mise run e2e`.
- **Phase gate:** `mise run test`, `mise run e2e`, and one full `mise run load-test` calibration + recorded run, before `/gsd-verify-work`.

### Wave 0 Gaps

- [ ] `e2e/60-concurrent-logins.bats` — covers TEST-05 (e2e half)
- [ ] `e2e/70-invalid-settings.bats` — covers TEST-06
- [ ] `.mise.toml` — add `k6 = "2.2.0"`, add `[tasks.load-test]`
- [ ] `e2e/compose.yaml` — add the `toxiproxy` service; `e2e/toxiproxy.json` — initial proxy config
- [ ] `emby-proxy.conf` — `proxy_pass` target becomes `toxiproxy:8096`; add `$request_time` to `log_format`
- [ ] A load-test scripts directory (`load-test/` or `tests/load/`) with four scenario scripts and an account-pool provisioning script
- [ ] `docs/performance.md` — new file (D-12)
- [ ] A fingerprint-write microbenchmark (extends `EmbyVerifiedPasswordsTests.cs`'s existing concurrency pattern, or a standalone diagnostic — planner's call)

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-------------------|
| V2 Authentication | Yes | Unchanged by this phase; AUTH-05 only closes a session-lifecycle gap, not an authentication decision (D-01's own framing: "AUTH-05 is a session-hygiene requirement, not an access-control one") |
| V3 Session Management | Yes | `POST /Sessions/Logout` is the existing, correct mechanism (`.claude/rules/plugin.md` §Emby); this phase only widens *when* it fires, never changes *how* |
| V4 Access Control | No new surface | The migration/target validation this phase touches indirectly (`EmbyAuthPlugin.UpdateConfiguration`) is unchanged; TEST-06 adds coverage, not new logic |
| V5 Input Validation | Yes | `EmbyAuthSettings.FindProblem` already validates the four cases TEST-06 covers; no new validation code, only new test coverage |
| V7 Error Handling & Logging | Yes | D-04's reworded log message and D-07/D-08's Error-log assertions are exactly this category — the existing rule ("never log a password or the API key," `.claude/rules/plugin.md:56`) must hold for every new log line this phase adds, including the load test's own scripts (never print the account-pool passwords to a load-test report) |

### Known Threat Patterns for this stack

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|----------------------|
| A stale Emby session left open by an unreadable success response (D-02's documented, accepted limit) | Information Disclosure (a session token remains valid on Emby longer than necessary) | Documented as a known limit in `docs/how-it-works.md`, not silently accepted — the maintainer's own framing: "it's up to Emby to return a good payload" |
| A load-test account pool's passwords leaking into a load-test report or CI log | Information Disclosure | Follow the existing `NAME-emby-pass[-N]` pattern so `90-jellyfin-log.bats`'s existing scanner also covers load-test passwords if the load test ever shares a Jellyfin log with the e2e suite; never print pool credentials to stdout/report files |
| A toxiproxy admin API left reachable with no auth beyond Docker network isolation | Tampering (an attacker on the Docker network could add/remove toxics) | Acceptable for this local-only, test/CI-scoped stack — toxiproxy's admin port (`8474`) is not published to the host in the config above unless a script needs it, and the existing Docker network already isolates `e2e/compose.yaml`'s services from anything outside the Compose project |

## Sources

### Primary (HIGH confidence)

- `src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs` — read in full, this session
- `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs` — read in full, this session
- `src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs` — read in full, this session
- `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs` — read in full, this session
- `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthSettings.cs` — read in full, this session
- `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthPlugin.cs` — read in full, this session
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs`, `EmbyClientTests.cs`, `EmbyAuthenticationProviderTests.cs` — read in full or in relevant ranges, this session
- `e2e/setup_suite.bash`, `e2e/helpers.bash`, `e2e/10-login-checks.bats`, `e2e/40-emby-outage.bats`, `e2e/90-jellyfin-log.bats`, `e2e/compose.yaml`, `e2e/emby-proxy.conf` — read in full, this session
- `.mise.toml`, `.github/workflows/ci.yml` — read in full, this session
- GitHub API `GET /repos/grafana/k6/releases/latest` and `GET /repos/Shopify/toxiproxy/releases/tags/v2.12.0` — queried live, this session
- `docker manifest inspect ghcr.io/shopify/toxiproxy:2.12.0` and `docker pull ghcr.io/shopify/toxiproxy:2.12.0` — run live, this session

### Secondary (MEDIUM confidence)

- `04-CONTEXT.md` and `04-DISCUSSION-LOG.md` — read in full, this session; the Jellyfin v12.1 core-source quotes within them (`UserManager.cs`, `CryptographyProvider.cs`) were verified by the orchestrator during the prior `/gsd-discuss-phase` session, not re-verified against Jellyfin source by this research session
- `.planning/research/PITFALLS.md`, `ARCHITECTURE.md`, `STACK.md`, `SUMMARY.md` — read in full, this session; dated 2026-09-17, predating Phases 1-3's execution, so some file:line references inside them are stale against the current tree (this research re-verified every claim it relied on against the current source, listed above)
- `.planning/codebase/CONCERNS.md`, `TESTING.md` — read in full, this session

### Tertiary (LOW confidence)

- WebSearch result summarizing k6's release cadence (used only to corroborate, not establish, the GitHub-API-verified version) — not independently authoritative, superseded by the direct API query above

## Metadata

**Confidence breakdown:**
- Standard stack (k6, toxiproxy): HIGH — both versions and installability verified live against GitHub/GHCR this session, not from training data or an unverified web search
- Architecture (test levels, data flow, pool-sizing rules): HIGH for the repo-code parts (all files read this session); MEDIUM for the Jellyfin-core lock behavior underlying the pool-sizing rules, since this session relied on `04-CONTEXT.md`'s recorded prior verification rather than re-reading Jellyfin source directly
- Pitfalls: HIGH for the two repo-specific ones (existing test coverage, `UpdateConfiguration` validation gap — both directly read this session); MEDIUM for the account-pool and bats-fd3 pitfalls, which combine this session's own reasoning with prior research/external citations
- Numeric thresholds (PERF-02): explicitly **not** determined by this research — see Open Questions #1; this is a planning-time decision requiring a calibration run

**Research date:** 2026-09-20
**Valid until:** 30 days for the repo-code findings (stable unless Phases 1-3's code changes further); re-verify the k6/toxiproxy version pins at plan/implementation time regardless of this window, per this repo's own "look up the current stable version" rule
