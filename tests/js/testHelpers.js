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

/**
 * Jellyfin's own Default login method ID, matching `LoginMethodMove.DefaultProviderId`.
 */
const DEFAULT_PROVIDER_ID = 'Jellyfin.Server.Implementations.Users.DefaultAuthenticationProvider';

/**
 * The value that means no path moves a user off the Emby login method, matching
 * `PluginConfiguration.RemainOnEmbyLoginMethod`.
 */
const REMAIN_ON_EMBY_LOGIN_METHOD = 'RemainOnEmbyLoginMethod';

const DEFAULT_CONFIG = {
  EmbyServerUrl: 'http://emby.example.test:8096',
  EmbyApiKey: 'default-api-key',
  MigrationMode: 'KeepEmbyInCharge',
  AccountAccess: 'NoLibraries',
  MigrationTarget: DEFAULT_PROVIDER_ID,
  PasswordSetTarget: '',
};

/**
 * The default `Task` field a healthy, never-yet-run install reports: a registered worker,
 * idle, with no prior run. Used as the stub default so an unrelated test does not have to
 * pass a `Task` value just to avoid the absent-task condition; a test of that condition
 * passes `task: null` explicitly.
 */
const DEFAULT_TASK = { State: 'Idle', Progress: null, LastEndTimeUtc: null, LastResult: null };

/**
 * The default `AvailableTargets` field: just Jellyfin's Default login method, matching a
 * healthy install where the migration target dropdown's saved default selects cleanly. A test
 * of the dropdown itself passes an explicit list.
 */
const DEFAULT_AVAILABLE_TARGETS = [{ Name: 'Default', Id: DEFAULT_PROVIDER_ID }];

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
 * @param {Array} [options.users] - the users `getJSON('EmbyAuth/Migration')` resolves with. Defaults to an empty
 *   list, so an ordinary successful load needs no configuration.
 * @param {boolean} [options.recordsUnavailable] - the `RecordsUnavailable` flag `getJSON('EmbyAuth/Migration')`
 *   resolves with, settable at any time on the returned stub.
 * @param {object | null} [options.task] - the `Task` field `getJSON('EmbyAuth/Migration')` resolves with. Defaults
 *   to an idle, never-run worker (see {@link DEFAULT_TASK}); pass `null` to test the absent-task condition.
 * @param {Array | null} [options.taskSequence] - when set, advances the `Task` field through this array by
 *   successive `getJSON` call number (clamped to the last entry once exhausted), so a test can script a sequence
 *   of task states across successive polls. Takes priority over `options.task` once the first call is made.
 * @param {boolean} [options.migrationStatusHangs] - makes every `getJSON('EmbyAuth/Migration')` call return a
 *   promise that never resolves, so a test can prove the page never issues a second request while one is pending.
 * @param {Array} [options.availableTargets] - the `AvailableTargets` field `getJSON('EmbyAuth/Migration')`
 *   resolves with. Defaults to a list holding only Jellyfin's Default login method (see
 *   {@link DEFAULT_AVAILABLE_TARGETS}), so the default `config.MigrationTarget` selects cleanly.
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
    recordsUnavailable: options.recordsUnavailable ?? false,
    task: options.task !== undefined ? options.task : DEFAULT_TASK,
    taskSequence: options.taskSequence ?? null,
    migrationStatusHangs: options.migrationStatusHangs ?? false,
    availableTargets: options.availableTargets ?? DEFAULT_AVAILABLE_TARGETS,
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

      if (api.migrationStatusHangs) {
        return new Promise(() => {});
      }

      let task = api.task;
      if (api.taskSequence) {
        const index = Math.min(api.migrationStatusCalls - 1, api.taskSequence.length - 1);
        task = api.taskSequence[index];
      }

      return Promise.resolve({
        Users: api.users,
        RecordsUnavailable: api.recordsUnavailable,
        Task: task,
        AvailableTargets: api.availableTargets,
      });
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
 * Installs a controllable fake `setInterval`/`clearInterval` on the window, replacing jsdom's
 * real timer-driven implementation.
 *
 * jsdom implements `window.setInterval` internally by chaining calls to Node's own bare
 * `setTimeout` (see `jsdom/lib/jsdom/browser/Window.js`'s `timerInitializationSteps`), so
 * `node:test`'s built-in `mock.timers` — which mocks the global `setInterval` function, not
 * `setTimeout` — never intercepts it. Overriding the window's own methods directly, before the
 * page's script runs, sidesteps that implementation detail entirely and leaves the real Node
 * timers `flush()` depends on untouched.
 *
 * @param {import('jsdom').DOMWindow} window - the page's window, patched in place.
 * @returns {{tick: (ms: number) => void}} `tick(ms)` synchronously runs every registered
 *   interval's callback once per elapsed `delay`, in registration order.
 */
function installFakeInterval(window) {
  const intervals = new Map();
  let nextHandle = 1;

  window.setInterval = function (handler, delay) {
    const handle = nextHandle;
    nextHandle += 1;
    intervals.set(handle, { handler, delay, elapsed: 0 });
    return handle;
  };

  window.clearInterval = function (handle) {
    intervals.delete(handle);
  };

  return {
    tick(ms) {
      for (const [handle, interval] of intervals) {
        interval.elapsed += ms;
        while (interval.elapsed >= interval.delay && intervals.has(handle)) {
          interval.elapsed -= interval.delay;
          interval.handler();
        }
      }
    },
  };
}

/**
 * Builds a jsdom window over the shipping settings page, with `ApiClient` and
 * `Dashboard` stubs injected before the page's inline script parses.
 *
 * @param {object} [options] - forwarded to {@link stubApiClient}.
 * @returns {{window: import('jsdom').DOMWindow, document: Document, api: object,
 *   dashboard: object, interval: {tick: (ms: number) => void}, close: () => void}} the
 *   constructed window, its document, the two stubs, the fake interval controller (see
 *   {@link installFakeInterval}), and a `close()` that dispatches `pagehide` (stopping any
 *   active migration poll through the page's own cleanup listener) before tearing the window down.
 */
function buildDom(options = {}) {
  const html = fs.readFileSync(PAGE_PATH, 'utf8');
  const api = stubApiClient(options);
  const dashboard = stubDashboard();
  let interval;

  const dom = new JSDOM(html, {
    url: 'http://localhost/web/configurationpage',
    runScripts: 'dangerously',
    beforeParse(window) {
      window.ApiClient = api;
      window.Dashboard = dashboard;
      interval = installFakeInterval(window);
    },
  });

  return {
    window: dom.window,
    document: dom.window.document,
    api,
    dashboard,
    interval,
    close() {
      const pageElement = dom.window.document.querySelector('#EmbyAuthConfigPage');
      if (pageElement) {
        pageElement.dispatchEvent(new dom.window.Event('pagehide'));
      }

      dom.window.close();
    },
  };
}

/**
 * Reads the migration list's items as an array of their text content, in DOM order.
 *
 * @param {Document} document - the page's document.
 * @returns {string[]} the text of each `<li>` under `#EmbyAuthMigrationUsers`.
 */
function listItemTexts(document) {
  return Array.from(document.querySelectorAll('#EmbyAuthMigrationUsers li')).map(
    (item) => item.textContent,
  );
}

/**
 * Returns the settings-level status element that sits with the Save button.
 *
 * @param {Document} document - the page's document.
 * @returns {Element} the `#EmbyAuthSettingsStatus` element.
 */
function settingsStatus(document) {
  return document.querySelector('#EmbyAuthSettingsStatus');
}

/**
 * Describes an element's children, in document order, for a deep-equal
 * assertion against an expected child tree.
 *
 * Each entry holds the tag name, the class list as a sorted array (so the
 * assertion does not depend on class order), the `aria-hidden` attribute
 * (or null), and the child's own text content.
 *
 * @param {Element} element - the message element to describe.
 * @returns {Array<{tagName: string, classes: string[], ariaHidden: (string | null), text: string}>}
 *   one entry per element child, in document order.
 */
function messageChildren(element) {
  return Array.from(element.children).map((child) => ({
    tagName: child.tagName,
    classes: Array.from(child.classList).sort(),
    ariaHidden: child.getAttribute('aria-hidden'),
    text: child.textContent,
  }));
}

/**
 * Reads the CSS rules the page's own `<style>` elements declare.
 *
 * jsdom loads no dashboard stylesheet, so this is how a test can assert on a
 * rule the page ships, by reading the CSSOM jsdom parses from an inline
 * `<style>` element.
 *
 * @param {Document} document - the page's document.
 * @returns {CSSRule[]} every rule declared by a `<style>` element on the page, in document order.
 */
function pageStyleRules(document) {
  return Array.from(document.styleSheets).flatMap((sheet) => Array.from(sheet.cssRules));
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

/**
 * Advances the page's fake poll interval by one 2-second tick and awaits the real macrotask
 * boundaries the page's own promise chain still needs to settle.
 *
 * The fake interval (see {@link installFakeInterval}, returned from `buildDom` as `interval`)
 * fires its callback synchronously; `flush()` lets the resolved `ApiClient.getJSON` promise
 * actually run its `.then()` callbacks before an assertion reads the resulting DOM state.
 *
 * @param {{tick: (ms: number) => void}} interval - the fake interval controller `buildDom` returned.
 * @param {number} [times] - how many 2-second poll intervals to advance. Defaults to 1.
 * @returns {Promise<void>} resolves once every tick's promise chain has settled.
 */
async function tickPoll(interval, times = 1) {
  for (let i = 0; i < times; i += 1) {
    interval.tick(2000);
    // eslint-disable-next-line no-await-in-loop
    await flush();
  }
}

/**
 * Reads a `<select>`'s options as an array of `{ value, text, disabled, selected }`, in DOM order.
 *
 * @param {HTMLSelectElement} select - the select element.
 * @returns {Array<{value: string, text: string, disabled: boolean, selected: boolean}>} its options.
 */
function selectOptions(select) {
  return Array.from(select.options).map((option) => ({
    value: option.value,
    text: option.textContent,
    disabled: option.disabled,
    selected: option.selected,
  }));
}

module.exports = {
  buildDom,
  stubApiClient,
  stubDashboard,
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
};
