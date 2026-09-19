'use strict';

const path = require('node:path');
const fs = require('node:fs');
const { JSDOM } = require('jsdom');

const PAGE_PATH = path.join(
  __dirname,
  '..',
  '..',
  'src',
  'Jellyfin.Plugin.EmbyAuth',
  'Configuration',
  'configPage.html',
);

const DEFAULT_CONFIG = {
  EmbyServerUrl: 'http://emby.example.test:8096',
  EmbyApiKey: 'default-api-key',
  MigrationMode: 'KeepEmbyInCharge',
  AccountAccess: 'NoLibraries',
};

/**
 * Builds a rejection value for a stub method controlled by a failure flag.
 *
 * A flag of `true` rejects with a generic Error. A flag that is already an
 * Error instance rejects with that exact value, so a test can assert the
 * page never echoes it.
 *
 * @param {boolean | Error} flag - the failure flag.
 * @param {string} genericMessage - the message for the generic Error.
 * @returns {Error} the value to reject with.
 */
function rejectionFor(flag, genericMessage) {
  return flag instanceof Error ? flag : new Error(genericMessage);
}

/**
 * Builds a stub `ApiClient` global matching the members `configPage.html` calls.
 *
 * Each failure flag can be switched at any time by assigning the property on
 * the returned object, so a single test can move a stub from failing to
 * succeeding without building a second window.
 *
 * @param {object} [options] - stub configuration.
 * @param {object} [options.config] - the configuration `getPluginConfiguration` resolves with.
 * @param {Array} [options.users] - the users `getJSON('EmbyAuth/Migration')` resolves with.
 * @param {boolean | Error} [options.getConfigFails] - makes every `getPluginConfiguration` call reject.
 * @param {number} [options.getConfigFailsFromCall] - makes `getPluginConfiguration` reject starting
 *   with this 1-based call number, so an earlier call in the same test can still succeed.
 * @param {boolean | Error} [options.updateConfigFails] - makes `updatePluginConfiguration` reject.
 * @param {boolean | Error} [options.migrationStatusFails] - makes `getJSON` reject.
 * @param {boolean | Error} [options.runMigrationFails] - makes `ajax` reject.
 * @returns {object} the stub `ApiClient`.
 */
function stubApiClient(options = {}) {
  const api = {
    config: { ...DEFAULT_CONFIG, ...(options.config ?? {}) },
    users: options.users ?? [],
    getConfigFails: options.getConfigFails ?? false,
    getConfigFailsFromCall: options.getConfigFailsFromCall ?? null,
    updateConfigFails: options.updateConfigFails ?? false,
    migrationStatusFails: options.migrationStatusFails ?? false,
    runMigrationFails: options.runMigrationFails ?? false,
    getConfigCalls: 0,
    updateCalls: [],
    migrationStatusCalls: 0,
    runMigrationCalls: 0,
    getUrl(pathSegment) {
      return 'http://localhost/' + pathSegment;
    },
    getJSON() {
      api.migrationStatusCalls += 1;
      if (api.migrationStatusFails) {
        return Promise.reject(rejectionFor(api.migrationStatusFails, 'migration status failed'));
      }

      return Promise.resolve({ Users: api.users });
    },
    ajax() {
      api.runMigrationCalls += 1;
      if (api.runMigrationFails) {
        return Promise.reject(rejectionFor(api.runMigrationFails, 'run migration failed'));
      }

      return Promise.resolve({});
    },
    getPluginConfiguration() {
      api.getConfigCalls += 1;
      const failsFromCallNumber =
        api.getConfigFailsFromCall !== null && api.getConfigCalls >= api.getConfigFailsFromCall;
      if (api.getConfigFails || failsFromCallNumber) {
        return Promise.reject(rejectionFor(api.getConfigFails, 'load configuration failed'));
      }

      return Promise.resolve({ ...api.config });
    },
    updatePluginConfiguration(_id, config) {
      api.updateCalls.push(config);
      if (api.updateConfigFails) {
        return Promise.reject(rejectionFor(api.updateConfigFails, 'save configuration failed'));
      }

      return Promise.resolve({});
    },
  };

  return api;
}

/**
 * Builds a stub `Dashboard` global matching the members `configPage.html` calls.
 *
 * @returns {object} the stub `Dashboard`.
 */
function stubDashboard() {
  return {
    showLoadingCalls: 0,
    hideLoadingCalls: 0,
    updateResults: [],
    showLoadingMsg() {
      this.showLoadingCalls += 1;
    },
    hideLoadingMsg() {
      this.hideLoadingCalls += 1;
    },
    processPluginConfigurationUpdateResult(result) {
      this.updateResults.push(result);
    },
  };
}

/**
 * Builds a jsdom window over the shipping settings page, with `ApiClient` and
 * `Dashboard` stubs injected before the page's inline script parses.
 *
 * @param {object} [options] - forwarded to {@link stubApiClient}.
 * @returns {{window: import('jsdom').DOMWindow, document: Document, api: object, dashboard: object}}
 *   the constructed window, its document, and the two stubs.
 */
function buildDom(options = {}) {
  const html = fs.readFileSync(PAGE_PATH, 'utf8');
  const api = stubApiClient(options);
  const dashboard = stubDashboard();

  const dom = new JSDOM(html, {
    url: 'http://localhost/web/configurationpage',
    runScripts: 'dangerously',
    beforeParse(window) {
      window.ApiClient = api;
      window.Dashboard = dashboard;
    },
  });

  return { window: dom.window, document: dom.window.document, api, dashboard };
}

/**
 * Awaits three macrotask boundaries, so promise chains queued by a dispatched
 * event have settled before an assertion reads the resulting DOM state.
 *
 * @returns {Promise<void>} resolves once the boundaries have passed.
 */
async function flush() {
  for (let i = 0; i < 3; i += 1) {
    // eslint-disable-next-line no-await-in-loop
    await new Promise((resolve) => setTimeout(resolve, 0));
  }
}

/**
 * Dispatches `pageshow` on the settings page's own page element.
 *
 * @param {Document} document - the page's document.
 * @param {import('jsdom').DOMWindow} window - the page's window.
 * @returns {void}
 */
function firePageshow(document, window) {
  document.querySelector('#EmbyAuthConfigPage').dispatchEvent(new window.Event('pageshow'));
}

/**
 * Dispatches a cancelable `submit` event on the settings form.
 *
 * @param {Document} document - the page's document.
 * @param {import('jsdom').DOMWindow} window - the page's window.
 * @returns {void}
 */
function fireSubmit(document, window) {
  document
    .querySelector('#EmbyAuthConfigForm')
    .dispatchEvent(new window.Event('submit', { cancelable: true }));
}

/**
 * Activates the Save button through its own DOM `click` method, so a
 * disabled button's activation behavior is honored the way a real click is.
 *
 * @param {Document} document - the page's document.
 * @returns {void}
 */
function clickSave(document) {
  document.querySelector('.button-submit').click();
}

/**
 * Dispatches a click on the Run migration now button.
 *
 * @param {Document} document - the page's document.
 * @param {import('jsdom').DOMWindow} window - the page's window.
 * @returns {void}
 */
function clickRunMigration(document, window) {
  document
    .querySelector('#EmbyAuthRunMigration')
    .dispatchEvent(new window.Event('click', { bubbles: true, cancelable: true }));
}

module.exports = {
  buildDom,
  stubApiClient,
  stubDashboard,
  flush,
  firePageshow,
  fireSubmit,
  clickSave,
  clickRunMigration,
};
