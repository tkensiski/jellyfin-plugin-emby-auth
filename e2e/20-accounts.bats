#!/usr/bin/env bats
# Account handling with the default settings: accounts created before the first login, remote access,
# administrators, Quick Connect, and password changes in Jellyfin.

setup_file() {
	load helpers
	reset_plugin_config
	local name
	for name in dave henry kate leo; do
		precreate_on_emby_method "$name"
	done
	precreate_admin_on_emby_method jack
}

setup() {
	load helpers
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
	[ "$(emby_login_requests jack)" = "0" ]
	[ "$(policy_field jack IsAdministrator)" = "true" ]
	[ "$(policy_field jack AuthenticationProviderId)" = "$EMBY_PROVIDER" ]
}

@test "a Quick Connect login does not move a user to Default" {
	run quick_connect_status "$JF_TOKEN" "$(jellyfin_user_id henry)"
	[ "$output" = "200" ]
	[ "$(policy_field henry AuthenticationProviderId)" = "$EMBY_PROVIDER" ]

	run login_status "$JELLYFIN" henry henry-emby-pass
	[ "$output" = "200" ]
	[ "$(policy_field henry AuthenticationProviderId)" = "$DEFAULT_PROVIDER" ]
}

@test "a password that an administrator sets in Jellyfin moves the user to Default" {
	run status POST "$JELLYFIN/Users/$(jellyfin_user_id kate)/Password" "$JF_TOKEN" '{"NewPw":"kate-jf-pass","CurrentPw":"","ResetPassword":false}'
	[ "$output" = "204" ]
	[ "$(policy_field kate AuthenticationProviderId)" = "$DEFAULT_PROVIDER" ]
	run login_status "$JELLYFIN" kate kate-jf-pass
	[ "$output" = "200" ]
}

@test "a password reset in Jellyfin keeps the user on the Emby login method and does not allow a blank password" {
	run status POST "$JELLYFIN/Users/$(jellyfin_user_id leo)/Password" "$JF_TOKEN" '{"ResetPassword":true}'
	[ "$output" = "204" ]
	[ "$(policy_field leo AuthenticationProviderId)" = "$EMBY_PROVIDER" ]
	run login_status "$JELLYFIN" leo ""
	[ "$output" = "401" ]

	run login_status "$JELLYFIN" leo leo-emby-pass
	[ "$output" = "200" ]
	[ "$(policy_field leo AuthenticationProviderId)" = "$DEFAULT_PROVIDER" ]
}
