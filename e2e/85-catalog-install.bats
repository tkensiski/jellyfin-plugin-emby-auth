#!/usr/bin/env bats
# A clean Jellyfin 12.1 server, with no plugin mounted, adds the merged manifest as a
# repository, lists both versions in its catalog, installs the lower one, tolerates a repeat
# install of the same version, and updates to the higher one that Jellyfin itself resolves as
# newest. setup_file builds two real versions with scripts/package.sh, merges them with
# scripts/manifest.sh, and starts the two `catalog` profile services; teardown_file removes only
# those two services, leaving the shared stack untouched.

LOWER_VERSION="0.0.1.0"
HIGHER_VERSION="0.0.2.0"
PLUGIN_NAME="Emby Auth"

# plugin_id_nodash -> prints PLUGIN_ID (exported by helpers.bash) with its dashes stripped.
# GET /Packages and GET /Plugins both report the plugin guid this way; meta.json on disk and
# PLUGIN_ID itself keep the dashes. Not computed at file-load time: PLUGIN_ID does not exist
# until setup()'s `load helpers` has run.
plugin_id_nodash() {
	printf '%s' "${PLUGIN_ID//-/}"
}

# catalog_jellyfin_ready -> true once the catalog Jellyfin server's own /health returns Healthy.
# jellyfin_ready in helpers.bash reads the shared JELLYFIN base URL, so it cannot be reused here.
catalog_jellyfin_ready() {
	[[ "$(curl -s "$CATALOG_JELLYFIN/health")" == "Healthy" ]]
}

# installed_versions -> prints one version per line, read from every meta.json under
# /config/plugins/ inside the catalog Jellyfin container. Jellyfin composes the plugin folder
# name from the package name and version, and the package name contains a space, so the
# installed version is read this way rather than by guessing the folder name.
installed_versions() {
	docker compose -f "$COMPOSE_FILE" exec -T catalog-jellyfin \
		sh -c 'for f in /config/plugins/*/meta.json; do [ -f "$f" ] && cat "$f"; done' |
		jq -r .version
}

# version_installed VERSION -> true once installed_versions contains exactly VERSION.
version_installed() {
	installed_versions | grep -qx "$1"
}

# active_installed_version -> prints the version of the one plugin folder under
# /config/plugins/ whose meta.json reports status "Active". An update leaves the superseded
# version's folder and meta.json on disk with status "Superseded" rather than deleting it, so
# this is the source of truth for which version Jellyfin currently uses, not installed_versions.
active_installed_version() {
	docker compose -f "$COMPOSE_FILE" exec -T catalog-jellyfin \
		sh -c 'for f in /config/plugins/*/meta.json; do [ -f "$f" ] && cat "$f"; done' |
		jq -rs 'map(select(.status == "Active")) | .[0].version'
}

# catalog_login -> re-authenticates against the catalog Jellyfin server and exports CATALOG_TOKEN.
# Called once in setup_file and again after every restart. Exported because bats runs setup_file
# and each @test in separate processes, the same reason setup_suite.bash exports JF_TOKEN.
catalog_login() {
	CATALOG_TOKEN="$(login_token "$CATALOG_JELLYFIN" catalogadmin catalog-jf-pass)"
	export CATALOG_TOKEN
}

# catalog_jellyfin_restart -> stops and starts the catalog Jellyfin container (not `restart`,
# matching jellyfin_stop/jellyfin_start's documented reason: stopping lets Jellyfin close its
# SQLite connections before the process ends), waits for it to become healthy, and logs in
# again so CATALOG_TOKEN is valid for the restarted server.
catalog_jellyfin_restart() {
	docker compose -f "$COMPOSE_FILE" stop catalog-jellyfin >&3 2>&1
	docker compose -f "$COMPOSE_FILE" start catalog-jellyfin >&3 2>&1
	wait_until "catalog Jellyfin" catalog_jellyfin_ready
	catalog_login
}

setup_file() {
	load helpers
	REPO_ROOT="$(cd "$E2E_DIR/.." && pwd)"

	mkdir -p "$CATALOG_ARTIFACT_DIR"
	local changelog_fixture="$CATALOG_ARTIFACT_DIR/CHANGELOG.md"
	{
		echo "# Changelog"
		echo
		echo "## [$LOWER_VERSION] - 2026-09-01"
		echo
		echo "- The first version this catalog test builds."
		echo
		echo "## [$HIGHER_VERSION] - 2026-09-02"
		echo
		echo "- The second version this catalog test builds and updates to."
	} >"$changelog_fixture"

	local version
	for version in "$LOWER_VERSION" "$HIGHER_VERSION"; do
		PACKAGE_VERSION="$version" \
			CHANGELOG_PATH="$changelog_fixture" \
			PACKAGE_OUTPUT_DIR="$CATALOG_ARTIFACT_DIR/v$version" \
			RELEASE_URL_BASE="http://catalog-manifest" \
			"$REPO_ROOT/scripts/package.sh" build >&3
	done

	# scripts/manifest.sh merge is the same merge the Pages rebuild performs, so the document
	# under test below is the real one rather than a hand-written fixture.
	"$REPO_ROOT/scripts/manifest.sh" merge \
		"$CATALOG_ARTIFACT_DIR/v$LOWER_VERSION" "$CATALOG_ARTIFACT_DIR/v$HIGHER_VERSION" \
		>"$CATALOG_ARTIFACT_DIR/manifest.json"

	docker compose -f "$COMPOSE_FILE" --profile catalog up -d catalog-manifest catalog-jellyfin >&3 2>&1
	wait_until "catalog Jellyfin" catalog_jellyfin_ready

	complete_startup_wizard "$CATALOG_JELLYFIN" catalogadmin catalog-jf-pass
	catalog_login
}

teardown_file() {
	load helpers
	docker compose -f "$COMPOSE_FILE" rm -sfv catalog-manifest catalog-jellyfin >&3 2>&1
}

setup() {
	load helpers
	# Each @test runs in its own process, so a token exported inside one test's body (as
	# catalog_jellyfin_restart does) is not visible to the next test. Re-authenticate here
	# instead of relying on it: by the time any @test runs, setup_file's own login has already
	# created the admin account, so this is always safe to call.
	catalog_login
}

@test "the manifest is served as JSON" {
	run curl -sI "http://127.0.0.1:$CATALOG_MANIFEST_PORT/manifest.json"
	[ "$status" -eq 0 ]
	[[ "$output" == *"application/json"* ]]
}

@test "the recorded checksum in the served manifest is each zip's real checksum" {
	local version zip checksum recorded
	for version in "$LOWER_VERSION" "$HIGHER_VERSION"; do
		zip="$CATALOG_ARTIFACT_DIR/v$version/jellyfin-plugin-emby-auth_$version.zip"
		checksum="$(openssl dgst -md5 -r "$zip" | cut -d' ' -f1)"
		recorded="$(curl -sf "http://127.0.0.1:$CATALOG_MANIFEST_PORT/manifest.json" |
			jq -r --arg v "$version" '.[0].versions[] | select(.version == $v) | .checksum')"
		[ "$checksum" = "$recorded" ]
	done
}

@test "adding the repository works" {
	local repos
	api POST "$CATALOG_JELLYFIN/Repositories" "$CATALOG_TOKEN" \
		'[{"Name":"emby-auth-e2e","Url":"http://catalog-manifest/manifest.json","Enabled":true}]' >/dev/null
	repos="$(api GET "$CATALOG_JELLYFIN/Repositories" "$CATALOG_TOKEN")"
	[ "$(jq 'length' <<<"$repos")" = "1" ]
	[ "$(jq -r '.[0].Url' <<<"$repos")" = "http://catalog-manifest/manifest.json" ]
}

@test "the catalog lists both versions" {
	local packages package
	packages="$(api GET "$CATALOG_JELLYFIN/Packages" "$CATALOG_TOKEN")"
	package="$(jq --arg g "$(plugin_id_nodash)" '[.[] | select(.guid == $g)]' <<<"$packages")"
	[ "$(jq 'length' <<<"$package")" = "1" ]
	[ "$(jq -r ".[0].versions[].version" <<<"$package" | sort -V | tr '\n' ' ')" = "$LOWER_VERSION $HIGHER_VERSION " ]
}

@test "installing the lower version works" {
	local name_encoded
	name_encoded="$(jq -rn --arg n "$PLUGIN_NAME" '$n | @uri')"
	run status POST \
		"$CATALOG_JELLYFIN/Packages/Installed/$name_encoded?version=$LOWER_VERSION&repositoryUrl=http://catalog-manifest/manifest.json" \
		"$CATALOG_TOKEN"
	[ "$output" = "204" ]
	wait_until "the lower version to install" version_installed "$LOWER_VERSION"
}

@test "installing the same version twice is idempotent" {
	local name_encoded
	name_encoded="$(jq -rn --arg n "$PLUGIN_NAME" '$n | @uri')"
	status POST \
		"$CATALOG_JELLYFIN/Packages/Installed/$name_encoded?version=$LOWER_VERSION&repositoryUrl=http://catalog-manifest/manifest.json" \
		"$CATALOG_TOKEN" >/dev/null
	wait_until "the lower version to still be the only installed version" version_installed "$LOWER_VERSION"
	[ "$(installed_versions | wc -l | tr -d ' ')" = "1" ]
}

@test "the server loads the installed version after a restart" {
	catalog_jellyfin_restart
	local plugins
	plugins="$(api GET "$CATALOG_JELLYFIN/Plugins" "$CATALOG_TOKEN")"
	[ "$(jq -r --arg g "$(plugin_id_nodash)" '.[] | select(.Id == $g) | .Status' <<<"$plugins")" = "Active" ]
	[ "$(active_installed_version)" = "$LOWER_VERSION" ]
}

@test "updating with no version parameter resolves and installs the higher version" {
	local name_encoded plugins
	name_encoded="$(jq -rn --arg n "$PLUGIN_NAME" '$n | @uri')"
	status POST \
		"$CATALOG_JELLYFIN/Packages/Installed/$name_encoded?repositoryUrl=http://catalog-manifest/manifest.json" \
		"$CATALOG_TOKEN" >/dev/null
	wait_until "the higher version to install" version_installed "$HIGHER_VERSION"

	catalog_jellyfin_restart
	plugins="$(api GET "$CATALOG_JELLYFIN/Plugins" "$CATALOG_TOKEN")"
	[ "$(jq -r --arg g "$(plugin_id_nodash)" '.[] | select(.Id == $g) | .Status' <<<"$plugins")" = "Active" ]
	[ "$(active_installed_version)" = "$HIGHER_VERSION" ]
}
