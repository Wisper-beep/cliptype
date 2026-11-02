const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const core = require('../extension/core.js');

test('normalize newlines, tabs, and control characters without losing Unicode', () => {
  assert.equal(core.normalize('中文🙂\r\nA\rB\nC\u2028D\u2029E\tF\0\b\x7f'), '中文🙂\nA\nB\nC\nD\nE    F');
  assert.equal(core.normalize('A\r\nB', true), 'A B');
});
test('graphemes keep combining accents, emoji families and flags together', () => {
  assert.deepEqual(core.graphemes('中e\u0301👩‍💻🇨🇳'), ['中', 'e\u0301', '👩‍💻', '🇨🇳']);
});
test('settings validate untrusted messages and saved preferences', () => {
  assert.deepEqual(core.settings(), core.DEFAULTS);
  assert.deepEqual(core.settings(null), core.DEFAULTS);
  assert.deepEqual(core.settings({interval: -5, delay: 99999, newlines: 'send'}), {...core.DEFAULTS, interval: 0, delay: 5000});
  assert.deepEqual(core.settings({interval: 'bad', delay: Infinity, newlines: 'space'}), {...core.DEFAULTS, newlines: 'space'});
});
test('production manifest only requests declared functionality', () => {
  const manifest = require('../extension/manifest.json');
  assert.equal(manifest.manifest_version, 3);
  assert.deepEqual(manifest.permissions, ['activeTab', 'scripting', 'storage', 'clipboardRead']);
  assert.equal(manifest.host_permissions, undefined);
  assert.equal(manifest.content_scripts, undefined);
  assert.equal(manifest.commands._execute_action.suggested_key, 'Alt+Shift+V');
  for (const file of [manifest.action.default_popup, manifest.background.service_worker, ...Object.values(manifest.icons)])
    assert.ok(fs.existsSync(path.join(__dirname, '../extension', file)), file);
});
test('all extension JavaScript parses without dynamic code generation', () => {
  for (const file of ['background.js', 'core.js', 'content.js', 'popup.js', 'test.js']) {
    const source = fs.readFileSync(path.join(__dirname, '../extension', file), 'utf8');
    new vm.Script(source, {filename: file});
    assert.doesNotMatch(source, /\beval\s*\(|new Function\s*\(/);
  }
});
test('rhythm is bounded and supports CJK punctuation, sentences and blank lines', () => {
  const options = {...core.DEFAULTS, interval:100, randomize:true, variation:20, punctuation:200, sentence:400, paragraph:800};
  assert.equal(core.delayAfter('字', '字', options, 0), 80);
  assert.equal(core.delayAfter('字', '字', options, 1), 120);
  assert.equal(core.delayAfter('，', '字', options, .5), 300);
  assert.equal(core.delayAfter('。', '字', options, .5), 500);
  assert.equal(core.delayAfter('\n', '\n', options, .5), 900);
});
test('session only pauses active tasks and cannot resume a completed task', () => {
  const session = new core.Session(4);
  assert.equal(session.resume(), false); session.begin();
  assert.ok(session.pause()); assert.equal(session.state, 'paused'); assert.ok(session.resume());
  assert.equal(session.state, 'countdown'); session.running(); assert.ok(session.pause()); assert.ok(session.resume());
  assert.equal(session.state, 'running'); session.finish('completed'); assert.equal(session.pause(), false); assert.equal(session.resume(), false);
});
