# Phase 4: Emby Traffic Under Load and Failure - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-09-20
**Phase:** 4-Emby Traffic Under Load and Failure
**Areas offered:** Load-test harness and stack, Slow-Emby simulation, Bottleneck verdicts (PERF-02), Concurrency and invalid-settings tests
**Areas discussed:** Concurrency and invalid-settings tests, Bottleneck verdicts (PERF-02)

---

## Concurrency and invalid-settings tests

### The losing concurrent login

| Option | Description | Selected |
|--------|-------------|----------|
| Refuse it (today's behavior) | HTTP 401, no production code, TEST-05 becomes a regression test. Matches Phase 1 D-02. | ✓ |
| Make the loser succeed | Catch the duplicate-name `ArgumentException`, re-resolve by name, save the password. Contradicts Phase 1 D-02. | |
| Refuse it, but log it correctly | Keep the 401 and fix the misleading Error message in the same change. | |

**User's choice:** Refuse it (today's behavior)
**Notes:** The log wording was raised separately later in the area and changed then (see below), so the outcome is the same as the third option reached in two steps.

### TEST-05 test level

| Option | Description | Selected |
|--------|-------------|----------|
| Both e2e and unit | E2E for the real Jellyfin invariants, unit for the deterministic duplicate-name path. | ✓ |
| E2E only | Matches criterion 2's wording literally; no fast deterministic guard on the catch block. | |
| Unit only | Fast, but cannot prove Jellyfin serializes on `Guid.Empty`; criterion 2's "through Jellyfin" unmet. | |

**User's choice:** Both e2e and unit

### TEST-06 scope and assertion strictness

| Option | Description | Selected |
|--------|-------------|----------|
| Two cases, assert the problem text | Blank URL and blank API key, asserting the matching sentence in the log. | |
| One case, assert the problem text | Blank URL only; cheapest. | |
| All four cases | Blank URL, unparsable URL, URL with user info, blank API key — each with its own refusal and Error assertion. | ✓ |
| Two cases, assert Error level only | Same two cases, asserting only that an Error entry exists. | |

**User's choice:** All four cases

### The user-info case and the log scanner

| Option | Description | Selected |
|--------|-------------|----------|
| Yes — make it scannable | Embed a credential matching `90-jellyfin-log.bats:16`, so the existing whole-log check proves the URL never reaches the log. | ✓ |
| No — use a neutral credential | Keeps the two tests independent. | |
| Yes, and add the API key too | Also assert the invalid API key never appears. | |

**User's choice:** Yes — make it scannable

### The race-loser Error message

| Option | Description | Selected |
|--------|-------------|----------|
| Reword the one message | One catch, one Error entry, both causes stated honestly. No detection logic. | ✓ |
| Leave it exactly as is | Zero code; message stays wrong for the race case. | |
| Branch on the duplicate-name case | Match the invariant "already exists" text and log the race at Information. | |

**User's choice:** Reword the one message
**Notes:** Raised because `ArgumentException` covers both a duplicate name and an invalid name, so the exception type alone cannot separate them, and because the load test's concurrent scenario will produce these entries in volume.

### Handling a race that may not occur

| Option | Description | Selected |
|--------|-------------|----------|
| Invariants only in e2e | Exactly one account, every response 200 or 401, none 500. The unit test carries the race proof. | ✓ |
| Require a refusal, with bounded retry | Proves the real race, at the cost of a retry loop. | |
| Require a refusal, no retry | Proves the race on every green run, but a loaded CI machine turns a correct plugin red. | |

**User's choice:** Invariants only in e2e

### Where the two new e2e tests live

| Option | Description | Selected |
|--------|-------------|----------|
| Two new files | `60-concurrent-logins.bats` and `70-invalid-settings.bats`, one topic each. | ✓ |
| Fold into existing files | Concurrency into `20-accounts.bats`, invalid settings into `10-login-checks.bats`. | |
| One combined new file | A single `60-*.bats` for both topics. | |

**User's choice:** Two new files

### AUTH-05 and the unparsable success response

| Option | Description | Selected |
|--------|-------------|----------|
| Document it, write no code | State the limit in `docs/how-it-works.md` rather than overclaiming. | ✓ |
| Buffer and extract the token | Recover the token from a body that already failed to parse. | |
| Say nothing about it | Ship the no-user-name fix and leave the gap unstated. | |

**User's choice:** Document it, write no code
**Notes:** The user first asked what the case actually was, and whether an invalid auth response should not simply block the request. It already does — both the no-user-name response and the unparsable response refuse the Jellyfin login today, so AUTH-05 is about ending the Emby session, not about access. With that clear, the user answered: "agreed, its up to emby to return a good payload."

---

## Bottleneck verdicts (PERF-02)

### Scale the numbers are judged against

| Option | Description | Selected |
|--------|-------------|----------|
| Realistic, plus one stress run | Tens of users and ~10 concurrent first logins for the headline numbers, plus one larger run to find the cliff. | ✓ |
| Realistic only | Smallest harness; nothing locates the cliff. | |
| Large scale | Hundreds of users and ~50 concurrent; numbers describe a server this plugin is unlikely to meet. | |

**User's choice:** Realistic, plus one stress run

### The fix-or-accept rule

| Option | Description | Selected |
|--------|-------------|----------|
| Written thresholds, set before the run | A pass-or-fail number per bottleneck stated in the plan before anything is measured. | ✓ |
| Measure first, then judge each case | The roadmap's literal reading; the verdict is formed after seeing the result it judges. | |
| Thresholds, plus a pre-committed single-flight fix | Same thresholds, but the user-list guard ships regardless. | |

**User's choice:** Written thresholds, set before the run
**Notes:** This makes the single-flight guard subject to its own number rather than pre-committed. Research finding that shaped the option: a single-flight guard cuts origin requests from N to 1 but leaves wall time unchanged, so its threshold has to be written about Emby-side request count, not login latency.

### Where the numbers are recorded

| Option | Description | Selected |
|--------|-------------|----------|
| A new `docs/performance.md` | Method, environment, threshold, measured figure, and verdict in one page. | ✓ |
| A section in `docs/how-it-works.md` | No new file; a behavior doc grows a measurement appendix. | |
| Generated report, verdicts only in docs | Full report in `artifacts/`, verdict sentence in docs; not visible to a reader of the repo. | |

**User's choice:** A new `docs/performance.md`

### The Phase 1 D-08 deferred write

| Option | Description | Selected |
|--------|-------------|----------|
| Measure it, treat it as a fourth item | Its own threshold and verdict in `docs/performance.md`; no fix pre-decided. | ✓ |
| In scope, and fix it if the threshold says so | Authorise the verify-then-skip change up front. | |
| Out of scope — measure only | Report the cost and change nothing. | |

**User's choice:** Measure it, treat it as a fourth item
**Notes:** Raised on the back of a verified finding: `CreatePasswordHash` generates a fresh salt per call (`CryptographyProvider.cs:18-30`, `:89-95`), so `EmbyVerifiedPasswords.Record()`'s skip-if-unchanged branch almost never fires and the fingerprint file is rewritten on every accepted login. Counter-argument recorded with the option: a PBKDF2 verify is deliberately expensive, so skipping the writes may cost more CPU than it saves in I/O.

---

## Claude's Discretion

The maintainer did not select these areas, so they are settled by research and planning within the constraints recorded in CONTEXT.md.

- The load-test harness: what drives the load, whether it shares the e2e Compose stack, how the account pool is provisioned, and whether it runs in CI.
- How Emby is made slow or faulty: a pinned fault-injection service beside `emby-proxy`, or a delay built into `emby-proxy.conf`.
- The burst size for the concurrency e2e test.
- Whether the Jellyfin-log reader becomes a shared helper in `e2e/helpers.bash`.
- Which new Emby user names `e2e/setup_suite.bash` gains.
- Whether `docs/performance.md` is linked from `README.md` and the `docs/` index.
- Exact wording, test file names, and the split across test files.

## Deferred Ideas

- Make the losing concurrent login succeed by re-resolving the account by name.
- Branch the race-loser log on the duplicate-name case and drop it to Information.
- Buffer the Emby response body and extract the token when JSON parsing fails.
- Require a refusal in the concurrency e2e test.
- Skip the hash and fingerprint write when the password did not change — measured here, not pre-authorized.
- `HybridCache` for the Emby user list.
- Retiring the fingerprint file — carried from Phase 3, blocked upstream.
