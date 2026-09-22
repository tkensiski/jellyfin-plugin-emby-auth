# shellcheck shell=bash
# Helpers for the e2e tests. Both servers use the same MediaBrowser-style API.

E2E_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export E2E_DIR
# compose.yaml publishes Emby and Jellyfin on these host ports. Set them to run next to another copy of the containers.
export EMBY_PORT="${EMBY_PORT:-18096}"
export JELLYFIN_PORT="${JELLYFIN_PORT:-28096}"
export EMBY="http://127.0.0.1:$EMBY_PORT"
export JELLYFIN="http://127.0.0.1:$JELLYFIN_PORT"
# The `catalog` profile services (85-catalog-install.bats only) publish on these host ports.
export CATALOG_JELLYFIN_PORT="${CATALOG_JELLYFIN_PORT:-38096}"
export CATALOG_MANIFEST_PORT="${CATALOG_MANIFEST_PORT:-38080}"
export CATALOG_JELLYFIN="http://127.0.0.1:$CATALOG_JELLYFIN_PORT"
# The directory catalog-manifest's nginx serves as its document root, and where
# 85-catalog-install.bats writes the two builds and the merged manifest.json.
export CATALOG_ARTIFACT_DIR="$E2E_DIR/../artifacts/catalog"
export EMBY_PROVIDER=Jellyfin.Plugin.EmbyAuth.EmbyAuthenticationProvider
export DEFAULT_PROVIDER=Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider
export PLUGIN_ID=e973e09a-e8b4-40c1-9be2-8e51342de1f9
export COMPOSE_FILE="$E2E_DIR/compose.yaml"
# The Emby URL the plugin is configured with, as Jellyfin's container reaches the proxy.
export EMBY_INTERNAL_URL="http://emby-proxy:8096"
# The fingerprint store database, inside the Jellyfin container. This is Jellyfin's own
# DataFolderPath derivation: the plugins directory plus the plugin assembly's file name without
# its extension (PluginServiceRegistrator.cs).
export FINGERPRINT_STORE_DB="/config/plugins/Jellyfin.Plugin.EmbyAuth/Jellyfin.Plugin.EmbyAuth.VerifiedPasswords.db"
# The legacy fingerprint JSON file the plugin read before it moved to the store above, inside the
# Jellyfin container. This is PluginConfigurationsPath, the plugins directory plus "configurations".
export LEGACY_FINGERPRINT_FILE="/config/plugins/configurations/Jellyfin.Plugin.EmbyAuth.VerifiedPasswords.json"

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

# jellyfin_stop -> stops the Jellyfin container. Stopping sends a termination signal and lets
# Jellyfin close its SQLite connections, which checkpoints and removes the write-ahead log, so a
# database copied out afterward is complete. Never copy the database out while Jellyfin runs.
jellyfin_stop() {
	docker compose -f "$COMPOSE_FILE" stop jellyfin
}

# jellyfin_start -> starts the Jellyfin container again and waits for it to become healthy.
jellyfin_start() {
	docker compose -f "$COMPOSE_FILE" start jellyfin
	wait_until Jellyfin jellyfin_ready
}

# _fingerprint_store_container_command COMMAND... -> runs COMMAND in a throwaway container that
# shares the Jellyfin container's volumes, using the nginx:1.30.5-alpine image the emby-proxy
# service already pulls for this stack. `docker compose exec` needs a running container, but a
# stopped container's data must not be touched by a running Jellyfin, so file removal inside it
# goes through `--volumes-from` on a throwaway container instead.
_fingerprint_store_container_command() {
	local jellyfin_container_id
	jellyfin_container_id="$(docker compose -f "$COMPOSE_FILE" ps -a -q jellyfin)"
	docker run --rm --volumes-from "$jellyfin_container_id" nginx:1.30.5-alpine "$@"
}

# fingerprint_store_pull DEST -> copies the fingerprint store database out of a stopped
# container into the file DEST on the host. Requires sqlite3 on the host and fails, naming it,
# rather than skipping: a silent skip would make this test look like it covers the upgrade path
# when it does not.
fingerprint_store_pull() {
	local dest="$1"
	if ! command -v sqlite3 >/dev/null; then
		echo "sqlite3 is required on the host to read the fingerprint store and was not found." >&2
		return 1
	fi
	docker compose -f "$COMPOSE_FILE" cp "jellyfin:$FINGERPRINT_STORE_DB" "$dest"
	if [[ ! -f "$dest" ]]; then
		echo "fingerprint_store_pull did not produce a file at $dest." >&2
		return 1
	fi
}

# fingerprint_store_push SOURCE -> copies the database file SOURCE from the host back into the
# container at the fingerprint store path, then removes the write-ahead-log and shared-memory
# siblings inside the container so a stale log cannot be replayed over the file just pushed.
fingerprint_store_push() {
	local source="$1"
	docker compose -f "$COMPOSE_FILE" cp "$source" "jellyfin:$FINGERPRINT_STORE_DB"
	_fingerprint_store_container_command sh -c "rm -f '$FINGERPRINT_STORE_DB-wal' '$FINGERPRINT_STORE_DB-shm'"
}

# fingerprint_store_remove -> removes the database and its write-ahead-log and shared-memory
# siblings inside the container. This is the state a server upgrading from the JSON store is in.
fingerprint_store_remove() {
	_fingerprint_store_container_command sh -c "rm -f '$FINGERPRINT_STORE_DB' '$FINGERPRINT_STORE_DB-wal' '$FINGERPRINT_STORE_DB-shm'"
}

# legacy_fingerprint_file_write SOURCE -> copies a JSON file from the host into the container at
# the legacy fingerprint file path.
legacy_fingerprint_file_write() {
	local source="$1"
	docker compose -f "$COMPOSE_FILE" cp "$source" "jellyfin:$LEGACY_FINGERPRINT_FILE"
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

# precreate_on_emby_method_without_password NAME -> creates a Jellyfin account on the Emby login method with no saved password.
# This deliberately produces the account state AUTH-06's migration test needs: a user on the Emby login
# method whose account has never had a Jellyfin password set, so the migration list names them as such
# and the migration task must not move them.
precreate_on_emby_method_without_password() {
	local name="$1" id
	id="$(create_user "$JELLYFIN" "$JF_TOKEN" "$name")"
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
	set_plugin_config "$JF_TOKEN" \
		".MigrationMode = \"MoveAfterFirstLogin\" | .AccountAccess = \"CopyEmbyRemoteAccess\" | .MigrationTarget = \"$DEFAULT_PROVIDER\" | .PasswordSetTarget = \"\""
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
	task_id="$(api GET "$JELLYFIN/ScheduledTasks" "$token" | jq -r '.[] | select(.Key == "EmbyAuthMigration") | .Id')"
	if [[ -z "$task_id" ]]; then
		echo "Jellyfin has no scheduled task with the key EmbyAuthMigration." >&2
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

# jellyfin_log_lines PATTERN -> prints every Jellyfin log line containing the fixed string PATTERN.
# Jellyfin's log reaches `docker compose logs` a moment after the request that produced it, so this
# retries for up to ten seconds before giving up. The caller asserts the log level itself by looking
# for the Serilog level token (for example "[ERR]") in the returned lines, which lets this one helper
# serve both an Error-level assertion and a plain presence check.
jellyfin_log_lines() {
	local pattern="$1"
	local logs matches
	for _ in $(seq 1 10); do
		logs="$(docker compose -f "$COMPOSE_FILE" logs jellyfin 2>&1)"
		matches="$(grep -F "$pattern" <<<"$logs" || true)"
		if [[ -n "$matches" ]]; then
			printf '%s\n' "$matches"
			return 0
		fi
		sleep 1
	done
	echo "No Jellyfin log line matched '$pattern' within 10 seconds." >&2
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
