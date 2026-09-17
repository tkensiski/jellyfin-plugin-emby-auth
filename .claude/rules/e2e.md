---
paths:
  - "e2e/**"
---

# End-to-end tests

## Layout

- `setup_suite.bash` starts Emby, `emby-proxy`, and Jellyfin once for all files, configures the plugin, and creates every Emby user. `teardown_suite` removes the containers unless `KEEP_E2E=1`.
- Each `NN-topic.bats` file creates its own Jellyfin accounts in `setup_file` and resets the plugin settings with `reset_plugin_config`. A file must not depend on another file. Run one file with `bats e2e/NN-topic.bats`.
- A file that stops a service starts it again in `teardown_file` (see `40-emby-outage.bats`).
- `90-jellyfin-log.bats` runs last and checks the whole Jellyfin log.

## Conventions

- Create a new Emby user in `setup_suite.bash`, never in a test file. The plugin caches the Emby user list for 60 seconds, so a user created later is not visible at once.
- Give each test file its own user names.
- Name every test password `NAME-emby-pass[-N]`, `NAME-jf-pass`, or `NAME-jf-random`. The log check finds leaked passwords by this pattern.
- Use `emby_login_requests NAME` to prove whether a login reached Emby. The helper reads the `emby-proxy` log. It first sends a marker request and waits for it, so the count includes all earlier requests.
- Do not use the Emby activity log for a "did not reach Emby" check. Emby writes entries after a delay, so the check passes before the entry exists.
- Jellyfin runs at Debug level (`JELLYFIN_Serilog__MinimumLevel__Default` in `compose.yaml`), so the log check also sees the exceptions that Jellyfin logs for refused logins.
- Scripts must pass `shellcheck -x` and `shfmt -d`. Inside a test, use `if [[ ... ]]; then ...; return 1; fi` instead of a bare `[[ ... ]]` in a loop.

## Server facts

- Jellyfin 12.1 starts a setup server first. Wait for `/health` to return `Healthy`, not for `/System/Info/Public`. Emby has no `/health`.
- Emby and Jellyfin use the same API style. The helpers send `Authorization: MediaBrowser Client=..., Token=...` to both servers.
- Quick Connect: `POST /QuickConnect/Initiate` (anonymous), `POST /QuickConnect/Authorize?code=&userId=` (admin token), then `POST /Users/AuthenticateWithQuickConnect` with the secret.
- The migration task has the key `EmbyAuthMoveUsersToDefault`. Start it with `POST /ScheduledTasks/Running/{id}` (`run_migration_task`).
