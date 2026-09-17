# Settings

Open **Dashboard > Plugins > Emby Auth** in Jellyfin.

| Setting | Value |
|---|---|
| Emby server URL | The base URL of Emby as Jellyfin reaches it, for example `http://emby:8096`. Use `https`, or an address on a private network. The URL must not contain a user name or password. |
| Emby API key | An API key that you create for the plugin on the Emby server. |
| Migration behavior | When users move to the Default login method. See [Migration behavior](#migration-behavior). Default: **Move each user to Jellyfin after the first login**. |
| Account access | The access of accounts that the plugin creates. See [Account access](#account-access). Default: **Jellyfin defaults, with Emby's remote access setting**. |

If a setting is missing or not valid, the plugin refuses each login that it handles and writes the cause to the Jellyfin log at Error level.

## Set the settings through the API

Send `POST /Plugins/e973e09a-e8b4-40c1-9be2-8e51342de1f9/Configuration` with an administrator token and a body like this:

```json
{
  "EmbyServerUrl": "http://emby:8096",
  "EmbyApiKey": "<key>",
  "MigrationMode": "MoveAfterFirstLogin",
  "AccountAccess": "CopyEmbyRemoteAccess"
}
```

## Migration behavior

| Choice | API value | Who checks the password | When the user moves to Default |
|---|---|---|---|
| Move each user to Jellyfin after the first login | `MoveAfterFirstLogin` | Emby, once | Right after the first login that Emby accepts |
| Keep Emby in charge until the migration task runs | `KeepEmbyInCharge` | Emby, on every login. A password change on Emby applies to the next login. | When you [run the migration](migration.md#run-the-migration) |

## Account access

| Choice | API value | New accounts | Existing accounts on the Emby login method |
|---|---|---|---|
| Jellyfin defaults, with Emby's remote access setting | `CopyEmbyRemoteAccess` | Jellyfin's default permissions. Remote access only if Emby allows it. | Lose remote access when Emby does not allow it. |
| No libraries until an administrator grants them, with Emby's remote access setting | `NoLibraries` | Jellyfin's default permissions, but no library access. Remote access only if Emby allows it. | Lose remote access when Emby does not allow it. |
| Jellyfin defaults only | `JellyfinDefaults` | Jellyfin's default permissions, including remote access. | No change. |

The plugin never gives administrator rights, and never turns remote access on for an existing account.

With **Jellyfin defaults only**, a user that Emby limits to the local network can connect to Jellyfin from the internet. Emby checks remote access against the address of the caller, which is the Jellyfin server, so Emby does not block these logins.
