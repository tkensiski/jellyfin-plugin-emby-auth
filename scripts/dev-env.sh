#!/usr/bin/env bash
set -euo pipefail

# A local demo environment: Emby, the logging proxy, and Jellyfin with the plugin, in containers on 127.0.0.1.
# It uses e2e/compose.yaml with its own project name and ports, so it can run next to the end-to-end tests.
#
# Usage:
#   scripts/dev-env.sh up      Build the plugin, start fresh containers, configure both servers, and create demo users.
#   scripts/dev-env.sh status  Show the containers.
#   scripts/dev-env.sh down    Remove the containers and their data.
#
# EMBY_PORT and JELLYFIN_PORT (default 18196 and 28196) set the host ports.
# The demo passwords below are for these local containers only.

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly REPO_ROOT
export COMPOSE_PROJECT_NAME=emby-auth-dev
export EMBY_PORT="${EMBY_PORT:-18196}"
export JELLYFIN_PORT="${JELLYFIN_PORT:-28196}"

# shellcheck source=e2e/helpers.bash
source "$REPO_ROOT/e2e/helpers.bash"

usage() {
	echo "Usage: $0 up | status | down" >&2
}

up() {
	dotnet publish "$REPO_ROOT/src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj" -c Release -o "$REPO_ROOT/artifacts/plugin"
	"$REPO_ROOT/scripts/fetch-jellyfinsecurity.sh" fetch
	docker compose -f "$COMPOSE_FILE" down --volumes
	docker compose -f "$COMPOSE_FILE" up -d
	echo "Waiting for Emby and Jellyfin to start..."
	wait_until Emby emby_ready
	wait_until Jellyfin jellyfin_ready

	complete_startup_wizard "$EMBY" embyadmin embyadmin-demo-pass
	complete_startup_wizard "$JELLYFIN" jfadmin jfadmin-demo-pass
	EMBY_TOKEN="$(login_token "$EMBY" embyadmin embyadmin-demo-pass)"
	JF_TOKEN="$(login_token "$JELLYFIN" jfadmin jfadmin-demo-pass)"
	export EMBY_TOKEN JF_TOKEN
	local api_key
	api_key="$(create_emby_api_key "$EMBY_TOKEN" jellyfin-emby-auth)"
	api POST "$JELLYFIN/Plugins/$PLUGIN_ID/Configuration" "$JF_TOKEN" \
		"$(jq -cn --arg k "$api_key" '{EmbyServerUrl: "http://emby-proxy:8096", EmbyApiKey: $k}')" >/dev/null

	local name
	for name in alice bob carol dave erin; do
		set_password "$EMBY" "$EMBY_TOKEN" "$(create_user "$EMBY" "$EMBY_TOKEN" "$name")" "$name-demo-pass"
	done
	update_policy "$EMBY" "$EMBY_TOKEN" "$(emby_user_id carol)" '.EnableRemoteAccess = false'
	update_policy "$EMBY" "$EMBY_TOKEN" "$(emby_user_id erin)" '.IsDisabled = true'
	precreate_on_emby_method dave

	cat <<EOF

The demo environment runs.

  Emby      $EMBY      admin: embyadmin / embyadmin-demo-pass
  Jellyfin  $JELLYFIN      admin: jfadmin / jfadmin-demo-pass

Emby users (password: <name>-demo-pass):
  alice  No Jellyfin account. The first Jellyfin login creates one.
  bob    No Jellyfin account. Use bob to try another migration behavior.
  carol  Emby does not allow remote access. The new Jellyfin account copies that.
  dave   Has a Jellyfin account on the Emby login method, with a random Jellyfin password.
  erin   Disabled on Emby. The plugin does not send the password to Emby.

Plugin settings: Jellyfin > Dashboard > Plugins > Emby Auth
Migration:       the Migration section at the bottom of the plugin settings page
Plugin logs:     docker compose -p $COMPOSE_PROJECT_NAME -f e2e/compose.yaml logs -f jellyfin
Remove it all:   scripts/dev-env.sh down
EOF
}

main() {
	case "${1:-}" in
	up)
		up
		;;
	status)
		docker compose -f "$COMPOSE_FILE" ps
		;;
	down)
		docker compose -f "$COMPOSE_FILE" down --volumes
		;;
	*)
		usage
		exit 2
		;;
	esac
}

main "$@"
