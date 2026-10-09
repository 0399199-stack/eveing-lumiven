const fs = require('fs');
const path = require('path');
const vm = require('vm');
const { test } = require('node:test');
const assert = require('node:assert/strict');

function loadAPI() {
  let onAPILoaded;
  const sandbox = {
    console: { log() {} },
    WXE: { onAPILoaded(handler) { onAPILoaded = handler; } },
  };
  sandbox.window = sandbox;
  vm.createContext(sandbox);
  vm.runInContext(fs.readFileSync(path.join(__dirname, 'utils.js'), 'utf8'), sandbox);
  return { sandbox, emit: (groups) => onAPILoaded(groups) };
}

test('a bundle exposing finderUserPage alone initializes the profile API', () => {
  const { sandbox, emit } = loadAPI();
  const bundle = { marker: 'profile', finderUserPage() { return this.marker; } };
  emit({ bundle });
  assert.equal(sandbox.WXU.API.finderUserPage(), 'profile');
  assert.equal(Object.hasOwn(sandbox.WXU.API, 'toString'), false);
});

test('separate comment and profile API groups are retained in either export order', () => {
  const comments = { marker: 'comments', finderGetCommentDetail() { return this.marker; } };
  const profile = { marker: 'profile', finderUserPage() { return this.marker; } };
  for (const groups of [
    { comments, profile },
    { profile, comments },
  ]) {
    const { sandbox, emit } = loadAPI();
    emit(groups);
    assert.equal(sandbox.WXU.API.finderGetCommentDetail(), 'comments');
    assert.equal(sandbox.WXU.API.finderUserPage(), 'profile');
  }
});

test('non-enumerable prototype methods keep their source receiver when API groups merge', () => {
  class ProfileAPI {
    constructor() { this.marker = 'profile'; }
    finderUserPage() { return this.marker; }
  }
  const { sandbox, emit } = loadAPI();
  emit({ comments: { finderGetCommentDetail() {} }, profile: new ProfileAPI() });
  assert.equal(sandbox.WXU.API.finderUserPage(), 'profile');
  assert.ok(Object.keys(sandbox.WXU.API).includes('finderUserPage'));
});

test('empty API groups do not interrupt later API discovery', () => {
  const { sandbox, emit } = loadAPI();
  assert.doesNotThrow(() => emit(null));
  assert.doesNotThrow(() => emit({ missing: null, notAnAPI: 0, profile: { finderUserPage() { return 1; } } }));
  assert.equal(sandbox.WXU.API.finderUserPage(), 1);
});
