#!/usr/bin/env bats
# Invalid settings saved on a running server. EmbyAuthPlugin.UpdateConfiguration validates only the
# migration target and the password-set target, so each of the four cases EmbyAuthSettings.FindProblem
# produces saves through the live settings API with no code change, and each must refuse a login.

setup_file() {
	load helpers
	reset_plugin_config
	precreate_on_emby_method dana
}

setup() {
	load helpers
}

teardown_file() {
	load helpers
	reset_plugin_config
	set_plugin_config "$JF_TOKEN" ".EmbyServerUrl = \"$EMBY_INTERNAL_URL\" | .EmbyApiKey = \"$EMBY_API_KEY\""
}

# assert_settings_problem FILTER SENTENCE -> resets the plugin to a known-good state, applies FILTER
# to the plugin settings, then asserts a login on the Emby login method is refused and that the
# Jellyfin log names SENTENCE at Error level. Resetting first means one case never inherits the
# broken settings a previous case left, so this must never build a payload from a fetched
# configuration a previous case already broke.
assert_settings_problem() {
	local filter="$1" sentence="$2" status log

	reset_plugin_config
	set_plugin_config "$JF_TOKEN" ".EmbyServerUrl = \"$EMBY_INTERNAL_URL\" | .EmbyApiKey = \"$EMBY_API_KEY\""
	set_plugin_config "$JF_TOKEN" "$filter"

	status="$(login_status "$JELLYFIN" dana dana-emby-pass)"
	[ "$status" = "401" ]

	log="$(jellyfin_log_lines "$sentence")"
	[[ "$log" == *"[ERR]"* ]]
}

@test "a blank Emby server URL refuses a login and names the problem in the Jellyfin log" {
	assert_settings_problem '.EmbyServerUrl = ""' "The Emby server URL is not set."
}

@test "an Emby server URL that is not a valid URL refuses a login and names the problem in the Jellyfin log" {
	assert_settings_problem '.EmbyServerUrl = "not-a-url"' "The Emby server URL is not a valid http or https URL."
}

@test "an Emby server URL carrying a credential refuses a login, names the problem, and the credential never reaches the log" {
	assert_settings_problem '.EmbyServerUrl = "http://user:leak-emby-pass@emby-proxy:8096"' "The Emby server URL must not contain a user name or password."
}

@test "a blank Emby API key refuses a login and names the problem in the Jellyfin log" {
	assert_settings_problem '.EmbyApiKey = ""' "The Emby API key is not set."
}
