# Phase 5: Public Repository - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-09-20
**Phase:** 5-public-repository
**Areas discussed:** Release gate mechanism, gitleaks scan mode and allowlist, zizmor pedantic persona, `.planning/` and `.claude/` in public, audit and approval ritual, README public-repo statements

---

## Area selection

Six gray areas were offered across two multi-select questions. The maintainer selected all six.

| Offered | Group | Selected |
|---|---|---|
| Release gate mechanism | CI and tooling | ✓ |
| gitleaks scan mode + allowlist | CI and tooling | ✓ |
| zizmor pedantic: gate or cleanup | CI and tooling | ✓ |
| `.planning/` and `.claude/` in public | Publishing | ✓ |
| Audit + approval ritual | Publishing | ✓ |
| README's public-repo statements | Publishing | ✓ |

---

## Release gate mechanism

### Where should the gate logic live?

| Option | Description | Selected |
|--------|-------------|----------|
| `scripts/release-gate.sh` + bats | New script beside `package.sh`, called from `release.yml`, tested in `tests/scripts/`. Follows the `check-tag` precedent. | ✓ |
| Inline `run:` block in `release.yml` | Fewer files, but untestable without `act`. | |
| Extend `scripts/package.sh` | Reuses the existing bats file, but mixes packaging and GitHub API concerns. | |

**User's choice:** `scripts/release-gate.sh` + bats

### What should the gate do when CI is still running on the tagged commit?

| Option | Description | Selected |
|--------|-------------|----------|
| Fail closed immediately | No passing `ci-success` at tag time means refuse. No poll loop, no timeout knob. | ✓ |
| Poll with a timeout | Wait up to N minutes, then decide. Handles tagging right after a merge. | |

**User's choice:** Fail closed immediately

### What counts as proof the commit passed?

| Option | Description | Selected |
|--------|-------------|----------|
| `ci-success` conclusion == success | Match the single aggregate check run `ci.yml` already publishes. Everything else refuses. | ✓ |
| Every check run must be success | Broader, but couples the gate to whatever checks exist later. | |

**User's choice:** `ci-success` conclusion == success

### How should `release-gate.sh` be made testable without hitting the GitHub API?

| Option | Description | Selected |
|--------|-------------|----------|
| Fake `gh` on PATH in bats | Temp dir with a fake `gh` printing fixture JSON. Exercises the `gh api` argument construction. | ✓ |
| Env var naming a checks JSON file | Matches `package.sh`'s env-override convention, but never tests the API call. | |
| Env var naming the command | Convention-consistent and still runs argument construction. | |

**User's choice:** Fake `gh` on PATH in bats

**Notes:** Research surfaced that `cancelled` and `skipped` must be hard failures rather than wait states. A repo-specific hazard was confirmed: `ci.yml:14-17` sets `cancel-in-progress: true`, so a superseded push to `main` leaves a genuinely `cancelled` `ci-success`, which the gate must refuse. The maintainer elected to leave step ordering and permission scoping to the planner.

---

## gitleaks scan mode and allowlist

### Which scan should `mise run lint` run?

| Option | Description | Selected |
|--------|-------------|----------|
| Full history (`gitleaks git`) | Measured 0.5 s over 204 commits. Makes REL-03 and PUB-01's history scan the same command. | ✓ |
| Working tree (`gitleaks dir`) | Matches what the other lint tools do, but a deleted-then-committed secret stays invisible. | |
| Staged only (`gitleaks git --staged`) | Cheapest, but CI has nothing staged, so the check would be theatre. | |

**User's choice:** Full history (`gitleaks git`)

### Which suppression mechanism for the test fixtures?

| Option | Description | Selected |
|--------|-------------|----------|
| `.gitleaks.toml` allowlist | Verified to clear history and working tree at once. Survives line moves. | ✓ |
| `.gitleaksignore` fingerprints | Maximally precise, but commit-pinned and accumulating. | |
| Inline `gitleaks:allow` comments | Cannot clear historical findings — old blobs lack the comment. | |

**User's choice:** `.gitleaks.toml` allowlist

### How broad should the allowlist be?

| Option | Description | Selected |
|--------|-------------|----------|
| Narrow: match the fixture values | Regexes for the specific fake values. A real secret pasted into `tests/` is still caught. | ✓ |
| Path-scoped to `tests/` + `.planning/` | Simple and verified, but blinds the scanner to the directory a stray credential would most likely land in. | |
| Path + rule scoped | Narrows the blind spot to the one noisy rule. | |

**User's choice:** Narrow: match the fixture values

**Notes:** Two claims were tested rather than assumed. A path allowlist cleared both scans to exit 0; so did a narrow two-regex value allowlist (`0123456789abcdef`, `sentinel-api-key-`), confirming the chosen option is implementable. All five historical findings were inspected and confirmed to be deliberate test fixtures — nothing required rotation.

---

## zizmor pedantic persona

### Should `mise run lint` move to `--persona=pedantic` permanently?

| Option | Description | Selected |
|--------|-------------|----------|
| Yes — pedantic becomes the gate | A later workflow edit reintroducing a finding fails CI. `zizmor` is pinned, so no surprise audits arrive. | ✓ |
| No — one-time cleanup | Fix the 7 now, leave lint on the default persona. | |

**User's choice:** Yes — pedantic becomes the gate

### How should the `anonymous-definition` finding on the `ci-success` job be resolved?

| Option | Description | Selected |
|--------|-------------|----------|
| `name: ci-success` (identical to id) | Satisfies zizmor while keeping the check-run name byte-identical. | ✓ |
| Suppress it, with a written reason | REL-02 permits it, and the job id is a real contract. | |
| Rename it and update the gate | Most readable, but makes the name a thing two files must agree on. | |

**User's choice:** `name: ci-success` (identical to id)

### What concurrency group should `release.yml` get?

| Option | Description | Selected |
|--------|-------------|----------|
| Group by ref, `cancel-in-progress: false` | Serializes releases without killing one mid-publish. | ✓ |
| Group by ref, `cancel-in-progress: true` | Mirrors `ci.yml`, but a re-pushed tag could cancel an in-flight publish. | |

**User's choice:** Group by ref, `cancel-in-progress: false`

**Notes:** A collision between REL-02 and REL-01 was found and verified live: this repository's check runs are named by job id (`ci-success`, `e2e`, `test`, `lint`, queried from the commits API for `ecee1ed`), because no job sets `name:`. zizmor's fix for `anonymous-definition` would therefore rename the check run the gate matches on. The chosen option resolves the collision at the cost of a name that carries no new information.

---

## `.planning/` and `.claude/` in public

### Should `.planning/` stay in the public repository?

| Option | Description | Selected |
|--------|-------------|----------|
| Keep it — publish as-is | 106 files stay visible. No history rewrite. | ✓ |
| Delete from HEAD only | Cosmetic — the files remain readable in history. | |
| Rewrite history to remove it | The only option that truly removes it, at the cost of every commit SHA. | |

**User's choice:** Keep it — publish as-is

### Should `CLAUDE.md` and `.claude/rules/*.md` stay public?

| Option | Description | Selected |
|--------|-------------|----------|
| Keep both | The Jellyfin/Emby behavior notes and the build contract are what a contributor needs. | ✓ |
| Keep rules, drop `CLAUDE.md` | Would require relocating the version-bump rule that Phase 6's DOCS-04 targets. | |

**User's choice:** Keep both

**Notes:** Checked before the question was put: all three phase security audits close clean (`verdict: SECURED` in 01 and 03, `status: verified` in 02), so publishing them discloses no open finding. The decisive framing was that deleting from HEAD achieves nothing once the full history is public.

---

## Audit and approval ritual

### How should the PUB-01 audit be run and evidenced?

| Option | Description | Selected |
|--------|-------------|----------|
| `scripts/pre-public-audit.sh` | Tracked, shellcheck-clean, prints a pass/fail report. Matches the workspace scripts rule. | ✓ |
| Checklist in the phase plan, run once | No artifact outlives its usefulness, at the cost of reproducibility. | |
| Checklist in `docs/` | Outlives the phase, but documents a one-time internal event for users. | |

**User's choice:** `scripts/pre-public-audit.sh`

### How should the PUB-03 approval and switch happen?

| Option | Description | Selected |
|--------|-------------|----------|
| You run the command yourself | The phase stops after the audit; the maintainer runs `gh repo edit --visibility public`. | ✓ |
| Checkpoint, then I run it | Self-contained and auditable, but an agent holds the trigger. | |

**User's choice:** You run the command yourself

### What ordering should the phase enforce?

| Option | Description | Selected |
|--------|-------------|----------|
| Strict: gate → audit → switch | Matches ROADMAP criterion 5 exactly. | ✓ |
| Parallel, converging at the switch | Marginally faster, but weakens the ordering evidence. | |

**User's choice:** Strict: gate → audit → switch

**Notes:** The counter-argument to the audit script — that a one-time gate becomes dead code the next day, which the "no speculative features" rule dislikes — was put explicitly and the maintainer chose the script anyway.

---

## README public-repo statements

### Where should the `targetAbi`-is-a-minimum statement live?

| Option | Description | Selected |
|--------|-------------|----------|
| In the Requirements section | Extends `README:14`; keeps version facts in one place. | |
| A new Compatibility section | Holds tested versions and the `targetAbi` floor together; a natural neighbour for Phase 6's catalog steps. | ✓ |
| Extend the Status line | Smallest diff, but buries a compatibility caveat in a status blurb. | |

**User's choice:** A new Compatibility section

### Should the now-false "repository is private" lines be fixed in this phase or left to Phase 6?

| Option | Description | Selected |
|--------|-------------|----------|
| Fix in Phase 5 | `README:21` and `:33` are false the moment PUB-03 lands. | ✓ |
| Leave to Phase 6 | DOCS-02 rewrites Install anyway, at the cost of a window where the README is wrong. | |

**User's choice:** Fix in Phase 5

### How should the README's claims be kept honest?

| Option | Description | Selected |
|--------|-------------|----------|
| State the version facts, claim nothing more | Follows Phase 3 D-18 — never claim unverified behavior. | ✓ |
| Add an explicit untested-versions warning | Clearer for a catalog browser, but adds length. | |

**User's choice:** State the version facts, claim nothing more

**Notes:** The maintainer answered the first question with "wtf is targetAbi". The term was explained from source — `scripts/package.sh:33-38` derives it from the `Jellyfin.Controller` version, and Jellyfin filters catalog entries with `Version.Parse(x.TargetAbi) <= appVer`, making it a floor with no ceiling — and the placement question was then re-asked and answered. That exchange is why D-18 in CONTEXT.md specifies writing the section for a reader who has never met the term.

---

## Claude's Discretion

Left to the planner, within the constraints recorded in CONTEXT.md:

- The exact `regexTarget` and regex form in `.gitleaks.toml`, provided no path exemption is used.
- Where the gate step sits in `release.yml` relative to `check-tag`, and the permission scoping the gate job needs.
- The descriptive `name:` values for the four jobs free to take one, and the comment wording for `release.yml:19`.
- The action-argument names for both new scripts, and the exact check set the audit script reports on.
- Whether `scripts/pre-public-audit.sh` gets bats coverage, and how much.
- Exact wording throughout the README and any new comments.

## Deferred Ideas

- Branch protection requiring `ci-success` on `main` — impossible until the repository is public (verified HTTP 403). Raise after PUB-03.
- A `SECURITY.md`, issue templates, or a contributing guide — offered at the final gate, not selected.
- `release-gate.sh` also verifying the tag is an ancestor of `main` — offered, not selected.
- The fate of the pushed `gsd/phase-*` branches once public — offered, not selected.
- Retiring `scripts/pre-public-audit.sh` once it has served its purpose.
