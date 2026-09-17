# shellcheck shell=bash
# Helpers for the e2e tests. Both servers use the same MediaBrowser-style API.

E2E_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export E2E_DIR
# compose.yaml publishes Emby and Jellyfin on these host ports. Set them to run next to another copy of the containers.
export EMBY_PORT="${EMBY_PORT:-18096}"
export JELLYFIN_PORT="${JELLYFIN_PORT:-28096}"
export EMBY="http://127.0.0.1:$EMBY_PORT"
export JELLYFIN="http://127.0.0.1:$JELLYFIN_PORT"
export EMBY_PROVIDER=Jellyfin.Plugin.EmbyAuth.EmbyAuthenticationProvider
export DEFAULT_PROVIDER=Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider
export PLUGIN_ID=e973e09a-e8b4-40c1-9be2-8e51342de1f9
export COMPOSE_FILE="$E2E_DIR/compose.yaml"

auth_header() {
	local token="${1:-}"
	local header='Authorization: MediaBrowser Client="e2e", Device="e2e", DeviceId="e2e-tests", Version="1.0.0"'
	if [[ -n "$token" ]]; then
		header+=", Token=\"$token\""
	fi
	printf '%s' "$header"
}

# api METHOD URL TOKEN [JSON] -> prints the response body, fails on HTTP >= 400.
api() {
	local method="$1" url="$2" token="$3" body="${4:-}"
	local args=(-sS --fail-with-body -X "$method" -H "$(auth_header "$token")")
	if [[ -n "$body" ]]; then
		args+=(-H 'Content-Type: application/json' -d "$body")
	fi
	curl "${args[@]}" "$url"
}

# status METHOD URL TOKEN [JSON] -> prints the HTTP status code only.
status() {
	local method="$1" url="$2" token="$3" body="${4:-}"
	local args=(-sS -o /dev/null -w '%{http_code}' -X "$method" -H "$(auth_header "$token")")
	if [[ -n "$body" ]]; then
		args+=(-H 'Content-Type: application/json' -d "$body")
	fi
	curl "${args[@]}" "$url"
}

credentials_json() {
	jq -cn --arg u "$1" --arg p "$2" '{Username: $u, Pw: $p}'
}

# wait_until DESCRIPTION COMMAND... -> runs COMMAND every 2 seconds until it succeeds, for at most 180 seconds.
wait_until() {
	local description="$1"
	shift
	for _ in $(seq 1 90); do
		if "$@"; then
			return 0
		fi
		sleep 2
	done
	echo "$description did not become ready within 180 seconds." >&2
	return 1
}

emby_ready() {
	curl -sf "$EMBY/System/Info/Public" >/dev/null
}

# Jellyfin answers with "Degraded" or a loading message while it starts.
jellyfin_ready() {
	[[ "$(curl -s "$JELLYFIN/health")" == "Healthy" ]]
}

complete_startup_wizard() {
	local base="$1" admin="$2" password="$3"
	api POST "$base/Startup/Configuration" "" '{"UICulture":"en-us","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}' >/dev/null
	api GET "$base/Startup/User" "" >/dev/null
	api POST "$base/Startup/User" "" "$(jq -cn --arg n "$admin" --arg p "$password" '{Name: $n, Password: $p}')" >/dev/null
	api POST "$base/Startup/Complete" "" >/dev/null
}

login_token() {
	local base="$1" user="$2" password="$3"
	api POST "$base/Users/AuthenticateByName" "" "$(credentials_json "$user" "$password")" | jq -r .AccessToken
}

login_status() {
	local base="$1" user="$2" password="$3"
	status POST "$base/Users/AuthenticateByName" "" "$(credentials_json "$user" "$password")"
}

# create_user BASE ADMIN_TOKEN NAME -> prints the new user ID.
create_user() {
	local base="$1" token="$2" name="$3"
	api POST "$base/Users/New" "$token" "$(jq -cn --arg n "$name" '{Name: $n}')" | jq -r .Id
}

set_password() {
	local base="$1" token="$2" user_id="$3" password="$4"
	api POST "$base/Users/$user_id/Password" "$token" \
		"$(jq -cn --arg p "$password" '{NewPw: $p, CurrentPw: "", ResetPassword: false}')" >/dev/null
}

user_by_name() {
	local base="$1" token="$2" name="$3"
	api GET "$base/Users" "$token" | jq -c --arg n "$name" '.[] | select(.Name == $n)'
}

# update_policy BASE TOKEN USER_ID JQ_FILTER -> applies JQ_FILTER to the user policy and saves it.
update_policy() {
	local base="$1" token="$2" user_id="$3" filter="$4"
	local policy
	policy="$(api GET "$base/Users/$user_id" "$token" | jq -c ".Policy | $filter")"
	api POST "$base/Users/$user_id/Policy" "$token" "$policy" >/dev/null
}

set_login_method() {
	local token="$1" user_id="$2" provider="$3"
	update_policy "$JELLYFIN" "$token" "$user_id" ".AuthenticationProviderId = \"$provider\""
}

emby_user_id() {
	api GET "$EMBY/Users" "$EMBY_TOKEN" | jq -r --arg n "$1" '.[] | select(.Name == $n) | .Id'
}

jellyfin_user_id() {
	user_by_name "$JELLYFIN" "$JF_TOKEN" "$1" | jq -r .Id
}

# policy_field NAME FIELD -> prints a field of the Jellyfin user policy.
policy_field() {
	user_by_name "$JELLYFIN" "$JF_TOKEN" "$1" | jq -r ".Policy.$2"
}

# precreate_on_emby_method NAME -> creates a Jellyfin account with the password NAME-jf-random on the Emby login method.
precreate_on_emby_method() {
	local name="$1" id
	id="$(create_user "$JELLYFIN" "$JF_TOKEN" "$name")"
	set_password "$JELLYFIN" "$JF_TOKEN" "$id" "$name-jf-random"
	set_login_method "$JF_TOKEN" "$id" "$EMBY_PROVIDER"
}

# precreate_admin_on_emby_method NAME -> creates a Jellyfin administrator with the password NAME-jf-pass on the Emby login method.
precreate_admin_on_emby_method() {
	local name="$1" id
	id="$(create_user "$JELLYFIN" "$JF_TOKEN" "$name")"
	set_password "$JELLYFIN" "$JF_TOKEN" "$id" "$name-jf-pass"
	update_policy "$JELLYFIN" "$JF_TOKEN" "$id" ".IsAdministrator = true | .AuthenticationProviderId = \"$EMBY_PROVIDER\""
}

reset_plugin_config() {
	set_plugin_config "$JF_TOKEN" '.MigrationMode = "MoveAfterFirstLogin" | .AccountAccess = "CopyEmbyRemoteAccess"'
}

# set_plugin_config TOKEN JQ_FILTER -> applies JQ_FILTER to the plugin settings and saves them.
set_plugin_config() {
	local token="$1" filter="$2"
	local config
	config="$(api GET "$JELLYFIN/Plugins/$PLUGIN_ID/Configuration" "$token" | jq -c "$filter")"
	api POST "$JELLYFIN/Plugins/$PLUGIN_ID/Configuration" "$token" "$config" >/dev/null
}

# run_migration_task TOKEN -> runs the plugin's migration task, waits for it to end, and prints its result status.
run_migration_task() {
	local token="$1"
	local task_id before after state
	task_id="$(api GET "$JELLYFIN/ScheduledTasks" "$token" | jq -r '.[] | select(.Key == "EmbyAuthMoveUsersToDefault") | .Id')"
	if [[ -z "$task_id" ]]; then
		echo "Jellyfin has no scheduled task with the key EmbyAuthMoveUsersToDefault." >&2
		return 1
	fi

	before="$(api GET "$JELLYFIN/ScheduledTasks/$task_id" "$token" | jq -r '.LastExecutionResult.EndTimeUtc // ""')"
	api POST "$JELLYFIN/ScheduledTasks/Running/$task_id" "$token" >/dev/null
	for _ in $(seq 1 30); do
		sleep 1
		state="$(api GET "$JELLYFIN/ScheduledTasks/$task_id" "$token")"
		after="$(jq -r '.LastExecutionResult.EndTimeUtc // ""' <<<"$state")"
		if [[ "$(jq -r .State <<<"$state")" == "Idle" && -n "$after" && "$after" != "$before" ]]; then
			jq -r .LastExecutionResult.Status <<<"$state"
			return 0
		fi
	done
	echo "The migration task did not end within 30 seconds." >&2
	return 1
}

# create_emby_api_key TOKEN APP_NAME -> prints the new API key.
create_emby_api_key() {
	local token="$1" app="$2"
	api POST "$EMBY/Auth/Keys?App=$app" "$token" >/dev/null
	api GET "$EMBY/Auth/Keys" "$token" | jq -r --arg a "$app" '.Items[] | select(.AppName == $a) | .AccessToken'
}

# emby_login_requests NAME -> prints how many login requests for exactly NAME reached Emby through the proxy.
# It first sends a marker login through the proxy and waits until the marker is in the proxy log,
# so that the count includes every earlier request. It fails if the marker does not appear.
emby_login_requests() {
	local name="$1"
	local marker="marker-$RANDOM$RANDOM"
	local logs=""
	docker compose -f "$COMPOSE_FILE" exec -T emby-proxy \
		wget -q -O /dev/null --header 'Content-Type: application/json' \
		--post-data "{\"Username\":\"$marker\",\"Pw\":\"x\"}" http://127.0.0.1:8096/Users/AuthenticateByName >/dev/null 2>&1 || true

	for _ in $(seq 1 20); do
		logs="$(docker compose -f "$COMPOSE_FILE" logs --no-log-prefix emby-proxy)"
		if grep -q -F "$marker" <<<"$logs"; then
			grep -F 'POST /Users/AuthenticateByName' <<<"$logs" | grep -c -F "\\\"Username\\\":\\\"$name\\\"" || true
			return 0
		fi
		sleep 1
	done
	echo "The marker request did not appear in the proxy log within 20 seconds." >&2
	return 1
}

# quick_connect_status ADMIN_TOKEN USER_ID -> prints the HTTP status of a Quick Connect login that the admin authorizes for the user.
quick_connect_status() {
	local token="$1" user_id="$2"
	local request code secret
	request="$(api POST "$JELLYFIN/QuickConnect/Initiate" "")"
	code="$(jq -r .Code <<<"$request")"
	secret="$(jq -r .Secret <<<"$request")"
	api POST "$JELLYFIN/QuickConnect/Authorize?code=$code&userId=$user_id" "$token" >/dev/null
	status POST "$JELLYFIN/Users/AuthenticateWithQuickConnect" "" "$(jq -cn --arg s "$secret" '{Secret: $s}')"
}
