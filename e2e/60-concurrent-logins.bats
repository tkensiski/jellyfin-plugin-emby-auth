#!/usr/bin/env bats
# Concurrent first logins through the running server. bella has no Jellyfin account: she is the
# unknown name whose first logins race on Jellyfin's shared lock. chris already has a Jellyfin
# account on the Emby login method, so her logins race on her own per-user lock instead.

setup_file() {
	load helpers
	reset_plugin_config
	precreate_on_emby_method chris
}

setup() {
	load helpers
}

# burst_logins NAME PASSWORD COUNT -> starts COUNT concurrent logins for NAME, waits for every one
# of them, then prints the path of each result file (one file per login, holding its HTTP status).
# Each login runs in a background subshell with file descriptor 3 closed, because a backgrounded
# command that inherits fd 3 makes bats hang after the test ends (bats-core docs; bats-core#419).
# Do not wrap the backgrounded call in `run`, which captures through fd 3.
burst_logins() {
	local name="$1" password="$2" count="$3"
	local pids=() files=() i file
	for i in $(seq 1 "$count"); do
		file="$BATS_TEST_TMPDIR/burst-$name-$i"
		files+=("$file")
		(login_status "$JELLYFIN" "$name" "$password" >"$file" 3>&-) &
		pids+=($!)
	done
	for i in "${pids[@]}"; do
		wait "$i"
	done
	printf '%s\n' "${files[@]}"
}

@test "a burst of concurrent first logins for an unknown name leaves exactly one account" {
	local files status_file status accounts

	mapfile -t files < <(burst_logins bella bella-emby-pass 5)

	for status_file in "${files[@]}"; do
		status="$(cat "$status_file")"
		if [[ "$status" != "200" && "$status" != "401" ]]; then
			echo "Unexpected login status $status in $status_file (expected 200 or 401, never 500)" >&2
			return 1
		fi
	done

	accounts="$(user_by_name "$JELLYFIN" "$JF_TOKEN" bella | jq -s 'length')"
	[ "$accounts" = "1" ]

	status="$(login_status "$JELLYFIN" bella bella-emby-pass)"
	[ "$status" = "200" ]
}

@test "a burst of concurrent logins for a name with an existing account all succeed" {
	local files status_file status accounts

	mapfile -t files < <(burst_logins chris chris-emby-pass 5)

	for status_file in "${files[@]}"; do
		status="$(cat "$status_file")"
		if [[ "$status" != "200" ]]; then
			echo "Unexpected login status $status in $status_file (expected 200 for every response)" >&2
			return 1
		fi
	done

	accounts="$(user_by_name "$JELLYFIN" "$JF_TOKEN" chris | jq -s 'length')"
	[ "$accounts" = "1" ]
	[ "$(policy_field chris AuthenticationProviderId)" = "$DEFAULT_PROVIDER" ]
}

@test "a second burst for a name whose account already exists still leaves exactly one account" {
	local files status_file status accounts

	mapfile -t files < <(burst_logins bella bella-emby-pass 5)

	for status_file in "${files[@]}"; do
		status="$(cat "$status_file")"
		if [[ "$status" != "200" ]]; then
			echo "Unexpected login status $status in $status_file (expected 200 for every response, the account already exists)" >&2
			return 1
		fi
	done

	accounts="$(user_by_name "$JELLYFIN" "$JF_TOKEN" bella | jq -s 'length')"
	[ "$accounts" = "1" ]
}
