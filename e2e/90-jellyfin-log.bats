#!/usr/bin/env bats
# Runs last. Checks the whole Jellyfin log, at Debug level, for test passwords and the Emby API key.

setup() {
	load helpers
}

@test "the Jellyfin log contains no password and no API key" {
	logs="$(docker compose -f "$COMPOSE_FILE" logs jellyfin 2>&1)"
	if [[ "$logs" != *"[DBG]"* ]]; then
		echo "The Jellyfin log has no Debug entries, so this check cannot see the Debug-level exceptions." >&2
		return 1
	fi

	# setup_suite.bash names every test password NAME-emby-pass[-N], NAME-jf-pass, or NAME-jf-random.
	if grep -E -o '[a-z]+-(emby-pass|jf-pass|jf-random|admin-pass)(-[0-9]+)?|wrong-pass' <<<"$logs" | sort -u | grep .; then
		echo "The Jellyfin log contains the test passwords above." >&2
		return 1
	fi

	if [[ "$logs" == *"$EMBY_API_KEY"* ]]; then
		echo "The Jellyfin log contains the Emby API key." >&2
		return 1
	fi
}
