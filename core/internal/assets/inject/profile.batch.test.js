const fs = require('fs');
const path = require('path');
const vm = require('vm');
const { test } = require('node:test');
const assert = require('node:assert/strict');

function loadProfile(api) {
  const toolbar = {
    children: [],
    querySelector() { return null; },
    appendChild(child) { this.children.push(child); },
  };
  const popups = [];
  const alerts = [];
  const calls = [];
  const document = {
    readyState: 'loading',
    addEventListener() {},
    querySelector(selector) {
      return selector === '.profile-info .opr-area' ? toolbar : null;
    },
    createElement() { return { style: {}, textContent: '', innerHTML: '' }; },
  };
  const sandbox = {
    console: { log() {}, warn() {} },
    document,
    location: { pathname: '/web/pages/profile', search: '?username=author-123' },
    URLSearchParams,
    setTimeout() {},
    __wx_log() {},
    __show_batch_download_ui__(videos) { popups.push(Array.from(videos, (video) => video.id)); },
    __close_batch_download_ui__() {},
    alert(message) { alerts.push(message); },
    WXE: {
      onUserFeedsLoaded() {},
      onUserLiveReplayLoaded() {},
      onInteractionedFeedsLoaded() {},
    },
    WXU: {
      API: api && {
        async finderUserPage(payload) {
          calls.push(payload);
          return api(payload);
        },
      },
      format_feed(item) { return { id: item.id, type: 'media' }; },
    },
  };
  sandbox.window = sandbox;
  vm.createContext(sandbox);
  vm.runInContext(fs.readFileSync(path.join(__dirname, 'profile.js'), 'utf8'), sandbox);
  sandbox.__wx_channels_profile_collector.injectToolbarDownloadIcon();
  return { sandbox, button: toolbar.children[0], popups, alerts, calls };
}

test('profile batch button fetches the first page when no feed event was captured', async () => {
  const env = loadProfile(async () => ({ data: { object: [{ id: 'one' }, { id: 'two' }] } }));
  await env.button.onclick();
  assert.equal(env.calls.length, 1);
  assert.equal(env.calls[0].username, 'author-123');
  assert.equal(env.calls[0].lastBuffer, '');
  assert.deepEqual(env.popups, [['one', 'two']]);
  assert.deepEqual(env.alerts, []);
});

test('profile batch button uses captured videos without another request', async () => {
  const env = loadProfile(async () => { throw new Error('unexpected request'); });
  env.sandbox.__wx_channels_profile_collector.videos.push({ id: 'captured', type: 'media' });
  await env.button.onclick();
  assert.equal(env.calls.length, 0);
  assert.deepEqual(env.popups, [['captured']]);
});

test('profile batch button reports unavailable list API visibly', async () => {
  const env = loadProfile(null);
  await env.button.onclick();
  assert.equal(env.popups.length, 0);
  assert.match(env.alerts[0], /接口尚未就绪/);
});
