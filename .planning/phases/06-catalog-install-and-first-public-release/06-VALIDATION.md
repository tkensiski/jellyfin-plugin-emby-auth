---
phase: "6"
slug: "catalog-install-and-first-public-release"
# status lifecycle: draft (seeded by plan-phase) → validated (set by validate-phase §6)
# audit-milestone §5.5 distinguishes NOT-VALIDATED (draft) from PARTIAL (validated + nyquist_compliant: false) (#2117)
status: draft
nyquist_compliant: false
wave_0_complete: false
created: "2026-09-21"
---

# Phase 6 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | bats 1.14.0 (script tests and e2e) |
| **Config file** | none — bats needs none; `.mise.toml` pins the binary |
| **Quick run command** | `bats tests/scripts/manifest.bats tests/scripts/package.bats` |
| **Full suite command** | `mise run test` (unit + script + JS), then `mise run e2e` (Docker) |
| **Estimated runtime** | ~20 seconds for the quick run; `mise run e2e` is minutes, Docker-bound |

---

## Sampling Rate

- **After every task commit:** Run `bats tests/scripts/manifest.bats` and `bats tests/scripts/package.bats`, whichever the task touches
- **After every plan wave:** Run `mise run test`, then `mise run e2e` — `.claude/rules/plugin.md` requires an end-to-end test for any change that depends on Jellyfin behavior
- **Before `/gsd-verify-work`:** `mise run lint`, `mise run test`, and `mise run e2e` all green
- **Max feedback latency:** 20 seconds for the per-task sample

---

## Per-Task Verification Map

Task IDs are assigned when the planner writes PLAN.md. Each row below binds a phase requirement to the command that proves it; `/gsd-validate-phase` fills the Task ID and Plan columns after planning.

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| pending | pending | 0 | PUB-02 | T-06-01 | Every manifest value is written through `jq --arg`/`--argjson`, never interpolated into a JSON literal | script | `bats tests/scripts/manifest.bats` | ❌ W0 | ⬜ pending |
| pending | pending | 0 | REL-04 | — | N/A | script | `bats tests/scripts/package.bats` | ✅ | ⬜ pending |
| pending | pending | 0 | PUB-04 | T-06-02 | The installed zip's MD5 matches the manifest checksum, or the install is refused | e2e | `bats e2e/07-catalog-install.bats` | ❌ W0 | ⬜ pending |
| pending | pending | 1 | PUB-02 | T-06-03 | `pages.yml` carries a least-privilege `permissions:` block and no `contents: write` | script | `actionlint && zizmor --offline --persona=pedantic .github/workflows` | ✅ | ⬜ pending |
| pending | pending | 1 | DOCS-02 | — | N/A | manual-only | — | n/a | ⬜ pending |
| pending | pending | 1 | DOCS-04 | — | N/A | manual-only | — | n/a | ⬜ pending |
| pending | pending | 2 | PUB-05 | — | N/A | manual-only | — | n/a | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/scripts/manifest.bats` — stubs for PUB-02, with a fake `gh` on `PATH` per the D-04 seam
- [ ] `e2e/07-catalog-install.bats` — stubs for PUB-04, plus its `e2e/compose.yaml` additions (a manifest-serving nginx, a clean Jellyfin service)
- [ ] `tests/scripts/package.bats` — new `@test` cases for REL-04's changelog wiring, D-09's refusal on an undated version, and D-17's `PACKAGE_VERSION` override
- [ ] Framework install: none — bats, jq, gh, openssl, nginx, and Docker are all already available

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| README states the manifest URL and the catalog install and update steps | DOCS-02 | Prose accuracy is human-reviewed; DOCS-01/03/05 shipped the same way in earlier phases | Read `README.md`. Confirm it gives the `https://<owner>.github.io/<repo>/manifest.json` URL and the `Dashboard > Plugins > Repositories > Add Repository` steps for install and update |
| The `CLAUDE.md` version-bump rule names every pin | DOCS-04 | Prose; same justification as DOCS-02 | Read the bump rule in `CLAUDE.md`. Confirm it names `Jellyfin.Controller` and `Jellyfin.Model`, `Microsoft.Data.Sqlite.Core`, the `jellyfin/jellyfin` image tag, the target framework, the test project's `Jellyfin.Controller` reference, and the `targetAbi` values in `tests/scripts/package.bats` |
| An administrator adds the manifest URL and installs 0.9.0.0 from the catalog on a clean Jellyfin 12.1 server | PUB-04 | Needs the live public manifest URL and a real server; the automated e2e proves the same flow against a hermetic local host | After the `v0.9.0.0` release and the Pages deploy, add the manifest URL on a clean server and install the plugin. Observe whether the dashboard renders the `changelog` field as Markdown or plain text (Open Question 2) |
| The same server receives the update to 1.0.0.0 from the catalog | PUB-04 | Same reason; the update check needs two catalog versions | After the `v1.0.0.0` release and its Pages deploy, confirm the manifest lists both versions and the server offers the update |
| Version 1.0.0.0 is tagged and published through the release workflow | PUB-05 | Closed by the maintainer's own tag push (D-13); no execution run can perform it | Push `v1.0.0.0`. Confirm with `gh release list` that the release exists and `pages.yml` fired |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 20s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
