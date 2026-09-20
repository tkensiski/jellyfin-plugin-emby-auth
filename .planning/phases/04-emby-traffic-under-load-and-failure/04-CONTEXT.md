# Phase 4: Emby Traffic Under Load and Failure - Context

**Gathered:** 2026-09-20
**Status:** Ready for planning

<domain>
## Phase Boundary

This phase makes the plugin's behavior toward Emby predictable when Emby is slow, when logins arrive at the same time, and when the settings are invalid, and it replaces every "unmeasured" note in `.planning/codebase/CONCERNS.md` §Performance Bottlenecks with a number. It covers AUTH-05, TEST-05, TEST-06, PERF-01, and PERF-02.

In scope: the Emby sign-out path in `EmbyClient.AuthenticateAsync`, a concurrent-first-login test at both the unit and the end-to-end level, an end-to-end test for invalid settings on a running server, a load test driven from a mise task with its own account pool, and a fix-or-accept verdict for each measured bottleneck.

Out of scope: everything in Phases 5 and 6 — the CI release gate, the zizmor and gitleaks work, the history scan, the public repository switch, the Pages manifest, and the release of version 0.9.0.0 and 1.0.0.0. Also out of scope: any change to the move-target settings or the migration list, which Phase 3 completed.

</domain>

<decisions>
## Implementation Decisions

### The Emby session (AUTH-05)

- **D-01:** `SignOutAsync` moves ahead of the user-name check in `EmbyClient.AuthenticateAsync`. Today the method returns at `EmbyClient.cs:88-92` when the response carries no user name, so a response that carries an `AccessToken` and no name never reaches the sign-out at `:94`. After the change every readable token ends its session, and the login is still refused. The refusal itself does not change: `AuthenticateAsync` already returns `null` for that response, and `EmbyAuthenticationProvider.cs:96-97` already turns `null` into `AuthenticationException`. AUTH-05 is a session-hygiene requirement, not an access-control one.
- **D-02:** A success response whose body cannot be parsed is documented, and no code tries to recover the token from it. The catch at `EmbyClient.cs:81-85` runs before anything is deserialized, so the token is inside the body that failed to parse. The maintainer's rule: "it's up to Emby to return a good payload." `docs/how-it-works.md` states the limit — one Emby session can stay open for such a response, and the plugin cannot end it — instead of claiming the plugin ends every session Emby opens. This follows the Phase 3 D-18 rule that the plugin never claims behavior it has not verified.

### Concurrent first logins (TEST-05)

- **D-03:** The losing login stays refused, and no production behavior changes. Jellyfin resolves the user name **before** it takes the lock (`UserManager.cs:573`) and re-resolves inside the lock only `if (user is not null)` (`:579-581`), so the second concurrent login for an unknown name still sees `resolvedUser == null`, calls `CreateUserAsync`, and gets `ArgumentException` from `:619-623`. `EmbyAuthenticationProvider.cs:183-187` catches it and refuses the login with HTTP 401. Exactly one account, no HTTP 500: ROADMAP criterion 2 already holds against today's code, so TEST-05 is a regression test that locks the behavior in. Rejected: re-resolving the account by name and saving the password onto it, which contradicts Phase 1 D-02 ("no account lookup by name, no retry").
- **D-04:** The Error entry that the losing login produces is reworded. `LogCreateAccountFailed` (`EmbyAuthenticationProvider.cs:259-260`) tells the administrator to rename the user on Emby, which is the wrong remedy for a benign race, and the load test's concurrent scenario will produce these entries in volume. One catch and one Error entry stay; the message states both causes and the remedy for each. No detection logic and no second branch, because Jellyfin throws `ArgumentException` for a duplicate name and for an invalid name alike, so the exception type cannot separate them.
- **D-05:** TEST-05 covers both levels. The end-to-end test is required by `CLAUDE.md` — the behavior is entirely Jellyfin's lock — and criterion 2 asks for logins "through Jellyfin". The unit test drives the same path deterministically by making `FakeUserManager.CreateUserAsync` throw `ArgumentException`, so a later change to the catch block goes red without Docker.
- **D-06:** The end-to-end test asserts invariants only: exactly one Jellyfin account for that name, every response is 200 or 401, and none is 500. It does **not** require that a refusal occurred. When the burst does not overlap, the later logins resolve the existing user, take `SavePasswordAsync`, and return 200 — a correct outcome that a "require a refusal" assertion would fail on a loaded CI machine. The deterministic proof that the duplicate-name path is handled lives in D-05's unit test instead.

### Invalid settings on a running server (TEST-06)

- **D-07:** The end-to-end test covers all four invalid cases that `EmbyAuthSettings.FindProblem` produces (`EmbyAuthSettings.cs:52-69`): a blank Emby server URL, a URL that is not absolute http or https, a URL that carries user info, and a blank API key. Each case saves the settings on the running server, shows that a login on the Emby login method is refused, and shows the matching problem sentence in the Jellyfin log at Error level. Saving is possible because `EmbyAuthPlugin.UpdateConfiguration` (`EmbyAuthPlugin.cs:76-88`) refuses only an unusable migration target or password-set target; it does not validate the URL or the API key.
- **D-08:** The user-info case embeds a credential that matches the pattern `90-jellyfin-log.bats:16` already greps for, for example `http://user:leak-emby-pass@emby-proxy:8096`. The existing whole-log check then proves that the plugin never writes the configured URL into the Jellyfin log — coverage for a path nothing checks today, at no extra cost. `LogSettingsInvalid` (`EmbyAuthenticationProvider.cs:268-269`) logs only `{Problem}`, a static sentence, so this is expected to pass. A failure here is a real finding, not a test defect.

### End-to-end test layout

- **D-09:** Two new files, `60-concurrent-logins.bats` and `70-invalid-settings.bats`, each owning its Jellyfin accounts in `setup_file` and its own `reset_plugin_config`. This follows the one-topic-per-file rule in `.claude/rules/e2e.md` and keeps `90-jellyfin-log.bats` last. Rejected: folding the invalid-settings cases into `10-login-checks.bats`, which would give the first file in the suite settings mutation that every later file then depends on being undone.

### The load test and the bottleneck verdicts (PERF-01, PERF-02)

- **D-10:** The headline numbers are measured at the scale this plugin actually serves — a home server, tens of Emby users, a burst of about ten concurrent first logins — and one larger stress run locates the cliff. The realistic run decides the verdicts; the stress run is one extra scenario, not a second suite.
- **D-11:** Each bottleneck gets a written pass-or-fail threshold in the plan **before** anything is measured, the same discipline as writing the test first. A measurement that misses its threshold is fixed in this phase; one that meets it is accepted in the docs with its number. No fix is pre-committed — in particular the single-flight guard on the Emby user list lives or dies on its own number like the other two.
- **D-12:** A new `docs/performance.md` carries the method, the environment each number was measured in, the threshold, the measured figure, and the verdict per item. `PITFALLS.md:239-246` is the reason the environment is not optional: a container capped modestly below its burst need can spend a large fraction of wall-clock time blocked by the CFS scheduler, so a number without its environment cannot be read. `docs/how-it-works.md` stays a behavior document.
- **D-13:** The per-login write cost is measured as a fourth item, with its own threshold and verdict, which answers the Phase 1 D-08 deferral ("the write cost belongs to PERF-02 in Phase 4, which measures it first"). No fix is authorized in advance. If the number justifies one, note the trade: skipping the writes means verifying the saved hash against the typed password first, and a PBKDF2 verify is deliberately expensive, so the change may cost more CPU than the disk and database writes it removes. It also touches the one path that guards the core value, and AUTH-01's wording would need checking against a skip. — **Reversibility:** costly if a fix is taken — it changes the login path that AUTH-01 governs and the assertion in the Phase 1 criterion 3 end-to-end test.

### Claude's Discretion

The maintainer did not select these two areas, so the researcher and the planner settle them. The constraints below are binding; the choices inside them are not.

- **The load-test harness: what drives the load, and where it runs.** `.planning/research/STACK.md` offers k6 2.2.0 as a mise-pinned CLI (HIGH confidence). `.planning/research/SUMMARY.md:88` flags the compose wiring as unsettled. Constraints: `mise run package`-style task per CI job (`CLAUDE.md`); a separate pool of test accounts, because Jellyfin locks an account out after 3 failed attempts (`PITFALLS.md:216-232`); every pool account must exist on Emby before the run starts, because the user list is cached for 60 seconds (`.claude/rules/e2e.md`); pin exact versions and look up the current stable one. Also undecided: whether the load test runs in CI at all, or only from a mise task on demand — the e2e job already has a 30-minute timeout (`ci.yml:49`).
- **How Emby is made slow or faulty.** `.planning/research/ARCHITECTURE.md` §Change 6 offers a fault-injection proxy beside the existing logging `emby-proxy` (toxiproxy 2.12.0, MEDIUM confidence, unverified against this toolchain) or a delay built into `e2e/emby-proxy.conf`. Stock `nginx:1.30.5-alpine` has no delay directive, so the second route means custom code. Constraint: a new image is pinned and audited to the same standard as `emby/embyserver` and `nginx` already are in `e2e/compose.yaml`, and `scripts/dev-env.sh` reuses that file, so `up` and `down` are re-checked after any change (`.claude/rules/e2e.md:13`).
- The burst size for D-06's end-to-end test, and whether the Jellyfin-log reader that D-07 needs becomes a shared helper in `e2e/helpers.bash` or stays inline as it is in `90-jellyfin-log.bats:9`.
- Which new Emby user names `e2e/setup_suite.bash` gains for the two new files, following the one-name-set-per-file rule.
- Whether `docs/performance.md` is linked from `README.md` and the `docs/` index.
- Exact wording throughout, within the rule that no message repeats the Emby URL or the API key.
- Test file names and the split across files, following `.planning/codebase/TESTING.md`.

</decisions>

<roadmap_change_required>
## Required Changes to the Roadmap

Make these before planning, so the plan is verified against criteria the plugin will actually meet.

1. **Criterion 1 overclaims.** It reads "Whenever Emby returns an access token, the plugin sends `POST /Sessions/Logout`". D-02 states that a success response whose body cannot be parsed hides its token from the plugin, so the criterion is false for that case. Reword it to cover every token the plugin can read, and add that `docs/how-it-works.md` states the unreadable-response limit. AUTH-05 in `.planning/REQUIREMENTS.md` carries the same wording and needs the same change.
2. **Criteria 4 and 5 name three bottlenecks; the phase now reports four.** D-13 adds the per-login account save and fingerprint write as a measured item with its own threshold and verdict. Add it to criterion 4's list of reported numbers and to criterion 5's fix-or-accept rule, so the phase is verified against what it delivers. D-11's "thresholds written before the run" belongs in criterion 5 as well, because it is what makes the verdict checkable.

</roadmap_change_required>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase requirements and prior decisions

- `.planning/ROADMAP.md` §Phase 4 — the goal and the five success criteria, subject to the two changes above.
- `.planning/REQUIREMENTS.md` — AUTH-05, TEST-05, TEST-06, PERF-01, PERF-02.
- `.planning/PROJECT.md` §Core Value, §Constraints, §Key Decisions — the rule that no password Emby did not verify opens an account, and the decision that the performance work starts with a load test.
- `.planning/phases/01-account-creation-and-login-security/01-CONTEXT.md` — D-02 (the plugin never compensates for a failure in Jellyfin's user store; no account lookup by name), which D-03 applies; D-08 and §Deferred "Skip the hash and fingerprint write", which D-13 answers; D-09 (hand-written doubles, no mocking library), which D-05 follows.
- `.planning/phases/03-migration-status-and-target/03-CONTEXT.md` — D-18 (the plugin never claims behavior it has not verified), which D-02 applies; D-22 (the shared e2e stack is the production topology).
- `.planning/codebase/CONCERNS.md:51-56` — the Emby session that can stay open, confirmed by reading. `:66-84` — the three unmeasured bottlenecks, with the file and line references.

### Repository rules

- `.claude/rules/plugin.md` §Jellyfin login flow — login methods run inside a lock and all unknown names share `Guid.Empty`; only `AuthenticationException` is caught, anything else becomes HTTP 500. §Emby — `POST /Sessions/Logout` with the session token ends the session and revokes the token.
- `.claude/rules/e2e.md` — Emby users are created only in `setup_suite.bash`; each file uses its own names; the password naming pattern that `90-jellyfin-log.bats` scans for; `scripts/dev-env.sh` reuses `compose.yaml` and `helpers.bash`.
- `CLAUDE.md` — test first then break the code once; `mise run e2e` for every change under `src/`; warnings are errors; one mise task per CI job; pin exact versions.

### Research

- `.planning/research/ARCHITECTURE.md` §Change 6 — the fault-injection options, what `emby-proxy` does today, and the data flow a load test would add.
- `.planning/research/PITFALLS.md:216-232` — Jellyfin's 3-attempt lockout and why the load test needs its own account pool. `:236-252` — Docker CFS throttling and why every number carries its environment. `:274-282` — the cache-miss stampede entry for `EmbyUserDirectory`.
- `.planning/research/STACK.md` — k6 2.2.0 and toxiproxy 2.12.0 with their confidence levels; re-check both at plan time.
- `.planning/research/SUMMARY.md:88` — load-test tooling is an open flag for this phase.

### Code this phase changes

- `src/Jellyfin.Plugin.EmbyAuth/EmbyClient.cs:87-94` — the early return D-01 removes; `:165-186` — `SignOutAsync`; `:81-85` — the unreadable-response catch D-02 documents.
- `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthenticationProvider.cs:183-187` — the catch that refuses the race loser; `:259-260` — the message D-04 rewords; `:268-269` — the Error entry D-07 asserts on; `:226-242` — `SavePasswordAsync`, whose per-login save D-13 measures.
- `src/Jellyfin.Plugin.EmbyAuth/EmbyUserDirectory.cs:55-83` — the cache with no single-flight guard; `:76` — the linear scan the stress run exercises.
- `src/Jellyfin.Plugin.EmbyAuth/EmbyVerifiedPasswords.cs:41-70` — `Record()`, the write under the lock; `:53-56` — the skip-if-unchanged branch that almost never fires (see §Existing Code Insights); `:78-90` — `Matches()`, which waits on the same lock.
- `src/Jellyfin.Plugin.EmbyAuth/EmbyAuthSettings.cs:44-69` — the four problems D-07 covers.
- `e2e/setup_suite.bash:30-36` — where the new Emby users go; `e2e/helpers.bash:159-170` — `reset_plugin_config` and `set_plugin_config`; `e2e/90-jellyfin-log.bats:9-24` — the whole-log scan D-08 relies on; `e2e/compose.yaml` — where a fault-injection service would go.
- `.mise.toml` — the `[tasks.e2e]` neighbour a load-test task joins, and the tool pins.

### Analysis and conventions

- `.planning/codebase/TESTING.md` — unit test conventions, `TestDoubles.cs`, the e2e layout, and the pre-commit hook file patterns.
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs` — `FakeUserManager`, which D-05 makes throw `ArgumentException`; `StubHttpMessageHandler`, which D-01's unit test drives; `CapturingLogger<T>`, which asserts D-04's message.
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyClientTests.cs:181-192` — `Login_ReturnsNull_WhenEmbyResponseHasNoUserName` is a `[Theory]` with two rows, `{"User":{"Name":""},"AccessToken":"t"}` and `"not json"`, both asserting `Assert.Single(handler.Requests)` at `:190`. **D-01 and D-02 split this theory**, because the two rows stop agreeing: the token row must then send two requests, and the unparsable row still sends one. `Login_ReturnsNull_WhenEmbyResponseHasAnInvalidCharset` (`:194-197`) is a second unparsable case and keeps its single-request assertion.

### Jellyfin source verified during this discussion (tag `v12.1`)

- `Jellyfin.Server.Implementations/Users/UserManager.cs:573` — `var user = GetUserByName(username);` then `LockAsync(user?.Id ?? Guid.Empty)`. The lookup happens **before** the lock.
- `Jellyfin.Server.Implementations/Users/UserManager.cs:579-581` — the re-resolve inside the lock runs only `if (user is not null)`, so an unknown name is never re-checked. This is why the race loser reaches `CreateUserAsync`.
- `Jellyfin.Server.Implementations/Users/UserManager.cs:619-623` — `ArgumentException` with "A user with the name '{0}' already exists.", built with `CultureInfo.InvariantCulture`.
- `Emby.Server.Implementations/Cryptography/CryptographyProvider.cs:18-30`, `:89-95` — `CreatePasswordHash` calls `GenerateSalt()` on every call, through `RandomNumberGenerator`. The hash string therefore differs on every login for the same password.

### External, verified during this discussion

- `https://bats-core.readthedocs.io/en/stable/writing-tests.html#file-descriptor-3-read-this-if-bats-hangs` — a backgrounded command in bats must close file descriptor 3 (`cmd 3>&-`) or bats hangs after the test. D-06's burst uses background jobs plus `wait`, so this applies directly. Corroborated by `bats-core/bats-core` issue #419.
- `https://learn.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/async-coordination-primitives-advanced` — `SemaphoreSlim` with `WaitAsync` is the single-flight primitive across an `await`; the C# `lock` and `Lock` are thread-affine and do not work there. `EmbyVerifiedPasswords` uses `Lock` with no `await` inside it, which is correct today.
- `https://alexeyfv.xyz/en/post/2025-12-17-cache-stampede-in-dotnet` and `https://dev.to/ssukhpinder/one-cache-miss-fifty-database-calls-1k5n` — `ConcurrentDictionary.GetOrAdd` and `IMemoryCache.GetOrCreateAsync` are **not** stampede-safe despite the names; a hand-written guard needs an explicit double-check after acquiring. Measured: a 50-request cold burst went from 50 origin calls to 1, with wall time unchanged (215 ms against 208 ms). **A single-flight guard reduces requests to Emby; it does not reduce login latency.** D-11's threshold for that bottleneck must therefore be written about Emby-side request count, not about login time.

</canonical_refs>

<code_context>
## Existing Code Insights

### A finding that changes the weight of bottleneck 3

`EmbyVerifiedPasswords.Record()` skips the file write when the stored fingerprint equals the new one (`:53-56`). The fingerprint is a SHA-256 of the **password hash**, and `CreatePasswordHash` generates a fresh random salt on every call, so the hash string differs on every login even when the password is identical. The skip therefore almost never fires for a repeat login, and the file is fully read, modified, serialized, written, and moved under the lock on **every** accepted Emby login. `.planning/codebase/CONCERNS.md:81-84` describes the skip as if it were the common case. It is not. The load test must measure the write path as the normal path, and `docs/performance.md` should state this, whatever the verdict.

### How the login lock actually distributes load

Jellyfin locks on `user?.Id ?? Guid.Empty`. An unknown name takes the shared `Guid.Empty` lock, so concurrent **first** logins serialize and a slow Emby stacks them up — that is bottleneck 1, and it is a migration-burst condition. A known name takes its own user ID, so repeat logins for different users run in parallel — which is the only condition under which the user-list cache-miss stampede can happen, because nothing serializes those callers. The two bottlenecks occur under opposite conditions and their scenarios must be driven separately.

### Reusable Assets

- `tests/Jellyfin.Plugin.EmbyAuth.Tests/TestDoubles.cs` — `StubHttpMessageHandler` already drives `EmbyClientTests`, so D-01's unit test is a new response fixture, not new machinery. `FakeUserManager` already fails a chosen method with a chosen exception type, which is exactly what D-05's unit test needs.
- `e2e/helpers.bash` — `login_status`, `jellyfin_user_id`, `user_by_name`, `set_plugin_config`, `reset_plugin_config`, and `emby_login_requests` cover everything the two new e2e files need except reading the Jellyfin log.
- `e2e/90-jellyfin-log.bats:9` — the `docker compose logs jellyfin` pattern D-07 reuses. The file checks only for leaked passwords and the API key; it does **not** fail on Error entries, so D-04's and D-07's deliberate Error output breaks nothing.
- `e2e/emby-proxy.conf` — the logging proxy already sits between Jellyfin and Emby, so a fault-injection layer is an addition to an existing hop rather than a new topology.

### Established Patterns

- Log methods are `[LoggerMessage]` partials at the end of each class, carrying names and status codes only, never a password or the API key.
- `EmbyUserDirectory` swaps an immutable `Snapshot` record through a `volatile` field (`:46`), so readers always see a consistent list. Any single-flight guard must keep that property rather than replace it with a lock around the read.
- Every refusal throws `AuthenticationException(InvalidLogin)`; only invalid settings use a different message, which is what D-07 asserts on.
- `EmbyVerifiedPasswords` uses `Lock` with no `await` inside the critical section. That is correct today and constrains any change: moving the file write off the lock must not introduce an `await` under it.

### Integration Points

- `.mise.toml` gains a load-test task beside `[tasks.e2e]`, and a tool pin if the harness needs one. `.github/workflows/ci.yml` changes only if the load test runs in CI — one mise task per job.
- `e2e/compose.yaml` and `e2e/setup_suite.bash` — where a fault-injection service and the account pool would land. `scripts/dev-env.sh` reuses both, so `up` and `down` are re-checked after any change.
- `docs/performance.md` is new. `docs/how-it-works.md` takes D-02's unreadable-response limit.
- `tests/Jellyfin.Plugin.EmbyAuth.Tests/EmbyClientTests.cs:181-192` — the existing `[Theory]` is the natural red test for D-01: changing the token row's expectation from one request to two fails against today's code. The same edit makes D-02 explicit in the suite, because the unparsable row keeps its single-request assertion and now states a deliberate limit rather than an accident.

</code_context>

<specifics>
## Specific Ideas

- The maintainer's framing for AUTH-05's residual case, in their own words: "its up to emby to return a good payload." The plugin states the limit and writes no recovery code for a malformed success response.
- The maintainer's instinct on hearing about the unparsable response was "if we don't have valid auth we should be blocking the request" — and that is already what happens. Keep this distinction explicit in `docs/how-it-works.md`: both the no-user-name response and the unparsable response refuse the Jellyfin login today. AUTH-05 is only about ending the session Emby already opened.
- D-08 turns an existing safety net into new coverage rather than adding a test: the invalid-settings test deliberately writes a credential the log scanner recognises, so the suite proves the URL never reaches the log. Prefer this shape over a new assertion wherever an existing check can be made to carry it.
- The thresholds in D-11 are written before the run on purpose, so the verdict cannot be formed after seeing the result it judges. Treat a missing threshold in the plan as a planning defect, not a detail to fill in during execution.

</specifics>

<deferred>
## Deferred Ideas

- **Make the losing concurrent login succeed** — re-resolve the account by name after the duplicate-name `ArgumentException` and save the password onto it, so both logins return 200. Rejected by D-03: it contradicts Phase 1 D-02, and it would need a guard against adopting an account on another login method. Revisit only if a real migration produces enough simultaneous first logins for the second-attempt cost to matter.
- **Branch the race-loser log on the duplicate-name case** and drop it to Information — rejected by D-04 because it couples the plugin to a Jellyfin message string for a log level. Revisit only if the reworded single message proves ambiguous in use.
- **Buffer the Emby response body and extract the token when JSON parsing fails** — rejected by D-02. It adds a hand-rolled parse path over a response Emby should never send, on the one code path that handles a password.
- **Require a refusal in the concurrency e2e test** — rejected by D-06 as a flake class. The unit test carries that proof instead.
- **Skip the hash and fingerprint write when the password did not change** — not rejected, and not authorized either. D-13 measures its cost and sets a threshold; a fix lands in this phase only if the number justifies it, with the PBKDF2 verify cost accounted for.
- **`HybridCache` for the Emby user list** — considered while researching the single-flight fix and dropped. It is a dependency and a caching framework for a cache with exactly one key; a `SemaphoreSlim(1,1)` with a double-check is the whole fix if a fix is warranted.
- **Retiring the fingerprint file** — carried from Phase 3 §Specific Ideas. Blocked upstream, not by this plugin, and not reopened by the finding above. The file being rewritten on every login is an argument about write cost, not about whether the record is needed.

</deferred>

---

*Phase: 4-Emby Traffic Under Load and Failure*
*Context gathered: 2026-09-20*
