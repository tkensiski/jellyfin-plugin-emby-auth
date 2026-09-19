# Development

The tools are pinned in `.mise.toml`. Run `mise install` first.

| Task | Command |
|---|---|
| Check formatting, and lint scripts and workflows | `mise run lint` |
| Build with warnings as errors, then run the unit tests, the script tests, and the settings-page tests | `mise run test` |
| Run end-to-end tests in Docker | `mise run e2e` |
| Build the release zip and manifest | `mise run package` |
| Run pre-commit checks | `prek run` |
| Run a CI job in a local container | `act pull_request -j lint` (or `-j test`) |
| Run the CI end-to-end job in a local container | `act pull_request -j e2e --bind --container-options "--network host"` |
| Start a demo in Docker to log in and look around | `scripts/dev-env.sh up` |
| Show or remove the demo | `scripts/dev-env.sh status` or `scripts/dev-env.sh down` |

CI runs `mise run lint`, `mise run test`, and `mise run e2e` on every pull request, so a local run of these three tasks matches CI.

## Settings-page tests

The settings-page tests live in `tests/js/`. They run on Node's built-in test runner (`node --test`) with jsdom against the real `configPage.html`, so they exercise the shipping file, not a copy. `mise run test` installs the test dependency from the committed lockfile (`npm --prefix tests/js ci`) on every run, so a local run matches CI. To run only this suite, use `node --test` from the repository root; it takes no path argument.

## End-to-end tests

The end-to-end tests start Emby, an nginx proxy that logs the requests to Emby, and Jellyfin. Emby and Jellyfin listen on `127.0.0.1:18096` and `127.0.0.1:28096`; set `EMBY_PORT` and `JELLYFIN_PORT` to use other ports. The tests remove the containers after the run. Set `KEEP_E2E=1` to keep them.

## Demo

The demo uses the same containers, on `127.0.0.1:18196` (Emby) and `127.0.0.1:28196` (Jellyfin), so it can run while the end-to-end tests run. `up` builds the plugin, starts new containers, configures the plugin, and creates demo users with different Emby settings. At the end, `up` shows the URLs, the logins, and what each demo user shows. `down` removes the containers and their data.

## Releases

1. Set `<Version>`, `<AssemblyVersion>`, and `<FileVersion>` in `Directory.Build.props`, and merge the change.
2. Tag the merge commit `v<version>`, for example `v1.0.0.0`, and push the tag.
3. `.github/workflows/release.yml` checks that the tag matches the version, runs `mise run test` and `mise run package`, and creates a GitHub release with the zip and `manifest.json`.
