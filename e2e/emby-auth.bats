#!/usr/bin/env bats
# End-to-end tests against real Emby and Jellyfin containers.
# The tests run in file order and share one pair of servers.

setup_file() {
	load helpers
	local repo="${BATS_TEST_DIRNAME}/.."

	dotnet publish "$repo/src/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.csproj" -c Release -o "$repo/artifacts/plugin" >&3
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
		"$(jq -cn --arg k "$EMBY_API_KEY" '{EmbyServerUrl: "http://emby-proxy:8096", EmbyApiKey: $k}')" >/dev/null

	ALICE_EMBY_ID="$(create_user "$EMBY" "$EMBY_TOKEN" alice)"
	export ALICE_EMBY_ID
	set_password "$EMBY" "$EMBY_TOKEN" "$ALICE_EMBY_ID" alice-pass-1

	local name
	for name in carol dave erin gina henry ivy jack kate; do
		set_password "$EMBY" "$EMBY_TOKEN" "$(create_user "$EMBY" "$EMBY_TOKEN" "$name")" "$name-emby-pass"
	done
	create_user "$EMBY" "$EMBY_TOKEN" frank >/dev/null
	update_policy "$EMBY" "$EMBY_TOKEN" "$(emby_user_id gina)" '.EnableRemoteAccess = false'
	update_policy "$EMBY" "$EMBY_TOKEN" "$(emby_user_id ivy)" '.IsDisabled = true'

	set_password "$JELLYFIN" "$JF_TOKEN" "$(create_user "$JELLYFIN" "$JF_TOKEN" carol)" carol-jf-pass

	# Accounts created before the first login get a random password, then the Emby login method.
	for name in dave erin henry kate; do
		local id
		id="$(create_user "$JELLYFIN" "$JF_TOKEN" "$name")"
		set_password "$JELLYFIN" "$JF_TOKEN" "$id" "$name-jf-random"
		set_login_method "$JF_TOKEN" "$id" "$EMBY_PROVIDER"
	done

	local jack_id
	jack_id="$(create_user "$JELLYFIN" "$JF_TOKEN" jack)"
	set_password "$JELLYFIN" "$JF_TOKEN" "$jack_id" jack-jf-pass
	update_policy "$JELLYFIN" "$JF_TOKEN" "$jack_id" ".IsAdministrator = true | .AuthenticationProviderId = \"$EMBY_PROVIDER\""
}

teardown_file() {
	load helpers
	if [[ "${KEEP_E2E:-}" != "1" ]]; then
		docker compose -f "$COMPOSE_FILE" down --volumes >&3 2>&1
	fi
}

setup() {
	load helpers
}

emby_user_id() {
	api GET "$EMBY/Users" "$EMBY_TOKEN" | jq -r --arg n "$1" '.[] | select(.Name == $n) | .Id'
}

policy_field() {
	user_by_name "$JELLYFIN" "$JF_TOKEN" "$1" | jq -r ".Policy.$2"
}

@test "Jellyfin serves the plugin settings page" {
	run status GET "$JELLYFIN/web/ConfigurationPage?name=Emby%20Auth" "$JF_TOKEN"
	[ "$output" = "200" ]
}

@test "the first login of an Emby user creates a Jellyfin account on the Default login method" {
	run login_status "$JELLYFIN" alice alice-pass-1
	[ "$output" = "200" ]

	# Also proves that the proxy log records the plugin's login requests.
	[ "$(emby_login_requests alice)" = "1" ]
	[ "$(policy_field alice AuthenticationProviderId)" = "$DEFAULT_PROVIDER" ]
	[ "$(policy_field alice IsAdministrator)" = "false" ]
	[ "$(policy_field alice EnableRemoteAccess)" = "true" ]
}

@test "Jellyfin refuses a wrong password for a user on the Emby login method" {
	run login_status "$JELLYFIN" erin wrong-pass
	[ "$output" = "401" ]
	[ "$(policy_field erin AuthenticationProviderId)" = "$EMBY_PROVIDER" ]
}

@test "after the first login, Jellyfin checks the password without Emby" {
	set_password "$EMBY" "$EMBY_TOKEN" "$ALICE_EMBY_ID" alice-pass-2

	run login_status "$JELLYFIN" alice alice-pass-2
	[ "$output" = "401" ]
	run login_status "$JELLYFIN" alice alice-pass-1
	[ "$output" = "200" ]
}

@test "the plugin refuses a blank password and does not contact Emby" {
	run login_status "$JELLYFIN" frank ""
	[ "$output" = "401" ]
	[ "$(emby_login_requests frank)" = "0" ]
	[ -z "$(user_by_name "$JELLYFIN" "$JF_TOKEN" frank)" ]
}

@test "the plugin does not send a password for a name that is not an Emby user" {
	run login_status "$JELLYFIN" adimn jf-admin-pass
	[ "$output" = "401" ]
	[ "$(emby_login_requests adimn)" = "0" ]
}

@test "the plugin refuses a name with extra spaces and does not send the password" {
	run login_status "$JELLYFIN" "erin " erin-emby-pass
	[ "$output" = "401" ]
	[ "$(emby_login_requests "erin ")" = "0" ]
	[ "$(policy_field erin AuthenticationProviderId)" = "$EMBY_PROVIDER" ]
}

@test "the plugin does not send a password for a disabled Emby user" {
	run login_status "$JELLYFIN" ivy ivy-emby-pass
	[ "$output" = "401" ]
	[ "$(emby_login_requests ivy)" = "0" ]
}

@test "a Jellyfin account on the Default login method keeps its own password" {
	run login_status "$JELLYFIN" carol carol-emby-pass
	[ "$output" = "401" ]
	run login_status "$JELLYFIN" carol carol-jf-pass
	[ "$output" = "200" ]
}

@test "an account created before its first login needs the Emby password, then moves to Default" {
	run login_status "$JELLYFIN" dave ""
	[ "$output" = "401" ]
	run login_status "$JELLYFIN" dave dave-jf-random
	[ "$output" = "401" ]
	[ "$(policy_field dave AuthenticationProviderId)" = "$EMBY_PROVIDER" ]

	run login_status "$JELLYFIN" dave dave-emby-pass
	[ "$output" = "200" ]
	[ "$(policy_field dave AuthenticationProviderId)" = "$DEFAULT_PROVIDER" ]
	run login_status "$JELLYFIN" dave dave-jf-random
	[ "$output" = "401" ]
}

@test "a new account copies the Emby remote access restriction" {
	run login_status "$JELLYFIN" gina gina-emby-pass
	[ "$output" = "200" ]
	[ "$(policy_field gina EnableRemoteAccess)" = "false" ]
}

@test "the plugin refuses an administrator account on the Emby login method" {
	run login_status "$JELLYFIN" jack jack-emby-pass
	[ "$output" = "401" ]
	[ "$(policy_field jack IsAdministrator)" = "true" ]
	[ "$(policy_field jack AuthenticationProviderId)" = "$EMBY_PROVIDER" ]
}

@test "a Quick Connect login does not move a user to Default" {
	henry_id="$(user_by_name "$JELLYFIN" "$JF_TOKEN" henry | jq -r .Id)"

	run quick_connect_status "$JF_TOKEN" "$henry_id"
	[ "$output" = "200" ]
	[ "$(policy_field henry AuthenticationProviderId)" = "$EMBY_PROVIDER" ]

	run login_status "$JELLYFIN" henry henry-emby-pass
	[ "$output" = "200" ]
	[ "$(policy_field henry AuthenticationProviderId)" = "$DEFAULT_PROVIDER" ]
}

@test "a password that an administrator sets in Jellyfin moves the user to Default" {
	kate_id="$(user_by_name "$JELLYFIN" "$JF_TOKEN" kate | jq -r .Id)"

	run status POST "$JELLYFIN/Users/$kate_id/Password" "$JF_TOKEN" '{"NewPw":"kate-jf-pass","CurrentPw":"","ResetPassword":false}'
	[ "$output" = "204" ]
	[ "$(policy_field kate AuthenticationProviderId)" = "$DEFAULT_PROVIDER" ]
	run login_status "$JELLYFIN" kate kate-jf-pass
	[ "$output" = "200" ]
}

@test "the plugin ends its Emby session after each login" {
	run api GET "$EMBY/Sessions?DeviceId=jellyfin-plugin-emby-auth" "$EMBY_TOKEN"
	[ "$status" -eq 0 ]
	[ "$(jq length <<<"$output")" = "0" ]
}

@test "while Emby is unreachable, Jellyfin refuses a user on the Emby login method, even with the saved password" {
	docker compose -f "$COMPOSE_FILE" stop emby-proxy emby >&3 2>&1

	run login_status "$JELLYFIN" erin erin-emby-pass
	[ "$output" = "401" ]
	run login_status "$JELLYFIN" erin erin-jf-random
	[ "$output" = "401" ]
}

@test "while Emby is unreachable, users who moved to Default still log in" {
	run login_status "$JELLYFIN" alice alice-pass-1
	[ "$output" = "200" ]
	run login_status "$JELLYFIN" dave dave-emby-pass
	[ "$output" = "200" ]
	run login_status "$JELLYFIN" henry henry-emby-pass
	[ "$output" = "200" ]
}

@test "the Jellyfin log contains no password and no API key" {
	logs="$(docker compose -f "$COMPOSE_FILE" logs jellyfin 2>&1)"
	[[ "$logs" == *"[DBG]"* ]]

	local secret
	for secret in alice-pass-1 alice-pass-2 wrong-pass jf-admin-pass carol-jf-pass jack-jf-pass kate-jf-pass \
		carol-emby-pass dave-emby-pass erin-emby-pass gina-emby-pass henry-emby-pass ivy-emby-pass jack-emby-pass \
		dave-jf-random erin-jf-random henry-jf-random kate-jf-random "$EMBY_API_KEY"; do
		if [[ "$logs" == *"$secret"* ]]; then
			echo "The Jellyfin log contains the secret '$secret'." >&2
			return 1
		fi
	done
}
