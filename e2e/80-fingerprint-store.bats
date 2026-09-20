#!/usr/bin/env bats
# The one-time JSON-to-SQLite fingerprint import, proven against a real upgrade: a server that
# has a JSON file and no database, started on the version that reads it. elton is reserved for
# this file.

setup_file() {
	load helpers
	reset_plugin_config
	set_plugin_config "$JF_TOKEN" '.MigrationMode = "KeepEmbyInCharge"'

	local pre_upgrade_login_status
	pre_upgrade_login_status="$(login_status "$JELLYFIN" elton elton-emby-pass)"
	if [[ "$pre_upgrade_login_status" != "200" ]]; then
		echo "The pre-upgrade login for elton failed with status $pre_upgrade_login_status." >&2
		return 1
	fi
	if [[ "$(migration_state elton)" != "Ready" ]]; then
		echo "elton is not Ready before the upgrade; the rest of this file would prove nothing." >&2
		return 1
	fi
}

migration_state() {
	api GET "$JELLYFIN/EmbyAuth/Migration" "$JF_TOKEN" | jq -r --arg n "$1" '.Users[] | select(.Name == $n) | .State'
}

teardown_file() {
	load helpers
	jellyfin_start
	reset_plugin_config
}

setup() {
	load helpers
}

@test "a user ready to move before the upgrade is still ready after it" {
	local pre_upgrade_db="$BATS_TEST_TMPDIR/pre-upgrade.db"
	local legacy_rows="$BATS_TEST_TMPDIR/legacy-rows.json"
	local legacy_fixture="$BATS_TEST_TMPDIR/legacy-fixture.json"

	jellyfin_stop
	fingerprint_store_pull "$pre_upgrade_db"

	# Build the legacy JSON fixture from the row the plugin itself just wrote: the fingerprint is a
	# SHA-256 of a Jellyfin password hash, and Jellyfin salts every hash freshly, so a real record
	# from the plugin is the only way to produce a genuine "before the upgrade" file.
	sqlite3 -json "$pre_upgrade_db" "SELECT UserId, Fingerprint FROM VerifiedPasswords;" >"$legacy_rows"
	jq 'map({(.UserId): .Fingerprint}) | add // {}' "$legacy_rows" >"$legacy_fixture"

	legacy_fingerprint_file_write "$legacy_fixture"
	fingerprint_store_remove
	jellyfin_start

	[ "$(migration_state elton)" = "Ready" ]

	# The import must never delete or rewrite the legacy file.
	docker compose -f "$COMPOSE_FILE" exec -T jellyfin test -f "$LEGACY_FINGERPRINT_FILE"
}

@test "a later start does not import again, and the store still records a fresh login" {
	local post_upgrade_db="$BATS_TEST_TMPDIR/post-upgrade.db"
	local login_status_code

	jellyfin_stop
	fingerprint_store_pull "$post_upgrade_db"
	sqlite3 "$post_upgrade_db" "DELETE FROM VerifiedPasswords;" >"$BATS_TEST_TMPDIR/delete.log"
	fingerprint_store_push "$post_upgrade_db"
	jellyfin_start

	# A second import would have put elton's row back. This is the whole assertion: not that the
	# import produced the right value, but that it did not run a second time at all.
	[ "$(migration_state elton)" = "NeedsEmbyLogin" ]
	docker compose -f "$COMPOSE_FILE" exec -T jellyfin test -f "$LEGACY_FINGERPRINT_FILE"

	login_status_code="$(login_status "$JELLYFIN" elton elton-emby-pass)"
	[ "$login_status_code" = "200" ]
	[ "$(migration_state elton)" = "Ready" ]
}
