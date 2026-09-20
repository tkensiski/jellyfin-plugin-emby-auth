# Changelog

All notable changes to this plugin are documented in this file.

## Unreleased

### Added

- Two settings, **Migration target** and **Password-set target**. Each picks the login method a move sends a user to, or the value that means no path moves a user off the Emby login method at all.

### Removed

- The "Check the saved Jellyfin password first, then Emby" migration behavior. On the Emby login method, Emby now checks every login; the saved Jellyfin password hash never accepts a login on its own.

### Changed

- A failure while creating or saving an account now refuses the login instead of returning a server error. A failed cleanup delete of a half-made account is logged with the account name.
- The migration task's name changes to "Finish the Emby migration", and its scheduled-task key changes from `EmbyAuthMoveUsersToDefault` to `EmbyAuthMigration`. A server whose own scripts, or a stored trigger, reference the old key must be updated to the new one.
- `GET /EmbyAuth/Migration` returns a different response. A `RecordsUnavailable` flag reports a failed read of the record of passwords Emby verified. Each user reports a `State` of `Ready`, `NeedsEmbyLogin`, `NoPassword`, or `Unknown` in place of a single ready/not-ready flag. The response also carries the migration task's own state, and the list of login methods available as a migration target.

### Upgrade note

A server whose stored plugin settings still hold the removed migration-behavior value cannot load those settings. Jellyfin replaces the settings file with a default one, so the server loses its Emby server URL and its Emby API key and refuses every login on the Emby login method until an administrator enters them again in Dashboard > Plugins > Emby Auth. This affects only servers that had selected that behavior.
