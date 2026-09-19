'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { buildDom, flush, firePageshow, fireSubmit, clickSave } = require('./testHelpers');

test('a failed settings load shows a message on the page', async () => {
  const { document, window } = buildDom({ getConfigFails: true });

  firePageshow(document, window);
  await flush();

  assert.match(
    document.querySelector('#EmbyAuthMigrationSummary').textContent,
    /cannot load the plugin settings/,
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

  const summary = document.querySelector('#EmbyAuthMigrationSummary');
  assert.equal(summary.children.length, 0);
  assert.equal(summary.textContent.includes('sup3rsecret'), false);
  assert.equal(summary.textContent.includes(apiKeySentinel), false);
});

test('a failed configuration update shows a message', async () => {
  const { document, window, dashboard } = buildDom({ updateConfigFails: true });
  const hideCallsBefore = dashboard.hideLoadingCalls;

  fireSubmit(document, window);
  await flush();

  assert.match(
    document.querySelector('#EmbyAuthMigrationSummary').textContent,
    /cannot save the plugin settings/,
  );
  assert.ok(dashboard.hideLoadingCalls > hideCallsBefore);
});

test('a failed re-fetch during save shows the same message', async () => {
  const { document, window, api, dashboard } = buildDom({ getConfigFailsFromCall: 2 });

  firePageshow(document, window);
  await flush();

  const hideCallsBefore = dashboard.hideLoadingCalls;
  fireSubmit(document, window);
  await flush();

  assert.match(
    document.querySelector('#EmbyAuthMigrationSummary').textContent,
    /cannot save the plugin settings/,
  );
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

  const summary = document.querySelector('#EmbyAuthMigrationSummary');
  assert.equal(summary.children.length, 0);
  assert.equal(summary.textContent.includes('sup3rsecret'), false);
  assert.equal(summary.textContent.includes(apiKeySentinel), false);
  assert.ok(dashboard.hideLoadingCalls >= 1);
});
