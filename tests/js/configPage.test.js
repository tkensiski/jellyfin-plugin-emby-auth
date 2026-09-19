'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const {
  buildDom,
  flush,
  firePageshow,
  fireSubmit,
  clickSave,
  clickRunMigration,
  listItemTexts,
  settingsStatus,
  pageStyleRules,
} = require('./testHelpers');

const LOAD_FAILURE_MESSAGE =
  'Jellyfin cannot load the plugin settings. Save is turned off until the settings load. See the Jellyfin log.';
const SAVE_FAILURE_MESSAGE = 'Jellyfin cannot save the plugin settings. See the Jellyfin log.';

test('a failed settings load shows a message on the page', async () => {
  const { document, window } = buildDom({ getConfigFails: true });

  firePageshow(document, window);
  await flush();

  assert.equal(settingsStatus(document).textContent, LOAD_FAILURE_MESSAGE);
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
  assert.equal(status.children.length, 0);
  assert.equal(status.textContent.includes('sup3rsecret'), false);
  assert.equal(status.textContent.includes(apiKeySentinel), false);
});

test('a failed configuration update shows a message', async () => {
  const { document, window, dashboard } = buildDom({ updateConfigFails: true });
  const hideCallsBefore = dashboard.hideLoadingCalls;

  fireSubmit(document, window);
  await flush();

  assert.equal(settingsStatus(document).textContent, SAVE_FAILURE_MESSAGE);
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
  assert.equal(status.children.length, 0);
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
    { Name: 'alice', ReadyToMove: true },
    { Name: 'bob', ReadyToMove: false },
    { Name: 'carol', ReadyToMove: true },
  ];
  const { document, window } = buildDom({ users });

  firePageshow(document, window);
  await flush();

  assert.deepEqual(listItemTexts(document), [
    'alice: ready',
    'bob: needs one login while Emby runs',
    'carol: ready',
  ]);
});

test('two users with the same name each get their own entry', async () => {
  const users = [
    { Name: 'duplicate', ReadyToMove: true },
    { Name: 'duplicate', ReadyToMove: false },
  ];
  const { document, window } = buildDom({ users });

  firePageshow(document, window);
  await flush();

  assert.equal(listItemTexts(document).length, 2);
});

test('the list keeps the server order', async () => {
  const users = [
    { Name: 'zeta', ReadyToMove: true },
    { Name: 'alpha', ReadyToMove: true },
    { Name: 'mike', ReadyToMove: true },
  ];
  const { document, window } = buildDom({ users });

  firePageshow(document, window);
  await flush();

  assert.deepEqual(listItemTexts(document), ['zeta: ready', 'alpha: ready', 'mike: ready']);
});

test('an empty user list renders nothing and says so', async () => {
  const { document, window } = buildDom({ users: [] });

  firePageshow(document, window);
  await flush();

  assert.equal(listItemTexts(document).length, 0);
  assert.equal(
    document.querySelector('#EmbyAuthMigrationSummary').textContent,
    'No users are on the Emby login method.',
  );
});

test('loading the migration status twice does not accumulate entries', async () => {
  const users = [
    { Name: 'alice', ReadyToMove: true },
    { Name: 'bob', ReadyToMove: false },
    { Name: 'carol', ReadyToMove: true },
  ];
  const { document, window } = buildDom({ users });

  firePageshow(document, window);
  await flush();
  firePageshow(document, window);
  await flush();

  assert.equal(listItemTexts(document).length, 3);
});

test('a user name containing markup characters renders as text', async () => {
  const users = [{ Name: '<b>hacker</b>', ReadyToMove: true }];
  const { document, window } = buildDom({ users });

  firePageshow(document, window);
  await flush();

  const item = document.querySelector('#EmbyAuthMigrationUsers li');
  assert.equal(item.children.length, 0);
  assert.equal(item.textContent, '<b>hacker</b>: ready');
});

test('a failed migration status shows its message', async () => {
  const { document, window } = buildDom({ migrationStatusFails: true });

  firePageshow(document, window);
  await flush();

  assert.match(
    document.querySelector('#EmbyAuthMigrationSummary').textContent,
    /cannot read the migration status/,
  );
});

test('Run migration now sends the request and reports it', async (t) => {
  const { document, window, api, close } = buildDom({});
  t.after(() => close());

  assert.ok(document.querySelector('#EmbyAuthRunMigration'));

  clickRunMigration(document, window);
  await flush();

  assert.equal(api.runMigrationCalls, 1);
  assert.match(document.querySelector('#EmbyAuthMigrationSummary').textContent, /The migration runs/);
});

test('a failed Run migration now shows its message', async (t) => {
  const { document, window, close } = buildDom({ runMigrationFails: true });
  t.after(() => close());

  clickRunMigration(document, window);
  await flush();

  assert.match(
    document.querySelector('#EmbyAuthMigrationSummary').textContent,
    /did not start the migration/,
  );
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
    { Name: 'alice', ReadyToMove: true },
    { Name: 'bob', ReadyToMove: false },
    { Name: 'carol', ReadyToMove: true },
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
