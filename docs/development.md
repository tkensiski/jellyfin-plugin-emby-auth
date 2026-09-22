# Development

The tools are pinned in `.mise.toml`. Run `mise install` first.

| Task | Command |
|---|---|
| Check formatting, lint scripts and workflows, and scan for secrets | `mise run lint` |
| Build with warnings as errors, then run the unit tests, the script tests, and the settings-page tests | `mise run test` |
| Run end-to-end tests in Docker | `mise run e2e` |
| Build the release zip and manifest | `mise run package` |
| Rebuild the plugin repository manifest from every GitHub release | `mise run manifest` |
| Run pre-commit checks | `prek run` |
| Run a CI job in a local container | `act pull_request -j lint` (or `-j test`) |
| Run the CI end-to-end job in a local container | `act pull_request -j e2e --bind --container-options "--network host"` |
| Start a demo in Docker to log in and look around | `scripts/dev-env.sh up` |
| Show or remove the demo | `scripts/dev-env.sh status` or `scripts/dev-env.sh down` |
| Audit the repository before making it public | `GH_REPO=owner/repo scripts/pre-public-audit.sh run` |

CI runs `mise run lint`, `mise run test`, and `mise run e2e` on every pull request, so a local run of these three tasks matches CI, with one exception: `mise run lint`'s zizmor check runs `--offline`, so it skips every audit that needs the GitHub API — there is no `GH_TOKEN` on a laptop by default. The `lint` job also runs `mise run lint-workflows-online`, which repeats the same zizmor scan without `--offline`, using the job's own `GH_TOKEN`. A green `mise run lint` locally does not cover those online audits; only CI does.

## Pre-publication audit

`scripts/pre-public-audit.sh run` reports the evidence a human needs before making the repository public. It needs `GH_REPO` (`owner/repo`) and a `GH_TOKEN` that `gh` can use. Only the lint line (`mise run lint`, which includes the gitleaks history scan) is machine-checked pass/fail, and it prints `mise run lint`'s own output when it fails; the visibility, workflow-run, artifact, issue, pull-request, and release lines are for a human to read before deciding — the script cannot judge whether their contents are safe to publish.

## Settings-page tests

The settings-page tests live in `tests/js/`. They run on Node's built-in test runner (`node --test`) with jsdom against the real `configPage.html`, so they exercise the shipping file, not a copy. `mise run test` installs the test dependency from the committed lockfile (`npm --prefix tests/js ci`) on every run, so a local run matches CI. To run only this suite, use `node --test` from the repository root; it takes no path argument.

## End-to-end tests

The end-to-end tests start Emby, an nginx proxy that logs the requests to Emby, and Jellyfin. Emby and Jellyfin listen on `127.0.0.1:18096` and `127.0.0.1:28096`; set `EMBY_PORT` and `JELLYFIN_PORT` to use other ports. The tests remove the containers after the run. Set `KEEP_E2E=1` to keep them.

## Demo

The demo uses the same containers, on `127.0.0.1:18196` (Emby) and `127.0.0.1:28196` (Jellyfin), so it can run while the end-to-end tests run. `up` builds the plugin, starts new containers, configures the plugin, and creates demo users with different Emby settings. At the end, `up` shows the URLs, the logins, and what each demo user shows. `down` removes the containers and their data.

## Releases

1. Rename `CHANGELOG.md`'s `## Unreleased` heading to `## [<version>] - <YYYY-MM-DD>`, add a fresh empty `## Unreleased` above it, set `<Version>`, `<AssemblyVersion>`, and `<FileVersion>` in `Directory.Build.props`, and merge both changes in the same commit. `scripts/package.sh build` refuses a version with no dated `CHANGELOG.md` section, and `mise run test` runs `tests/scripts/package.bats`, which calls that build, so a version bump merged without its dated section fails the suite on `main`.
2. Tag the merge commit `v<version>`, for example `v1.0.0.0`, and push the tag.
3. `.github/workflows/release.yml` checks that the tag matches the version, then requires the tagged commit to carry a `ci-success` check run that completed with conclusion `success`, then runs `mise run test` and `mise run package`, and creates a GitHub release with the zip and `manifest.json`.

The workflow stops before creating any release when the tagged commit has no `ci-success` check run, when the check run it finds is not actually named `ci-success` (the GitHub API ignores an unrecognised or mistyped query filter rather than erroring on it), when the run is still in progress, when it finished with any conclusion other than `success` — including `cancelled`, which happens when a later push to `main` cancelled the earlier run — when the commit carries more than one `ci-success` check run (the gate expects exactly one and refuses rather than guess; re-run the workflow so the newest run is the one the API returns, or move the release to a fresh commit), or when the commit is not identical to or behind the repository's default branch. That last check exists because a passing `ci-success` check run also appears on the head commit of an open pull request, since CI runs on the `pull_request` event too; a passing check run there does not mean the commit was ever merged. To fix it, wait for CI on that commit to finish green on the default branch, then delete and push the tag again.

Some commits on `main` never get a `ci-success` run at all: GitHub only schedules a push-triggered workflow for the head of a push, so an intermediate commit in a multi-commit push gets no check suite. For those, re-pushing the tag does not help — `ci.yml` never runs against a tag push, only `release.yml` does. Dispatch CI on the tagged commit manually instead: on GitHub, open the CI workflow's Actions page, choose **Run workflow**, and pick the tagged commit's ref (or run `gh workflow run ci.yml --ref <tag-or-sha>`); once it finishes green, delete and push the tag again. There is no waiting period and no override inside the workflow.

After the release, `.github/workflows/pages.yml` runs once the Release workflow completes successfully, rebuilds one `manifest.json` listing every released version from the GitHub Releases API, re-checks each release's zip against the checksum that release recorded, publishes the result to GitHub Pages at `https://tkensiski.github.io/jellyfin-plugin-emby-auth/manifest.json`, and then fetches that published document and re-checks it again. Its trigger definition is read from the version of the workflow file on the default branch, so it only takes effect once merged to `main`, and its very first eligible completion after landing has been reported not to fire — if no Pages run appears after a release, dispatch it manually from the Actions page (choose **Run workflow** on the Pages workflow) or with `gh workflow run pages.yml`, and list its runs with `gh run list --workflow=pages.yml`.

GitHub Pages must already exist for this repository with its build type set to GitHub Actions before any `pages.yml` deploy can succeed. A repository administrator sets this once, either in **Settings > Pages** or with an authenticated `gh api` call; no workflow can set it, because the endpoint that changes it sits under the Administration repository permission, which a workflow's own token can never be granted.
