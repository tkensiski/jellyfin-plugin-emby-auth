#!/usr/bin/env bats
# The migration behavior and account access settings, and the migration task.

setup_file() {
	load helpers
	reset_plugin_config
	precreate_on_emby_method nora
	precreate_admin_on_emby_method rex
	set_password "$JELLYFIN" "$JF_TOKEN" "$(create_user "$JELLYFIN" "$JF_TOKEN" xena)" xena-jf-pass
}

wes_is_on_default() {
	[[ "$(policy_field wes AuthenticationProviderId)" == "$DEFAULT_PROVIDER" ]]
}

migration_user_state() {
	api GET "$JELLYFIN/EmbyAuth/Migration" "$JF_TOKEN" | jq -r --arg n "$1" '.Users[] | select(.Name == $n) | .State'
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

@test "Keep Emby in charge: an Emby password change is effective at once and the saved hash follows it" {
	set_plugin_config "$JF_TOKEN" '.MigrationMode = "KeepEmbyInCharge"'

	run login_status "$JELLYFIN" oscar oscar-emby-pass
	[ "$output" = "200" ]
	[ "$(policy_field oscar AuthenticationProviderId)" = "$EMBY_PROVIDER" ]

	set_password "$EMBY" "$EMBY_TOKEN" "$(emby_user_id oscar)" oscar-emby-pass-2
	run login_status "$JELLYFIN" oscar oscar-emby-pass
	[ "$output" = "401" ]
	run login_status "$JELLYFIN" oscar oscar-emby-pass-2
	[ "$output" = "200" ]

	[ "$(migration_user_state oscar)" = "Ready" ]
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

@test "the migration status shows who is ready to move, and only an administrator can use the migration API" {
	set_plugin_config "$JF_TOKEN" '.MigrationMode = "KeepEmbyInCharge" | .AccountAccess = "CopyEmbyRemoteAccess"'
	run login_status "$JELLYFIN" wes wes-emby-pass
	[ "$output" = "200" ]

	[ "$(migration_user_state wes)" = "Ready" ]
	[ "$(migration_user_state nora)" = "NeedsEmbyLogin" ]
	[ "$(migration_user_state rex)" = "NeedsEmbyLogin" ]

	xena_token="$(login_token "$JELLYFIN" xena xena-jf-pass)"
	run status GET "$JELLYFIN/EmbyAuth/Migration" "$xena_token"
	[ "$output" = "403" ]
	run status POST "$JELLYFIN/EmbyAuth/Migration/Run" "$xena_token"
	[ "$output" = "403" ]
	[ "$(policy_field wes AuthenticationProviderId)" = "$EMBY_PROVIDER" ]
}

@test "Run migration moves the users who are ready and keeps the others" {
	run status POST "$JELLYFIN/EmbyAuth/Migration/Run" "$JF_TOKEN"
	[ "$output" = "204" ]

	wait_until "The move of wes to Default" wes_is_on_default
	[ "$(policy_field nora AuthenticationProviderId)" = "$EMBY_PROVIDER" ]
	[ -z "$(migration_user_state wes)" ]
	[ "$(migration_user_state nora)" = "NeedsEmbyLogin" ]
}
