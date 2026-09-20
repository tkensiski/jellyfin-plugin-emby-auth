#!/usr/bin/env bats
# The migration hand-over to a second real login method, and the server-side target refusals.
# This file's scope stops at the hand-over: an Emby login, the saved password, the move, and that
# same password logging the user in. What the second login method does with the account afterwards
# is out of scope.

setup_file() {
	load helpers
	reset_plugin_config
	set_password "$JELLYFIN" "$JF_TOKEN" "$(create_user "$JELLYFIN" "$JF_TOKEN" zelda)" zelda-jf-pass
}

teardown_file() {
	load helpers
	reset_plugin_config
}

setup() {
	load helpers
}

migration_user_state() {
	api GET "$JELLYFIN/EmbyAuth/Migration" "$JF_TOKEN" | jq -r --arg n "$1" '.Users[] | select(.Name == $n) | .State'
}

# jellyfinsecurity_provider_id -> prints the login method id of the one AvailableTargets entry that
# is neither Jellyfin's Default nor this plugin's own Emby method. Read from the plugin's own API,
# never hardcoded, so a JellyfinSecurity version bump cannot silently break this file.
jellyfinsecurity_provider_id() {
	local id
	id="$(api GET "$JELLYFIN/EmbyAuth/Migration" "$JF_TOKEN" |
		jq -r --arg default "$DEFAULT_PROVIDER" --arg emby "$EMBY_PROVIDER" \
			'[.AvailableTargets[] | select(.Id != $default and .Id != $emby)] | first | .Id // empty')"
	if [[ -z "$id" ]]; then
		echo "No available migration target other than Default and Emby. Did the second login method's plugin mount take effect?" >&2
		return 1
	fi
	echo "$id"
}

# post_plugin_config_status FILTER -> applies FILTER to the current config and posts it, printing
# the HTTP status. Uses status rather than api, because api fails the test on a non-2xx response
# and a non-2xx response is what these tests assert.
post_plugin_config_status() {
	local filter="$1" config
	config="$(api GET "$JELLYFIN/Plugins/$PLUGIN_ID/Configuration" "$JF_TOKEN" | jq -c "$filter")"
	status POST "$JELLYFIN/Plugins/$PLUGIN_ID/Configuration" "$JF_TOKEN" "$config"
}

@test "a user migrates to the second login method and then logs in without reaching Emby" {
	local target
	target="$(jellyfinsecurity_provider_id)"

	set_plugin_config "$JF_TOKEN" ".MigrationMode = \"KeepEmbyInCharge\" | .MigrationTarget = \"$target\""

	run login_status "$JELLYFIN" yara yara-emby-pass
	[ "$output" = "200" ]
	[ "$(policy_field yara AuthenticationProviderId)" = "$EMBY_PROVIDER" ]
	[ "$(migration_user_state yara)" = "Ready" ]

	run run_migration_task "$JF_TOKEN"
	[ "$output" = "Completed" ]
	[ "$(policy_field yara AuthenticationProviderId)" = "$target" ]

	local before after
	before="$(emby_login_requests yara)"
	run login_status "$JELLYFIN" yara yara-emby-pass
	[ "$output" = "200" ]
	after="$(emby_login_requests yara)"
	[ "$after" = "$before" ]
}

@test "an account with no saved password is named, not moved, and still logs in through Emby" {
	precreate_on_emby_method_without_password zack

	[ "$(migration_user_state zack)" = "NoPassword" ]

	run run_migration_task "$JF_TOKEN"
	[ "$output" = "Completed" ]
	[ "$(policy_field zack AuthenticationProviderId)" = "$EMBY_PROVIDER" ]

	run login_status "$JELLYFIN" zack zack-emby-pass
	[ "$output" = "200" ]
}

@test "the server refuses a migration target Jellyfin does not report as enabled" {
	reset_plugin_config
	run post_plugin_config_status '.MigrationTarget = "Jellyfin.Plugin.NoSuchProvider.NotReal"'
	[ "$output" -ge 400 ]
	[ "$(api GET "$JELLYFIN/Plugins/$PLUGIN_ID/Configuration" "$JF_TOKEN" | jq -r .MigrationTarget)" = "$DEFAULT_PROVIDER" ]
}

@test "the server refuses this plugin's own Emby login method as a migration target" {
	reset_plugin_config
	run post_plugin_config_status ".MigrationTarget = \"$EMBY_PROVIDER\""
	[ "$output" -ge 400 ]
	[ "$(api GET "$JELLYFIN/Plugins/$PLUGIN_ID/Configuration" "$JF_TOKEN" | jq -r .MigrationTarget)" = "$DEFAULT_PROVIDER" ]
}

@test "a regular user gets 403 from both migration routes" {
	local zelda_token
	zelda_token="$(login_token "$JELLYFIN" zelda zelda-jf-pass)"
	run status GET "$JELLYFIN/EmbyAuth/Migration" "$zelda_token"
	[ "$output" = "403" ]
	run status POST "$JELLYFIN/EmbyAuth/Migration/Run" "$zelda_token"
	[ "$output" = "403" ]
}
