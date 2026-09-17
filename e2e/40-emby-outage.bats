#!/usr/bin/env bats
# Logins while Emby is stopped. teardown_file starts Emby again, so later files are not affected.

setup_file() {
	load helpers
	reset_plugin_config
	precreate_on_emby_method sam
	precreate_on_emby_method vic

	# tina moves to Default. uma stays on the Emby login method with a saved password that Emby verified.
	login_token "$JELLYFIN" tina tina-emby-pass >/dev/null
	set_plugin_config "$JF_TOKEN" '.MigrationMode = "KeepEmbyInCharge"'
	login_token "$JELLYFIN" uma uma-emby-pass >/dev/null

	docker compose -f "$COMPOSE_FILE" stop emby-proxy emby >&3 2>&1
}

teardown_file() {
	load helpers
	docker compose -f "$COMPOSE_FILE" start emby emby-proxy >&3 2>&1
	wait_until Emby emby_ready
	reset_plugin_config
}

setup() {
	load helpers
}

@test "while Emby is unreachable, Jellyfin refuses a user on the Emby login method, even with the saved password" {
	run login_status "$JELLYFIN" sam sam-emby-pass
	[ "$output" = "401" ]
	run login_status "$JELLYFIN" sam sam-jf-random
	[ "$output" = "401" ]
}

@test "while Emby is unreachable, a user who moved to Default still logs in" {
	[ "$(policy_field tina AuthenticationProviderId)" = "$DEFAULT_PROVIDER" ]
	run login_status "$JELLYFIN" tina tina-emby-pass
	[ "$output" = "200" ]
}

@test "while Emby is unreachable, a verified saved hash does not open an account" {
	[ "$(policy_field uma AuthenticationProviderId)" = "$EMBY_PROVIDER" ]
	run login_status "$JELLYFIN" uma uma-emby-pass
	[ "$output" = "401" ]
	run login_status "$JELLYFIN" vic vic-jf-random
	[ "$output" = "401" ]
}
