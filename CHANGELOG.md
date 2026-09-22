# Changelog

All notable changes to this plugin are documented in this file.

## Unreleased

## [1.0.0.0] - 2026-09-22

The first stable release. The plugin itself is unchanged from `0.9.0.0`.

The catalog entry's Compatibility link was relative, so Jellyfin resolved it against the administrator's own server rather than this repository. It is now an absolute URL.

See [Compatibility](https://github.com/tkensiski/jellyfin-plugin-emby-auth#compatibility) for the tested Jellyfin and Emby versions and the minimum Jellyfin version this build asks for.

## [0.9.0.0] - 2026-09-22

A Jellyfin plugin that moves users from Emby to Jellyfin without a password reset. A user logs in to Jellyfin with their Emby user name and password; the plugin checks the password against the Emby server and saves it in Jellyfin, then moves the user to a login method that checks the saved password without Emby.

An administrator gets:

- A settings page (Dashboard > Plugins > Emby Auth) for the Emby server URL, the Emby API key, the migration target, and the password-set target.
- A migration list showing each user's readiness to move.
- A migration task that moves every ready user at once.

See [Compatibility](README.md#compatibility) for the tested Jellyfin and Emby versions and the minimum Jellyfin version this build asks for.
