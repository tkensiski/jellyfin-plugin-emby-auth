#!/usr/bin/env bats
# The migration behavior and account access settings, and the migration task.

setup_file() {
	load helpers
	reset_plugin_config
	precreate_on_emby_method nora
	precreate_admin_on_emby_method rex
}

teardown_file() {
	load helpers
	reset_plugin_config
}

setup() {
	load helpers
}

@test "Keep Emby in charge: the user stays on the Emby login method, and Emby decides every login" {
	set_plugin_config "$JF_TOKEN" '.MigrationMode = "KeepEmbyInCharge"'
	[ "$(api GET "$JELLYFIN/Plugins/$PLUGIN_ID/Configuration" "$JF_TOKEN" | jq -r .MigrationMode)" = "KeepEmbyInCharge" ]

	run login_status "$JELLYFIN" mia mia-emby-pass
	[ "$output" = "200" ]
	[ "$(policy_field mia AuthenticationProviderId)" = "$EMBY_PROVIDER" ]

	set_password "$EMBY" "$EMBY_TOKEN" "$(emby_user_id mia)" mia-emby-pass-2
	run login_status "$JELLYFIN" mia mia-emby-pass
	[ "$output" = "401" ]
	run login_status "$JELLYFIN" mia mia-emby-pass-2
	[ "$output" = "200" ]
	[ "$(policy_field mia AuthenticationProviderId)" = "$EMBY_PROVIDER" ]
}

@test "the migration task moves only users whose saved password Emby verified" {
	set_plugin_config "$JF_TOKEN" '.MigrationMode = "KeepEmbyInCharge"'
	run login_status "$JELLYFIN" mia mia-emby-pass-2
	[ "$output" = "200" ]

	run run_migration_task "$JF_TOKEN"
	[ "$output" = "Completed" ]

	[ "$(policy_field mia AuthenticationProviderId)" = "$DEFAULT_PROVIDER" ]
	[ "$(policy_field nora AuthenticationProviderId)" = "$EMBY_PROVIDER" ]
	[ "$(policy_field rex AuthenticationProviderId)" = "$EMBY_PROVIDER" ]
	run login_status "$JELLYFIN" mia mia-emby-pass-2
	[ "$output" = "200" ]
}

@test "Jellyfin password first: the saved password works until Emby accepts a new one" {
	set_plugin_config "$JF_TOKEN" '.MigrationMode = "JellyfinPasswordFirst"'

	run login_status "$JELLYFIN" oscar oscar-emby-pass
	[ "$output" = "200" ]
	[ "$(policy_field oscar AuthenticationProviderId)" = "$EMBY_PROVIDER" ]

	set_password "$EMBY" "$EMBY_TOKEN" "$(emby_user_id oscar)" oscar-emby-pass-2
	run login_status "$JELLYFIN" oscar oscar-emby-pass
	[ "$output" = "200" ]
	run login_status "$JELLYFIN" oscar oscar-emby-pass-2
	[ "$output" = "200" ]
	run login_status "$JELLYFIN" oscar oscar-emby-pass
	[ "$output" = "401" ]
	[ "$(policy_field oscar AuthenticationProviderId)" = "$EMBY_PROVIDER" ]
}

@test "No libraries: a new account gets no library access and copies Emby remote access" {
	set_plugin_config "$JF_TOKEN" '.MigrationMode = "MoveAfterFirstLogin" | .AccountAccess = "NoLibraries"'

	run login_status "$JELLYFIN" paul paul-emby-pass
	[ "$output" = "200" ]
	[ "$(policy_field paul EnableAllFolders)" = "false" ]
	[ "$(policy_field paul EnabledFolders | jq length)" = "0" ]
	[ "$(policy_field paul EnableRemoteAccess)" = "true" ]
}

@test "Jellyfin defaults only: a new account ignores the Emby remote access restriction" {
	set_plugin_config "$JF_TOKEN" '.MigrationMode = "MoveAfterFirstLogin" | .AccountAccess = "JellyfinDefaults"'

	run login_status "$JELLYFIN" quinn quinn-emby-pass
	[ "$output" = "200" ]
	[ "$(policy_field quinn EnableRemoteAccess)" = "true" ]
}
