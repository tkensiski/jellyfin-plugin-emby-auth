#!/usr/bin/env bats
# Login checks with the default settings. setup_suite.bash starts the servers and creates the Emby users.

setup_file() {
	load helpers
	reset_plugin_config
	precreate_on_emby_method erin
	set_password "$JELLYFIN" "$JF_TOKEN" "$(create_user "$JELLYFIN" "$JF_TOKEN" carol)" carol-jf-pass
}

setup() {
	load helpers
}

@test "Jellyfin serves the plugin settings page" {
	run status GET "$JELLYFIN/web/ConfigurationPage?name=Emby%20Auth" "$JF_TOKEN"
	[ "$output" = "200" ]
}

@test "the first login of an Emby user creates a Jellyfin account on the Default login method" {
	run login_status "$JELLYFIN" alice alice-emby-pass
	[ "$output" = "200" ]

	# Also proves that the proxy log records the plugin's login requests.
	[ "$(emby_login_requests alice)" = "1" ]
	[ "$(policy_field alice AuthenticationProviderId)" = "$DEFAULT_PROVIDER" ]
	[ "$(policy_field alice IsAdministrator)" = "false" ]
	[ "$(policy_field alice EnableRemoteAccess)" = "true" ]
}

@test "after the first login, Jellyfin checks the password without Emby" {
	run login_status "$JELLYFIN" alice alice-emby-pass
	[ "$output" = "200" ]
	[ "$(policy_field alice AuthenticationProviderId)" = "$DEFAULT_PROVIDER" ]

	set_password "$EMBY" "$EMBY_TOKEN" "$(emby_user_id alice)" alice-emby-pass-2
	run login_status "$JELLYFIN" alice alice-emby-pass-2
	[ "$output" = "401" ]
	run login_status "$JELLYFIN" alice alice-emby-pass
	[ "$output" = "200" ]
}

@test "Jellyfin refuses a wrong password for a user on the Emby login method" {
	run login_status "$JELLYFIN" erin wrong-pass
	[ "$output" = "401" ]
	[ "$(policy_field erin AuthenticationProviderId)" = "$EMBY_PROVIDER" ]
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

@test "the plugin ends its Emby session after each login" {
	run login_status "$JELLYFIN" erin erin-emby-pass
	[ "$output" = "200" ]

	run api GET "$EMBY/Sessions?DeviceId=jellyfin-plugin-emby-auth" "$EMBY_TOKEN"
	[ "$status" -eq 0 ]
	[ "$(jq length <<<"$output")" = "0" ]
}
