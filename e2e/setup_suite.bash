# shellcheck shell=bash
# Starts Emby, the logging proxy, and Jellyfin once for all e2e test files, and creates every Emby user.
#
# All Emby users exist before the first login, because the plugin caches the Emby user list for 60 seconds.
# Test passwords follow the pattern NAME-emby-pass[-N], NAME-jf-pass, or NAME-jf-random, so that
# 90-jellyfin-log.bats finds them in the log without a list.

setup_suite() {
	# shellcheck source=e2e/helpers.bash
	source "$(dirname "${BASH_SOURCE[0]}")/helpers.bash"

	# UseSharedCompilation=false stops Roslyn starting VBCSCompiler for this publish. A real compile
	# otherwise leaves that server running for a ten-minute keep-alive, and it inherits the output
	# descriptors bats opens for setup_suite. bats then never sees end of file on its own output
	# stream, so the suite blocks until the server exits: measured at 603 seconds for 74 seconds of
	# test work. This is the failure class of bats-core#419, which 60-concurrent-logins.bats guards
	# against by closing fd 3; closing fd 3 is not enough here, because bats opens fd 4 on the same
	# stream and the server holds that instead. Removing the server covers every descriptor.
	UseSharedCompilation=false dotnet publish "$E2E_DIR/../src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj" -c Release -o "$E2E_DIR/../artifacts/plugin" >&3
	"$E2E_DIR/../scripts/fetch-jellyfinsecurity.sh" fetch >&3
	docker compose -f "$COMPOSE_FILE" down --volumes >&3 2>&1
	docker compose -f "$COMPOSE_FILE" up -d >&3 2>&1
	wait_until Emby emby_ready
	wait_until Jellyfin jellyfin_ready

	complete_startup_wizard "$EMBY" embyadmin emby-admin-pass
	complete_startup_wizard "$JELLYFIN" jfadmin jf-admin-pass
	EMBY_TOKEN="$(login_token "$EMBY" embyadmin emby-admin-pass)"
	JF_TOKEN="$(login_token "$JELLYFIN" jfadmin jf-admin-pass)"
	EMBY_API_KEY="$(create_emby_api_key "$EMBY_TOKEN" jellyfin-emby-auth)"
	export EMBY_TOKEN JF_TOKEN EMBY_API_KEY

	api POST "$JELLYFIN/Plugins/$PLUGIN_ID/Configuration" "$JF_TOKEN" \
		"$(jq -cn --arg u "$EMBY_INTERNAL_URL" --arg k "$EMBY_API_KEY" '{EmbyServerUrl: $u, EmbyApiKey: $k}')" >/dev/null

	local name
	for name in alice bella carol chris dana dave elton erin gina henry ivy jack kate leo mia nora oscar paul quinn rex sam tina uma vic wes yara zack; do
		set_password "$EMBY" "$EMBY_TOKEN" "$(create_user "$EMBY" "$EMBY_TOKEN" "$name")" "$name-emby-pass"
	done
	create_user "$EMBY" "$EMBY_TOKEN" frank >/dev/null
	update_policy "$EMBY" "$EMBY_TOKEN" "$(emby_user_id gina)" '.EnableRemoteAccess = false'
	update_policy "$EMBY" "$EMBY_TOKEN" "$(emby_user_id quinn)" '.EnableRemoteAccess = false'
	update_policy "$EMBY" "$EMBY_TOKEN" "$(emby_user_id ivy)" '.IsDisabled = true'
}

teardown_suite() {
	# shellcheck source=e2e/helpers.bash
	source "$(dirname "${BASH_SOURCE[0]}")/helpers.bash"
	if [[ "${KEEP_E2E:-}" != "1" ]]; then
		docker compose -f "$COMPOSE_FILE" down --volumes >&3 2>&1
	fi
}
