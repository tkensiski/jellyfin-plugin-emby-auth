'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const {
  buildDom,
  flush,
  tickPoll,
  firePageshow,
  fireSubmit,
  clickSave,
  clickRunMigration,
  listItemTexts,
  settingsStatus,
  pageStyleRules,
  messageChildren,
  selectOptions,
  DEFAULT_PROVIDER_ID,
  REMAIN_ON_EMBY_LOGIN_METHOD,
} = require('./testHelpers');

const LOAD_FAILURE_MESSAGE =
  'Jellyfin cannot load the plugin settings. Save is turned off until the settings load. See the Jellyfin log.';
const SAVE_FAILURE_MESSAGE = 'Jellyfin cannot save the plugin settings. See the Jellyfin log.';
const MIGRATION_READ_FAILURE_MESSAGE =
  'Jellyfin cannot read the migration status. See the Jellyfin log.';
const MIGRATION_START_FAILURE_MESSAGE = 'Jellyfin did not start the migration. See the Jellyfin log.';
const MIGRATION_STARTED_MESSAGE = 'The migration runs. This list updates until it finishes.';
const NO_USERS_MESSAGE = 'No users are on the Emby login method.';
const EXPECTED_ICON = [{ tagName: 'SPAN', classes: ['material-icons', 'warning'], ariaHidden: 'true', text: '' }];
const RECORDS_UNAVAILABLE_PATH_PATTERN = /[\\/]/;

test('a failed settings load shows a message on the page', async () => {
  const { document, window } = buildDom({ getConfigFails: true });

  firePageshow(document, window);
  await flush();

  assert.equal(settingsStatus(document).textContent, LOAD_FAILURE_MESSAGE);
  assert.deepEqual(messageChildren(settingsStatus(document)), EXPECTED_ICON);
  assert.equal(
    document.querySelector('#EmbyAuthMigrationSummary').textContent.includes('plugin settings'),
    false,
  );
  assert.equal(document.querySelector('.button-submit').disabled, true);
});

test('Save is turned off after a failed settings load', async () => {
  const { document, window, api } = buildDom({ getConfigFails: true });

  firePageshow(document, window);
  await flush();

  assert.equal(document.querySelector('.button-submit').disabled, true);

  clickSave(document);
  await flush();

  assert.deepEqual(api.updateCalls, []);
});

test('activating Save twice after a failed load still sends nothing', async () => {
  const { document, window, api } = buildDom({ getConfigFails: true });

  firePageshow(document, window);
  await flush();

  clickSave(document);
  clickSave(document);
  await flush();

  assert.deepEqual(api.updateCalls, []);
});

test('a later successful load turns Save back on', async () => {
  const { document, window, api } = buildDom({
    getConfigFails: true,
    config: {
      EmbyServerUrl: 'http://emby.example.test:8096',
      EmbyApiKey: 'recovered-api-key',
      MigrationMode: 'MoveAfterFirstLogin',
      AccountAccess: 'CopyEmbyRemoteAccess',
    },
  });

  firePageshow(document, window);
  await flush();
  assert.equal(document.querySelector('.button-submit').disabled, true);

  api.getConfigFails = false;
  firePageshow(document, window);
  await flush();

  assert.equal(document.querySelector('#EmbyServerUrl').value, 'http://emby.example.test:8096');
  assert.equal(document.querySelector('#EmbyApiKey').value, 'recovered-api-key');
  assert.equal(document.querySelector('#MigrationMode').value, 'MoveAfterFirstLogin');
  assert.equal(document.querySelector('#AccountAccess').value, 'CopyEmbyRemoteAccess');
  assert.equal(document.querySelector('.button-submit').disabled, false);
});

test('the load-failure message repeats neither the configured URL nor the API key', async () => {
  const secretUrl = 'http://admin:sup3rsecret@emby.example.test:8096';
  const apiKeySentinel = 'sentinel-api-key-9f8e7d';
  const rejection = new Error(`request to ${secretUrl} failed with key ${apiKeySentinel}`);
  const { document, window } = buildDom({ getConfigFails: rejection });

  firePageshow(document, window);
  await flush();

  const status = settingsStatus(document);
  assert.deepEqual(messageChildren(status), EXPECTED_ICON);
  assert.equal(status.textContent, LOAD_FAILURE_MESSAGE);
  assert.equal(status.textContent.includes('sup3rsecret'), false);
  assert.equal(status.textContent.includes(apiKeySentinel), false);
});

test('a failed configuration update shows a message', async () => {
  const { document, window, dashboard } = buildDom({ updateConfigFails: true });
  const hideCallsBefore = dashboard.hideLoadingCalls;

  fireSubmit(document, window);
  await flush();

  assert.equal(settingsStatus(document).textContent, SAVE_FAILURE_MESSAGE);
  assert.deepEqual(messageChildren(settingsStatus(document)), EXPECTED_ICON);
  assert.ok(dashboard.hideLoadingCalls > hideCallsBefore);
});

test('a failed re-fetch during save shows the same message', async () => {
  const { document, window, api, dashboard } = buildDom({ getConfigFailsFromCall: 2 });

  firePageshow(document, window);
  await flush();

  const hideCallsBefore = dashboard.hideLoadingCalls;
  fireSubmit(document, window);
  await flush();

  assert.match(settingsStatus(document).textContent, /cannot save the plugin settings/);
  assert.deepEqual(api.updateCalls, []);
  assert.ok(dashboard.hideLoadingCalls > hideCallsBefore);
});

test('the save-failure message repeats neither the configured URL nor the API key', async () => {
  const secretUrl = 'http://admin:sup3rsecret@emby.example.test:8096';
  const apiKeySentinel = 'sentinel-api-key-9f8e7d';
  const rejection = new Error(`request to ${secretUrl} failed with key ${apiKeySentinel}`);
  const { document, window, dashboard } = buildDom({ updateConfigFails: rejection });

  document.querySelector('#EmbyServerUrl').value = secretUrl;
  document.querySelector('#EmbyApiKey').value = apiKeySentinel;

  fireSubmit(document, window);
  await flush();

  const status = settingsStatus(document);
  assert.deepEqual(messageChildren(status), EXPECTED_ICON);
  assert.equal(status.textContent, SAVE_FAILURE_MESSAGE);
  assert.equal(status.textContent.includes('sup3rsecret'), false);
  assert.equal(status.textContent.includes(apiKeySentinel), false);
  assert.ok(dashboard.hideLoadingCalls >= 1);
});

test('a successful load fills the four inputs', async () => {
  const { document, window } = buildDom({
    config: {
      EmbyServerUrl: 'http://emby.example.test:8096',
      EmbyApiKey: 'load-test-api-key',
      MigrationMode: 'KeepEmbyInCharge',
      AccountAccess: 'JellyfinDefaults',
    },
  });

  firePageshow(document, window);
  await flush();

  assert.equal(document.querySelector('#EmbyServerUrl').value, 'http://emby.example.test:8096');
  assert.equal(document.querySelector('#EmbyApiKey').value, 'load-test-api-key');
  assert.equal(document.querySelector('#MigrationMode').value, 'KeepEmbyInCharge');
  assert.equal(document.querySelector('#AccountAccess').value, 'JellyfinDefaults');
});

test('the migration list shows one entry per user', async () => {
  const users = [
    { Name: 'alice', State: 'Ready' },
    { Name: 'bob', State: 'NeedsEmbyLogin' },
    { Name: 'carol', State: 'Ready' },
  ];
  const { document, window } = buildDom({ users });

  firePageshow(document, window);
  await flush();

  assert.deepEqual(listItemTexts(document), [
    'alice: will move on the next run',
    'bob: needs one login while Emby runs',
    'carol: will move on the next run',
  ]);
});

test('two users with the same name each get their own entry', async () => {
  const users = [
    { Name: 'duplicate', State: 'Ready' },
    { Name: 'duplicate', State: 'NeedsEmbyLogin' },
  ];
  const { document, window } = buildDom({ users });

  firePageshow(document, window);
  await flush();

  assert.equal(listItemTexts(document).length, 2);
});

test('the list keeps the server order', async () => {
  const users = [
    { Name: 'zeta', State: 'Ready' },
    { Name: 'alpha', State: 'Ready' },
    { Name: 'mike', State: 'Ready' },
  ];
  const { document, window } = buildDom({ users });

  firePageshow(document, window);
  await flush();

  assert.deepEqual(listItemTexts(document), [
    'zeta: will move on the next run',
    'alpha: will move on the next run',
    'mike: will move on the next run',
  ]);
});

test('the migration list renders a distinct string for each of the four states', async () => {
  // Every user shares the same name, so uniqueness can only come from the state-derived text, not the name.
  const users = [
    { Name: 'user', State: 'Ready' },
    { Name: 'user', State: 'NeedsEmbyLogin' },
    { Name: 'user', State: 'NoPassword' },
    { Name: 'user', State: 'Unknown' },
  ];
  const { document, window } = buildDom({ users });

  firePageshow(document, window);
  await flush();

  const texts = listItemTexts(document);
  assert.equal(texts.length, 4);
  assert.equal(new Set(texts).size, 4);
});

test('an empty user list renders nothing and says so', async () => {
  const { document, window } = buildDom({ users: [] });

  firePageshow(document, window);
  await flush();

  assert.equal(listItemTexts(document).length, 0);
  assert.equal(document.querySelector('#EmbyAuthMigrationSummary').textContent, NO_USERS_MESSAGE);
  assert.deepEqual(
    messageChildren(document.querySelector('#EmbyAuthMigrationSummary')),
    [],
  );
});

test('loading the migration status twice does not accumulate entries', async () => {
  const users = [
    { Name: 'alice', State: 'Ready' },
    { Name: 'bob', State: 'NeedsEmbyLogin' },
    { Name: 'carol', State: 'Ready' },
  ];
  const { document, window } = buildDom({ users });

  firePageshow(document, window);
  await flush();
  firePageshow(document, window);
  await flush();

  assert.equal(listItemTexts(document).length, 3);
});

test('a user name containing markup characters renders as text', async () => {
  const users = [{ Name: '<b>hacker</b>', State: 'Ready' }];
  const { document, window } = buildDom({ users });

  firePageshow(document, window);
  await flush();

  const item = document.querySelector('#EmbyAuthMigrationUsers li');
  assert.equal(item.children.length, 0);
  assert.equal(item.textContent, '<b>hacker</b>: will move on the next run');
});

test('a failed migration status shows its message', async () => {
  const { document, window } = buildDom({ migrationStatusFails: true });

  firePageshow(document, window);
  await flush();

  const summary = document.querySelector('#EmbyAuthMigrationSummary');
  assert.equal(summary.textContent, MIGRATION_READ_FAILURE_MESSAGE);
  assert.deepEqual(messageChildren(summary), EXPECTED_ICON);
});

test('a records-unavailable migration status shows a warning naming the Jellyfin log', async () => {
  const { document, window } = buildDom({ recordsUnavailable: true });

  firePageshow(document, window);
  await flush();

  const summary = document.querySelector('#EmbyAuthMigrationSummary');
  assert.match(summary.textContent, /log/i);
  assert.deepEqual(messageChildren(summary), EXPECTED_ICON);
});

test('a records-available migration status shows no records-unavailable warning', async () => {
  const { document, window } = buildDom({ recordsUnavailable: false, users: [] });

  firePageshow(document, window);
  await flush();

  const summary = document.querySelector('#EmbyAuthMigrationSummary');
  assert.equal(summary.textContent, NO_USERS_MESSAGE);
  assert.deepEqual(messageChildren(summary), []);
});

test('the records-unavailable message contains no file system path', async () => {
  const { document, window } = buildDom({ recordsUnavailable: true });

  firePageshow(document, window);
  await flush();

  const summary = document.querySelector('#EmbyAuthMigrationSummary');
  assert.equal(RECORDS_UNAVAILABLE_PATH_PATTERN.test(summary.textContent), false);
});

test('Run migration now saves the picked target then sends the request and reports it', async (t) => {
  const { document, window, api, close } = buildDom({});
  t.after(() => close());

  assert.ok(document.querySelector('#EmbyAuthRunMigration'));

  firePageshow(document, window);
  await flush();

  clickRunMigration(document, window);
  await flush();

  assert.equal(api.updateCalls.length, 1);
  assert.equal(api.updateCalls[0].MigrationTarget, DEFAULT_PROVIDER_ID);
  assert.equal(api.runMigrationCalls, 1);
  const summary = document.querySelector('#EmbyAuthMigrationSummary');
  assert.equal(summary.textContent, MIGRATION_STARTED_MESSAGE);
  assert.deepEqual(messageChildren(summary), []);
});

test('a failed Run migration now shows its message', async (t) => {
  const { document, window, close } = buildDom({ runMigrationFails: true });
  t.after(() => close());

  firePageshow(document, window);
  await flush();

  clickRunMigration(document, window);
  await flush();

  const summary = document.querySelector('#EmbyAuthMigrationSummary');
  assert.equal(summary.textContent, MIGRATION_START_FAILURE_MESSAGE);
  assert.deepEqual(messageChildren(summary), EXPECTED_ICON);
});

test('Run migration now begins polling that issues a second request after one interval', async (t) => {
  const { document, window, api, interval, close } = buildDom({});
  t.after(() => close());

  firePageshow(document, window);
  await flush();

  clickRunMigration(document, window);
  await flush();
  const callsBefore = api.migrationStatusCalls;

  await tickPoll(interval);

  assert.equal(api.migrationStatusCalls, callsBefore + 1);
});

test('polling continues across three consecutive Running responses', async (t) => {
  const running = { State: 'Running', Progress: 0.1, LastEndTimeUtc: null, LastResult: null };
  const { document, window, api, interval, close } = buildDom({
    taskSequence: [running, running, running],
  });
  t.after(() => close());

  firePageshow(document, window);
  await flush();

  clickRunMigration(document, window);
  await flush();

  await tickPoll(interval, 3);
  const callsAfterThree = api.migrationStatusCalls;

  await tickPoll(interval);

  assert.equal(api.migrationStatusCalls, callsAfterThree + 1);
});

test('polling continues when Idle repeats the pre-run end time', async (t) => {
  const idleUnchanged = {
    State: 'Idle',
    Progress: null,
    LastEndTimeUtc: '2026-09-20T00:00:00Z',
    LastResult: 'Completed',
  };
  const { document, window, api, interval, close } = buildDom({ task: idleUnchanged });
  t.after(() => close());

  firePageshow(document, window);
  await flush();

  clickRunMigration(document, window);
  await flush();
  const callsBefore = api.migrationStatusCalls;

  await tickPoll(interval);

  assert.equal(api.migrationStatusCalls, callsBefore + 1);
});

test('polling stops once Idle reports a new end time', async (t) => {
  const before = {
    State: 'Idle',
    Progress: null,
    LastEndTimeUtc: '2026-09-20T00:00:00Z',
    LastResult: 'Completed',
  };
  const after = {
    State: 'Idle',
    Progress: null,
    LastEndTimeUtc: '2026-09-20T00:05:00Z',
    LastResult: 'Completed',
  };
  const { document, window, api, interval, close } = buildDom({ task: before });
  t.after(() => close());

  firePageshow(document, window);
  await flush();

  api.task = after;
  clickRunMigration(document, window);
  await flush();

  await tickPoll(interval);
  const callsAfterStop = api.migrationStatusCalls;

  await tickPoll(interval);

  assert.equal(api.migrationStatusCalls, callsAfterStop);
});

test('polling gives up after about 20 seconds when no run begins', async (t) => {
  const neverStarted = { State: 'Idle', Progress: null, LastEndTimeUtc: null, LastResult: null };
  const { document, window, api, interval, close } = buildDom({ task: neverStarted });
  t.after(() => close());

  firePageshow(document, window);
  await flush();

  clickRunMigration(document, window);
  await flush();

  await tickPoll(interval, 10);

  const summary = document.querySelector('#EmbyAuthMigrationSummary');
  assert.match(summary.textContent, /log/i);

  const callsAfterTimeout = api.migrationStatusCalls;
  await tickPoll(interval);

  assert.equal(api.migrationStatusCalls, callsAfterTimeout);
});

test('the page never stacks a second migration-status request while one is pending', async (t) => {
  const running = { State: 'Running', Progress: 0.2, LastEndTimeUtc: null, LastResult: null };
  const { document, window, api, interval, close } = buildDom({ task: running });
  t.after(() => close());

  // The page's own load succeeds and starts polling because the task is already Running;
  // only the poll's own subsequent requests hang, isolating the overlap guard under test.
  firePageshow(document, window);
  await flush();

  api.migrationStatusHangs = true;
  const callsBeforePoll = api.migrationStatusCalls;

  await tickPoll(interval, 2);

  assert.equal(api.migrationStatusCalls, callsBeforePoll + 1);
});

test('polling starts on load when the task is already running', async (t) => {
  const running = { State: 'Running', Progress: 0.4, LastEndTimeUtc: null, LastResult: null };
  const { document, window, api, interval, close } = buildDom({ task: running });
  t.after(() => close());

  firePageshow(document, window);
  await flush();
  const callsBefore = api.migrationStatusCalls;

  await tickPoll(interval);

  assert.equal(api.migrationStatusCalls, callsBefore + 1);
});

test('pagehide stops polling', async (t) => {
  const { document, window, api, interval, close } = buildDom({});
  t.after(() => close());

  firePageshow(document, window);
  await flush();

  clickRunMigration(document, window);
  await flush();

  document.querySelector('#EmbyAuthConfigPage').dispatchEvent(new window.Event('pagehide'));

  const callsBefore = api.migrationStatusCalls;
  await tickPoll(interval);

  assert.equal(api.migrationStatusCalls, callsBefore);
});

test('the section reports the migration task as absent when the response carries no task', async (t) => {
  const { document, window, close } = buildDom({ task: null });
  t.after(() => close());

  firePageshow(document, window);
  await flush();

  const summary = document.querySelector('#EmbyAuthMigrationSummary');
  assert.match(summary.textContent, /registered/i);
  assert.match(summary.textContent, /log/i);
});

test('the migration target dropdown orders Default, Remain, then the rest', async (t) => {
  const availableTargets = [
    { Name: 'Default', Id: DEFAULT_PROVIDER_ID },
    { Name: 'JellyfinSecurity', Id: 'jellyfin-security-provider-id' },
    { Name: 'AnotherMethod', Id: 'another-provider-id' },
  ];
  const { document, window, close } = buildDom({ availableTargets });
  t.after(() => close());

  firePageshow(document, window);
  await flush();

  const options = selectOptions(document.querySelector('#EmbyAuthMigrationTarget'));
  assert.deepEqual(
    options.map((option) => option.value),
    [DEFAULT_PROVIDER_ID, REMAIN_ON_EMBY_LOGIN_METHOD, 'jellyfin-security-provider-id', 'another-provider-id'],
  );
  assert.equal(options[0].text, 'Move to Default');
  assert.equal(options[1].text, 'Remain on Emby Login');
  assert.equal(options[2].text, 'Move to JellyfinSecurity');
});

test('with only Default enabled the dropdown offers exactly two options', async (t) => {
  const availableTargets = [{ Name: 'Default', Id: DEFAULT_PROVIDER_ID }];
  const { document, window, close } = buildDom({ availableTargets });
  t.after(() => close());

  firePageshow(document, window);
  await flush();

  const options = selectOptions(document.querySelector('#EmbyAuthMigrationTarget'));
  assert.equal(options.length, 2);
});

test('an unavailable saved target selects nothing and names the Jellyfin log', async (t) => {
  const { document, window, close } = buildDom({
    config: { MigrationTarget: 'a-removed-plugin-provider-id' },
  });
  t.after(() => close());

  firePageshow(document, window);
  await flush();

  const select = document.querySelector('#EmbyAuthMigrationTarget');
  assert.equal(select.value, '');
  const summary = document.querySelector('#EmbyAuthMigrationSummary');
  assert.match(summary.textContent, /log/i);
});

test('Run migration now refuses to start when the saved target is unavailable', async (t) => {
  const { document, window, api, close } = buildDom({
    config: { MigrationTarget: 'a-removed-plugin-provider-id' },
  });
  t.after(() => close());

  firePageshow(document, window);
  await flush();

  clickRunMigration(document, window);
  await flush();

  assert.deepEqual(api.updateCalls, []);
  assert.equal(api.runMigrationCalls, 0);
});

test('picking a target and clicking Run saves it before queueing the task', async (t) => {
  const availableTargets = [
    { Name: 'Default', Id: DEFAULT_PROVIDER_ID },
    { Name: 'JellyfinSecurity', Id: 'jellyfin-security-provider-id' },
  ];
  const { document, window, api, close } = buildDom({ availableTargets });
  t.after(() => close());

  firePageshow(document, window);
  await flush();

  document.querySelector('#EmbyAuthMigrationTarget').value = 'jellyfin-security-provider-id';
  clickRunMigration(document, window);
  await flush();

  assert.equal(api.updateCalls.length, 1);
  assert.equal(api.updateCalls[0].MigrationTarget, 'jellyfin-security-provider-id');
  assert.equal(api.runMigrationCalls, 1);
});

test('a failed save before Run stops the migration from starting', async (t) => {
  const { document, window, api, close } = buildDom({ updateConfigFails: true });
  t.after(() => close());

  firePageshow(document, window);
  await flush();

  clickRunMigration(document, window);
  await flush();

  assert.equal(api.runMigrationCalls, 0);
  const summary = document.querySelector('#EmbyAuthMigrationSummary');
  assert.match(summary.textContent, /log/i);
});

test('a target name with markup characters renders as an option with text only', async (t) => {
  const availableTargets = [
    { Name: 'Default', Id: DEFAULT_PROVIDER_ID },
    { Name: '<b>hacker</b>', Id: 'hacker-provider-id' },
  ];
  const { document, window, close } = buildDom({ availableTargets });
  t.after(() => close());

  firePageshow(document, window);
  await flush();

  const option = Array.from(document.querySelector('#EmbyAuthMigrationTarget').options).find(
    (candidate) => candidate.value === 'hacker-provider-id',
  );
  assert.equal(option.children.length, 0);
  assert.ok(option.textContent.endsWith('<b>hacker</b>'));
});

test('submitting the settings form leaves the migration target unchanged', async (t) => {
  const { document, window, api } = buildDom({});

  firePageshow(document, window);
  await flush();

  fireSubmit(document, window);
  await flush();

  assert.equal(api.updateCalls.length, 1);
  assert.equal(api.updateCalls[0].MigrationTarget, DEFAULT_PROVIDER_ID);
});

test('a no-saved-password account with the Default target gets the blank-password warning', async (t) => {
  const users = [{ Name: 'nopass', State: 'NoPassword' }];
  const { document, window, close } = buildDom({ users });
  t.after(() => close());

  firePageshow(document, window);
  await flush();

  const warning = document.querySelector('#EmbyAuthMigrationWarning');
  assert.match(warning.textContent, /blank password/i);
  assert.match(warning.textContent, /nopass/);
});

test('a no-saved-password account with another target gets the cannot-tell warning', async (t) => {
  const users = [{ Name: 'nopass', State: 'NoPassword' }];
  const availableTargets = [
    { Name: 'Default', Id: DEFAULT_PROVIDER_ID },
    { Name: 'JellyfinSecurity', Id: 'jellyfin-security-provider-id' },
  ];
  const { document, window, close } = buildDom({
    users,
    availableTargets,
    config: { MigrationTarget: 'jellyfin-security-provider-id' },
  });
  t.after(() => close());

  firePageshow(document, window);
  await flush();

  const warning = document.querySelector('#EmbyAuthMigrationWarning');
  assert.match(warning.textContent, /cannot tell|does not know/i);
  assert.doesNotMatch(warning.textContent, /blank password/i);
});

test('a no-saved-password account with the Remain target gets no warning', async (t) => {
  const users = [{ Name: 'nopass', State: 'NoPassword' }];
  const { document, window, close } = buildDom({
    users,
    config: { MigrationTarget: REMAIN_ON_EMBY_LOGIN_METHOD },
  });
  t.after(() => close());

  firePageshow(document, window);
  await flush();

  const warning = document.querySelector('#EmbyAuthMigrationWarning');
  assert.equal(warning.textContent, '');
});

test('no no-saved-password accounts means no warning, whatever the target', async (t) => {
  const users = [{ Name: 'ready1', State: 'Ready' }];
  const { document, window, close } = buildDom({ users });
  t.after(() => close());

  firePageshow(document, window);
  await flush();

  const warning = document.querySelector('#EmbyAuthMigrationWarning');
  assert.equal(warning.textContent, '');
});

test('the no-saved-password warning never claims the plugin refuses the move', async (t) => {
  const users = [{ Name: 'nopass', State: 'NoPassword' }];
  const { document, window, close } = buildDom({ users });
  t.after(() => close());

  firePageshow(document, window);
  await flush();

  const warning = document.querySelector('#EmbyAuthMigrationWarning');
  assert.doesNotMatch(warning.textContent, /refuse|refuses|will not move|blocks/i);
});

test('the password-set dropdown defaults to Same as the migration target', async (t) => {
  const { document, window, close } = buildDom({});
  t.after(() => close());

  firePageshow(document, window);
  await flush();

  const options = selectOptions(document.querySelector('#PasswordSetTarget'));
  assert.equal(options[0].value, '');
  assert.equal(options[0].text, 'Same as the migration target');
  assert.equal(options[0].selected, true);
});

test('submitting the settings form saves the picked password-set target', async (t) => {
  const availableTargets = [
    { Name: 'Default', Id: DEFAULT_PROVIDER_ID },
    { Name: 'JellyfinSecurity', Id: 'jellyfin-security-provider-id' },
  ];
  const { document, window, api } = buildDom({ availableTargets });

  firePageshow(document, window);
  await flush();

  document.querySelector('#PasswordSetTarget').value = 'jellyfin-security-provider-id';
  fireSubmit(document, window);
  await flush();

  assert.equal(api.updateCalls.length, 1);
  assert.equal(api.updateCalls[0].PasswordSetTarget, 'jellyfin-security-provider-id');
});

test('the settings status sits with the Save control', () => {
  const { document, window } = buildDom({});

  const status = settingsStatus(document);
  const form = document.querySelector('#EmbyAuthConfigForm');
  const verticalSection = document.querySelector('.verticalSection');
  const saveButton = document.querySelector('.button-submit');

  assert.ok(status, 'expected #EmbyAuthSettingsStatus to exist');
  assert.equal(form.contains(status), true);
  assert.equal(verticalSection.contains(status), false);
  assert.equal(
    Boolean(
      // eslint-disable-next-line no-bitwise
      saveButton.compareDocumentPosition(status) & window.Node.DOCUMENT_POSITION_FOLLOWING,
    ),
    true,
  );
});

test('a disabled Save is marked and dimmed', async () => {
  const { document, window } = buildDom({ getConfigFails: true });

  firePageshow(document, window);
  await flush();

  const saveButton = document.querySelector('.button-submit');
  assert.equal(saveButton.hasAttribute('disabled'), true);

  const disabledRule = pageStyleRules(document).find(
    (rule) => rule.selectorText && rule.selectorText.includes('.button-submit[disabled]'),
  );
  assert.ok(disabledRule, 'expected a page style rule targeting .button-submit[disabled]');

  const opacity = Number.parseFloat(disabledRule.style.opacity);
  assert.ok(opacity > 0 && opacity < 1);
  assert.equal(disabledRule.style.cursor, 'default');
});

test('a successful load clears a stale settings failure', async () => {
  const { document, window, api } = buildDom({ getConfigFails: true });

  firePageshow(document, window);
  await flush();
  assert.equal(settingsStatus(document).textContent, LOAD_FAILURE_MESSAGE);

  api.getConfigFails = false;
  firePageshow(document, window);
  await flush();

  assert.equal(settingsStatus(document).textContent, '');
  assert.deepEqual(messageChildren(settingsStatus(document)), []);
  assert.equal(document.querySelector('.button-submit').disabled, false);
});

test('attempting a save disables Save at once', async () => {
  const { document, window } = buildDom({});

  firePageshow(document, window);
  await flush();

  fireSubmit(document, window);
  assert.equal(document.querySelector('.button-submit').disabled, true);

  await flush();
});

test('Save stays off after a failed save', async () => {
  const { document, window, api } = buildDom({ updateConfigFails: true });

  fireSubmit(document, window);
  await flush();
  assert.equal(document.querySelector('.button-submit').disabled, true);

  const callsBefore = api.updateCalls.length;
  clickSave(document);
  clickSave(document);
  await flush();

  assert.equal(api.updateCalls.length, callsBefore);
});

test('a successful save turns Save back on', async () => {
  const { document, window, api, dashboard } = buildDom({});

  firePageshow(document, window);
  await flush();
  fireSubmit(document, window);
  await flush();

  assert.equal(api.updateCalls.length, 1);
  assert.equal(dashboard.updateResults.length, 1);
  assert.equal(document.querySelector('.button-submit').disabled, false);
});

test('a failed load clears the migration list', async () => {
  const users = [
    { Name: 'alice', State: 'Ready' },
    { Name: 'bob', State: 'NeedsEmbyLogin' },
    { Name: 'carol', State: 'Ready' },
  ];
  const { document, window, api } = buildDom({ users });

  firePageshow(document, window);
  await flush();
  assert.equal(listItemTexts(document).length, 3);

  api.getConfigFails = true;
  firePageshow(document, window);
  await flush();

  assert.equal(listItemTexts(document).length, 0);
  assert.equal(document.querySelector('#EmbyAuthMigrationSummary').textContent, '');
});

test('a plain migration message replaces a stale warning icon', async () => {
  const users = [
    { Name: 'alice', State: 'Ready' },
    { Name: 'bob', State: 'NeedsEmbyLogin' },
  ];
  const { document, window, api } = buildDom({ users, migrationStatusFails: true });

  firePageshow(document, window);
  await flush();

  const summary = document.querySelector('#EmbyAuthMigrationSummary');
  assert.deepEqual(messageChildren(summary), EXPECTED_ICON);

  api.migrationStatusFails = false;
  firePageshow(document, window);
  await flush();

  assert.deepEqual(messageChildren(summary), []);
  assert.equal(
    summary.textContent,
    'Users on the Emby login method: 2. Ready: 1. Needs an Emby login: 1. No saved password: 0. Unknown: 0.',
  );
});

test('a repeated settings failure shows only one icon', async () => {
  const { document, window } = buildDom({ getConfigFails: true });

  firePageshow(document, window);
  await flush();
  firePageshow(document, window);
  await flush();

  const status = settingsStatus(document);
  assert.deepEqual(messageChildren(status), EXPECTED_ICON);
  assert.equal(status.textContent, LOAD_FAILURE_MESSAGE);
});

test('a repeated migration failure shows only one icon', async () => {
  const { document, window } = buildDom({ migrationStatusFails: true });

  firePageshow(document, window);
  await flush();
  firePageshow(document, window);
  await flush();

  const summary = document.querySelector('#EmbyAuthMigrationSummary');
  assert.deepEqual(messageChildren(summary), EXPECTED_ICON);
  assert.equal(summary.textContent, MIGRATION_READ_FAILURE_MESSAGE);
});

test('a second save attempt clears the stale failure icon at once', async () => {
  const { document, window } = buildDom({ updateConfigFails: true });

  fireSubmit(document, window);
  await flush();

  const status = settingsStatus(document);
  assert.deepEqual(messageChildren(status), EXPECTED_ICON);

  fireSubmit(document, window);

  assert.deepEqual(messageChildren(status), []);
  assert.equal(status.textContent, '');

  await flush();
});

test('the warning icon is sized and spaced for inline text', () => {
  const { document } = buildDom({});

  const iconRule = pageStyleRules(document).find(
    (rule) => rule.selectorText && rule.selectorText.includes('.material-icons'),
  );

  assert.ok(iconRule, 'expected a page style rule targeting .material-icons');
  assert.notEqual(iconRule.style.fontSize, '');
  assert.notEqual(iconRule.style.verticalAlign, '');
  assert.ok(Number.parseFloat(iconRule.style.marginRight) > 0);
});
