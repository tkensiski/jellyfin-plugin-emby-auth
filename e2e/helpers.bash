# shellcheck shell=bash
# Helpers for e2e/emby-auth.bats. Both servers use the same MediaBrowser-style API.

export EMBY=http://127.0.0.1:18096
export JELLYFIN=http://127.0.0.1:28096
export EMBY_PROVIDER=Jellyfin.Plugin.EmbyAuth.EmbyAuthenticationProvider
export DEFAULT_PROVIDER=Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider
export PLUGIN_ID=e973e09a-e8b4-40c1-9be2-8e51342de1f9
export COMPOSE_FILE="${BATS_TEST_DIRNAME}/compose.yaml"

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

set_login_method() {
	local token="$1" user_id="$2" provider="$3"
	local policy
	policy="$(api GET "$JELLYFIN/Users/$user_id" "$token" | jq -c --arg p "$provider" '.Policy | .AuthenticationProviderId = $p')"
	api POST "$JELLYFIN/Users/$user_id/Policy" "$token" "$policy" >/dev/null
}
