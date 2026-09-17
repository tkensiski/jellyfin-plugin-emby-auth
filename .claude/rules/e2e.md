---
paths:
  - "e2e/**"
---

# End-to-end tests

- The tests in `e2e/emby-auth.bats` run in file order and share one set of containers. `setup_file` creates all users. A test can depend on the state that earlier tests leave. Make each test set up the state it asserts on, where practical.
- Jellyfin reaches Emby through `emby-proxy` (nginx, `emby-proxy.conf`), which logs each request body to stdout. Use `emby_login_requests NAME` to prove whether a login reached Emby. The helper sends a marker request and waits for it, so the count includes all earlier requests.
- Do not use the Emby activity log for a "did not reach Emby" check. Emby writes entries after a delay, so the check passes before the entry exists.
- Jellyfin runs at Debug level (`JELLYFIN_Serilog__MinimumLevel__Default`), so the log test also sees the exceptions that Jellyfin logs for refused logins. Add every new test password to the list in the last test.
- Jellyfin 12.1 starts a setup server first. Wait for `/health` to return `Healthy`, not for `/System/Info/Public`. Emby has no `/health`.
- Emby and Jellyfin use the same API style. The helpers send `Authorization: MediaBrowser Client=..., Token=...` to both servers.
- Quick Connect: `POST /QuickConnect/Initiate` (anonymous), `POST /QuickConnect/Authorize?code=&userId=` (admin token), then `POST /Users/AuthenticateWithQuickConnect` with the secret.
- Scripts must pass `shellcheck -x` and `shfmt -d`. Inside a test, use `if [[ ... ]]; then ...; return 1; fi` instead of a bare `[[ ... ]]` in a loop.
