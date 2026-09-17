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
	export EMBY_TOKEN JF_TOKEN

	api POST "$JELLYFIN/Plugins/$PLUGIN_ID/Configuration" "$JF_TOKEN" '{"EmbyServerUrl":"http://emby:8096"}' >/dev/null

	ALICE_EMBY_ID="$(create_user "$EMBY" "$EMBY_TOKEN" alice)"
	export ALICE_EMBY_ID
	set_password "$EMBY" "$EMBY_TOKEN" "$ALICE_EMBY_ID" alice-pass-1

	set_password "$EMBY" "$EMBY_TOKEN" "$(create_user "$EMBY" "$EMBY_TOKEN" carol)" emby-carol-pass
	set_password "$JELLYFIN" "$JF_TOKEN" "$(create_user "$JELLYFIN" "$JF_TOKEN" carol)" jf-carol-pass

	set_password "$EMBY" "$EMBY_TOKEN" "$(create_user "$EMBY" "$EMBY_TOKEN" dave)" dave-pass
	set_login_method "$JF_TOKEN" "$(create_user "$JELLYFIN" "$JF_TOKEN" dave)" "$EMBY_PROVIDER"

	set_password "$EMBY" "$EMBY_TOKEN" "$(create_user "$EMBY" "$EMBY_TOKEN" erin)" erin-pass
	set_login_method "$JF_TOKEN" "$(create_user "$JELLYFIN" "$JF_TOKEN" erin)" "$EMBY_PROVIDER"

	create_user "$EMBY" "$EMBY_TOKEN" frank >/dev/null
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

login_method_of() {
	user_by_name "$JELLYFIN" "$JF_TOKEN" "$1" | jq -r .Policy.AuthenticationProviderId
}

@test "Jellyfin serves the plugin settings page" {
	run status GET "$JELLYFIN/web/ConfigurationPage?name=Emby%20Auth" "$JF_TOKEN"
	[ "$output" = "200" ]
}

@test "the first login of an Emby user creates a Jellyfin account on the Default login method" {
	run login_status "$JELLYFIN" alice alice-pass-1
	[ "$output" = "200" ]

	alice="$(user_by_name "$JELLYFIN" "$JF_TOKEN" alice)"
	[ "$(jq -r .Policy.AuthenticationProviderId <<<"$alice")" = "$DEFAULT_PROVIDER" ]
	[ "$(jq -r .HasPassword <<<"$alice")" = "true" ]
	[ "$(jq -r .Policy.IsAdministrator <<<"$alice")" = "false" ]
}

@test "Jellyfin refuses a wrong password" {
	run login_status "$JELLYFIN" alice wrong-pass
	[ "$output" = "401" ]
}

@test "after the first login, Jellyfin checks the password without Emby" {
	set_password "$EMBY" "$EMBY_TOKEN" "$ALICE_EMBY_ID" alice-pass-2

	run login_status "$JELLYFIN" alice alice-pass-2
	[ "$output" = "401" ]
	run login_status "$JELLYFIN" alice alice-pass-1
	[ "$output" = "200" ]
}

@test "the plugin ends its Emby session after each login" {
	run api GET "$EMBY/Sessions?DeviceId=jellyfin-plugin-emby-auth" "$EMBY_TOKEN"
	[ "$status" -eq 0 ]
	[ "$(jq length <<<"$output")" = "0" ]
}

@test "a Jellyfin account on the Default login method keeps its own password" {
	run login_status "$JELLYFIN" carol emby-carol-pass
	[ "$output" = "401" ]
	run login_status "$JELLYFIN" carol jf-carol-pass
	[ "$output" = "200" ]
}

@test "an account created before its first login uses the Emby password, then moves to Default" {
	run login_status "$JELLYFIN" dave ""
	[ "$output" = "401" ]
	[ "$(login_method_of dave)" = "$EMBY_PROVIDER" ]

	run login_status "$JELLYFIN" dave dave-pass
	[ "$output" = "200" ]
	[ "$(login_method_of dave)" = "$DEFAULT_PROVIDER" ]
}

@test "a user whose Emby account has no password stays on the Emby login method" {
	run login_status "$JELLYFIN" frank ""
	[ "$output" = "200" ]
	[ "$(login_method_of frank)" = "$EMBY_PROVIDER" ]
}

@test "while Emby is unreachable, Jellyfin refuses a user who did not log in yet" {
	docker compose -f "$COMPOSE_FILE" stop emby >&3 2>&1

	run login_status "$JELLYFIN" erin erin-pass
	[ "$output" = "401" ]
}

@test "while Emby is unreachable, users who logged in before still log in" {
	run login_status "$JELLYFIN" alice alice-pass-1
	[ "$output" = "200" ]
	run login_status "$JELLYFIN" dave dave-pass
	[ "$output" = "200" ]
}

@test "the Jellyfin log contains no password" {
	run bash -c "docker compose -f '$COMPOSE_FILE' logs jellyfin 2>&1 | grep -E -c 'alice-pass|carol-pass|dave-pass|erin-pass'"
	[ "$output" = "0" ]
}
