# Jellyfin Emby Auth

[![CI](https://github.com/tkensiski/jellyfin-plugin-emby-auth/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/tkensiski/jellyfin-plugin-emby-auth/actions/workflows/ci.yml)
[![License: GPL-3.0](https://img.shields.io/badge/license-GPL--3.0-blue)](LICENSE)

A Jellyfin plugin that moves users from Emby to Jellyfin without a password reset, so that you can shut down Emby.

Your users log in to Jellyfin with the user name and password that they use on Emby. The plugin checks each password against your Emby server and saves it in Jellyfin. Then each user moves to the login method you choose — Jellyfin's **Default** login method out of the box — and that login method checks the password without Emby. When every user has moved off the Emby login method, you shut down Emby and remove the plugin.

**Status:** tested only in local containers, with the Jellyfin `12.1.20260915-010956` and Emby `4.10.0.40` images. Not tested on a production server. See [Compatibility](#compatibility).

## Requirements

- Jellyfin 12.1. The plugin is built against the Jellyfin 12.1.0 packages.
- An Emby server that Jellyfin can reach over HTTP or HTTPS.
- An API key for the plugin on that Emby server.

## Compatibility

This plugin was tested only in local containers, against the two server images pinned in `e2e/compose.yaml`: Jellyfin `12.1.20260915-010956` and Emby `4.10.0.40`. No production server has run it. The Jellyfin image tag is a dated 12.1 build, not a release numbered `12.1.0` — `12.1.0` is the version of the Jellyfin packages the plugin compiles against, which is a separate thing from the server it was run against.

The release zip's `meta.json`, and the plugin repository `manifest.json`, both carry a `targetAbi` value. For this build it is `12.1.0.0`, those same compiled-against packages with a fourth part added. This value declares the minimum Jellyfin version the plugin asks for: Jellyfin decides which catalog entries to offer a server by parsing each entry's `targetAbi` and keeping the ones less than or equal to the server's own version. A server older than `12.1.0.0` is never offered the plugin. A server newer than it is offered the plugin, and Jellyfin will install it.

There is no upper bound in that value. A Jellyfin release newer than the tested image will install this plugin even though nobody has tested it there. Installing is not the same as supported — treat any Jellyfin build other than the one named above as untested.

## Install

### Add the plugin repository

1. In Jellyfin, open **Dashboard > Plugins > Repositories > Add Repository**.
2. Give the repository a name of your choosing, and set its URL to `https://tkensiski.github.io/jellyfin-plugin-emby-auth/manifest.json`, exactly. Select **Save**.
3. Open the **Catalog** tab, find **Emby Auth**, and install it.
4. Restart Jellyfin.

Continue with [Configure](#configure).

Jellyfin downloads a repository manifest and its zips without GitHub credentials, so this URL works because the release files it names are files anyone can download without signing in to GitHub. See [Compatibility](#compatibility) for the Jellyfin and Emby versions this plugin was tested against.

### Update

Once the repository is added, Jellyfin's own **Plugins** page offers each newer version the manifest lists. Installing the offered version and restarting Jellyfin is the whole update. The repository is added once, at install; taking an update repeats none of those steps. Adding it a second time leaves two repository entries that point at the same manifest — remove one to fix it.

### Download the zip

Use this route only if your Jellyfin server cannot reach GitHub Pages.

1. Get the zip `jellyfin-plugin-emby-auth_<version>.zip`:
   - From a GitHub release of this repository.
   - Or build it. The tools are pinned in `.mise.toml`:

     ```sh
     mise install
     mise run package
     ```

     The zip is in `artifacts/release/`.
2. Unzip it into `<jellyfin config>/plugins/EmbyAuth_<version>/`. The zip holds the plugin DLL and `meta.json`.
3. Restart Jellyfin.

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
