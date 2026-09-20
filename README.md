# Jellyfin Emby Auth

[![CI](https://github.com/tkensiski/jellyfin-plugin-emby-auth/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/tkensiski/jellyfin-plugin-emby-auth/actions/workflows/ci.yml)
[![License: GPL-3.0](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE)

A Jellyfin plugin that moves users from Emby to Jellyfin without a password reset, so that you can shut down Emby.

Your users log in to Jellyfin with the user name and password that they use on Emby. The plugin checks each password against your Emby server and saves it in Jellyfin. Then each user moves to the login method you choose — Jellyfin's **Default** login method out of the box — and that login method checks the password without Emby. When every user has moved off the Emby login method, you shut down Emby and remove the plugin.

**Status:** tested only in local containers, with Jellyfin 12.1.0 and Emby 4.10.0.40. Not tested on a production server. Not published to a plugin repository.

## Requirements

- Jellyfin 12.1. The plugin is built against the Jellyfin 12.1.0 packages.
- An Emby server that Jellyfin can reach over HTTP or HTTPS.
- An API key for the plugin on that Emby server.

## Install

1. Get the zip `jellyfin-plugin-emby-auth_<version>.zip`:
   - From a GitHub release of this repository. The repository is private, so you need access to it.
   - Or build it. The tools are pinned in `.mise.toml`:

     ```sh
     mise install
     mise run package
     ```

     The zip is in `artifacts/release/`.
2. Unzip it into `<jellyfin config>/plugins/EmbyAuth_<version>/`. The zip holds the plugin DLL and `meta.json`.
3. Restart Jellyfin.

Each release also has `manifest.json`, a Jellyfin plugin repository manifest with the download URL and MD5 checksum of the zip. Jellyfin downloads a repository manifest and the zip without GitHub credentials, so the manifest works as a repository URL only when the release files are public.

## Configure

1. Create an API key for the plugin on the Emby server.
2. In Jellyfin, open **Dashboard > Plugins > Emby Auth**.
3. Enter the **Emby server URL**, for example `http://emby:8096`, and the **Emby API key**.
4. Choose the **Migration behavior**, the **Password-set target**, and the **Account access**, or keep the defaults. Select **Save**.

[Settings](docs/settings.md) explains each choice and how to set the settings through the API.

## Migrate users

1. Ask your users to log in to Jellyfin with their Emby user name and password. The first login creates the Jellyfin account if it does not exist, and saves the password. With the default migration behavior, the user also moves to the migration target at that login.
2. In the **Migration** section of the settings page, pick the **Migration target** — Jellyfin's Default login method by default, or another login method Jellyfin reports as enabled — and select **Run migration now**. This moves each user marked ready.
3. When the list shows no users, shut down Emby and remove the plugin.

[Migration](docs/migration.md) has the full procedure, including users who cannot log in before the shutdown, and accounts that must exist before the first login.

## Documentation

| Document | Contents |
|---|---|
| [How it works](docs/how-it-works.md) | The login steps, password changes, security notes, and limits |
| [Settings](docs/settings.md) | Each setting and choice, and the settings API |
| [Migration](docs/migration.md) | The migration procedure, accounts that must exist first, and the migration API |
| [Development](docs/development.md) | Build, tests, CI, the local demo, and releases |

## License

GPL-3.0. See [LICENSE](LICENSE).

## Contact

To report a problem or ask a question, [open an issue](https://github.com/tkensiski/jellyfin-plugin-emby-auth/issues). Maintainer: [@tkensiski](https://github.com/tkensiski).
