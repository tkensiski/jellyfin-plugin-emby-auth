'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { buildDom, flush, firePageshow } = require('./testHelpers');

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
