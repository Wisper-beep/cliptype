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
  assert.deepEqual(core.settings(), {interval: 30, delay: 500, newlines: 'preserve'});
  assert.deepEqual(core.settings(null), core.DEFAULTS);
  assert.deepEqual(core.settings({interval: -5, delay: 99999, newlines: 'send'}), {interval: 0, delay: 5000, newlines: 'preserve'});
  assert.deepEqual(core.settings({interval: 'bad', delay: Infinity, newlines: 'space'}), {interval: 30, delay: 500, newlines: 'space'});
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
  for (const file of ['background.js', 'core.js', 'content.js', 'popup.js']) {
    const source = fs.readFileSync(path.join(__dirname, '../extension', file), 'utf8');
    new vm.Script(source, {filename: file});
    assert.doesNotMatch(source, /\beval\s*\(|new Function\s*\(/);
  }
});
