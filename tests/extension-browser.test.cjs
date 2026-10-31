const {test, before, after} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const http = require('node:http');
const {chromium} = require('playwright');

let context, worker, page, server, baseURL, tabId;
const artifacts = path.resolve(__dirname, '../.test-artifacts');
const extensionDir = path.join(artifacts, 'extension');
const sourceDir = path.resolve(__dirname, '../extension');
function copyTree(source, destination) {
  fs.mkdirSync(destination, {recursive:true});
  for (const entry of fs.readdirSync(source, {withFileTypes:true})) {
    const from = path.join(source, entry.name), to = path.join(destination, entry.name);
    if (entry.isDirectory()) copyTree(from, to);
    else if (entry.isFile()) fs.writeFileSync(to, fs.readFileSync(from));
  }
}
const fixture = `<!doctype html><meta charset="utf-8"><title>ClipType test</title>
<textarea id="plain" aria-label="普通文本框"></textarea>
<input id="single"><input id="limited" maxlength="3"><textarea id="other"></textarea>
<div id="rich" contenteditable="true" style="white-space:pre-wrap;min-height:80px"></div>
<div id="shadow"></div><iframe id="frame" src="/frame"></iframe>
<script>
window.counts={input:0,paste:0,enter:0};
document.addEventListener('input',()=>counts.input++);
document.addEventListener('paste',()=>counts.paste++);
document.addEventListener('keydown',e=>{if(e.key==='Enter')counts.enter++});
document.querySelector('#shadow').attachShadow({mode:'open'}).innerHTML='<textarea id="inside"></textarea>';
</script>`;
const command = message => worker.evaluate(async ({tabId, message}) => {
  return chrome.tabs.sendMessage(tabId, {channel: 'cliptype', ...message});
}, {tabId, message});
async function prepare(selector = '#plain', value = '', start = value.length, end = start) {
  await page.bringToFront();
  await page.evaluate(({selector, value, start, end}) => {
    const el = document.querySelector(selector); el.value = value; el.focus(); el.setSelectionRange(start, end);
    counts.input = counts.paste = counts.enter = 0;
  }, {selector, value, start, end});
  const capture = await command({action: 'capture'});
  assert.equal(capture.ok, true, capture.error);
  return capture;
}
async function start(capture, text, options = {}, mode = 'simulate') {
  const result = await command({action: 'start', id: capture.id, text, options: {delay: 300, interval: 1, ...options}, mode});
  assert.equal(result.ok, true, result.error);
}
async function finished() {
  await page.waitForFunction(() => {
    // Closed shadow UI is intentionally opaque; a new capture acknowledges that the job has ended.
    return true;
  });
  for (let i = 0; i < 100; i++) {
    const result = await command({action: 'capture'});
    if (result.ok) return;
    if (!result.error.includes('任务正在')) throw new Error(result.error);
    await new Promise(resolve => setTimeout(resolve, 30));
  }
  throw new Error('Input job did not finish');
}
before(async () => {
  fs.mkdirSync(artifacts, {recursive: true});
  copyTree(sourceDir, extensionDir);
  const manifest = JSON.parse(fs.readFileSync(path.join(extensionDir, 'manifest.json')));
  // Automated invocation cannot grant activeTab via a real toolbar click. Only the disposable
  // test copy receives localhost access. The shipped manifest has no host_permissions.
  manifest.host_permissions = ['http://127.0.0.1/*'];
  fs.writeFileSync(path.join(extensionDir, 'manifest.json'), JSON.stringify(manifest));
  server = http.createServer((req, res) => {
    res.writeHead(200, {'Content-Type': 'text/html; charset=utf-8'});
    res.end(req.url === '/frame' ? '<textarea id="inner"></textarea>' : fixture);
  });
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  baseURL = `http://127.0.0.1:${server.address().port}`;
  context = await chromium.launchPersistentContext(path.join(artifacts, 'profile-' + process.pid), {
    channel: 'chromium', headless: true,
    ...(process.env.CLIPTYPE_CHROMIUM ? {executablePath: process.env.CLIPTYPE_CHROMIUM} : {}),
    args: [`--disable-extensions-except=${extensionDir}`, `--load-extension=${extensionDir}`]
  });
  worker = context.serviceWorkers()[0] || await context.waitForEvent('serviceworker');
  page = context.pages()[0] || await context.newPage();
  await page.goto(baseURL);
  tabId = await worker.evaluate(async base => (await chrome.tabs.query({})).find(tab => tab.url?.startsWith(base)).id, baseURL);
  await worker.evaluate(async tabId => {
    await chrome.scripting.executeScript({target:{tabId},files:['core.js','content.js']});
  }, tabId);
});
after(async () => { await context?.close(); if (server) await new Promise(resolve => server.close(resolve)); });

test('textarea replaces original selection with Chinese, emoji and newline; emits input without paste or Enter', async () => {
  const capture = await prepare('#plain', 'abcd', 1, 3);
  await start(capture, '中文🙂\r\n下一行'); await finished();
  assert.equal(await page.locator('#plain').inputValue(), 'a中文🙂\n下一行d');
  const counts = await page.evaluate(() => window.counts);
  assert.equal(counts.input, 7); assert.equal(counts.paste, 0); assert.equal(counts.enter, 0);
});
test('single-line inputs normalize line breaks and tabs', async () => {
  const capture = await prepare('#single'); await start(capture, 'A\r\nB\tC'); await finished();
  assert.equal(await page.locator('#single').inputValue(), 'A B    C');
});
test('ordinary insertion inserts one chunk at the caret', async () => {
  const capture = await prepare('#plain', 'ab', 1); await start(capture, '中文🙂', {}, 'insert'); await finished();
  assert.equal(await page.locator('#plain').inputValue(), 'a中文🙂b');
  assert.equal(await page.evaluate(() => counts.input), 1);
});
test('contenteditable inserts text and newline without dispatching Enter', async () => {
  await page.locator('#rich').focus();
  const capture = await command({action:'capture'}); assert.equal(capture.ok, true, capture.error);
  await start(capture, '甲🙂\n乙'); await finished();
  assert.equal(await page.locator('#rich').innerText(), '甲🙂\n乙');
  assert.equal(await page.evaluate(() => counts.enter), 0);
});
test('open shadow DOM input keeps its caret', async () => {
  await page.locator('#shadow textarea').focus();
  const capture = await command({action:'capture'}); assert.equal(capture.ok, true, capture.error);
  await start(capture, '影子🙂'); await finished();
  assert.equal(await page.locator('#shadow textarea').inputValue(), '影子🙂');
});
test('same-origin iframe input works', async () => {
  await page.frameLocator('#frame').locator('#inner').focus();
  const capture = await command({action:'capture'}); assert.equal(capture.ok, true, capture.error);
  await start(capture, '内嵌中文'); await finished();
  assert.equal(await page.frameLocator('#frame').locator('#inner').inputValue(), '内嵌中文');
});
test('Esc cancels a partially typed task', async () => {
  const capture = await prepare(); await start(capture, 'x'.repeat(80), {interval:30});
  await page.waitForFunction(() => document.querySelector('#plain').value.length >= 3);
  await page.keyboard.press('Escape'); await finished();
  const value = await page.locator('#plain').inputValue(); assert.ok(value.length >= 3 && value.length < 80);
  const stable = value; await page.waitForTimeout(100); assert.equal(await page.locator('#plain').inputValue(), stable);
});
test('focus change stops typing without writing into the new field', async () => {
  const capture = await prepare(); await start(capture, 'x'.repeat(80), {interval:30});
  await page.waitForFunction(() => document.querySelector('#plain').value.length >= 3);
  await page.locator('#other').focus(); await finished();
  assert.equal(await page.locator('#other').inputValue(), '');
  assert.ok((await page.locator('#plain').inputValue()).length < 80);
});
test('maxlength and canceled beforeinput are respected', async () => {
  let capture = await prepare('#limited'); await start(capture, '123456'); await finished();
  assert.equal(await page.locator('#limited').inputValue(), '123');
  capture = await prepare();
  await page.evaluate(() => document.querySelector('#plain').addEventListener('beforeinput', e => e.preventDefault(), {once:true}));
  await start(capture, 'blocked'); await finished(); assert.equal(await page.locator('#plain').inputValue(), '');
});
test('React-style tracked value setter is bypassed so input handlers can observe changes', async () => {
  const capture = await prepare();
  await page.evaluate(() => {
    const input = document.querySelector('#plain'); const descriptor = Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value');
    window.trackerCalls = 0;
    Object.defineProperty(input, 'value', {configurable:true, get(){return descriptor.get.call(this)},set(value){trackerCalls++;descriptor.set.call(this,value)}});
  });
  await start(capture, '更新'); await finished();
  assert.equal(await page.locator('#plain').inputValue(), '更新');
  assert.equal(await page.evaluate(() => trackerCalls), 0);
  await page.evaluate(() => delete document.querySelector('#plain').value);
});
test('duplicate injection does not duplicate input; invalid capture token is rejected', async () => {
  await worker.evaluate(async tabId => chrome.scripting.executeScript({target:{tabId},files:['core.js','content.js']}), tabId);
  const capture = await prepare();
  const invalid = await command({action:'start', id:'invalid', text:'bad'}); assert.equal(invalid.ok, false);
  await start(capture, '一次'); await finished(); assert.equal(await page.locator('#plain').inputValue(), '一次');
});
test('popup UI uses real extension messaging and only stores settings (clipboard and active-tab query stubbed)', {timeout:15000}, async () => {
  await prepare();
  await context.addInitScript(({tabId}) => {
    if (location.protocol === 'chrome-extension:') {
      Object.defineProperty(navigator.clipboard, 'readText', {value:async () => '面板输入🙂'});
      chrome.tabs.query = async () => [{id:tabId}];
    }
  }, {tabId});
  const extensionId = new URL(worker.url()).host;
  const popup = await context.newPage();
  await popup.setViewportSize({width:400, height:720});
  await popup.goto(`chrome-extension://${extensionId}/popup.html`);
  await popup.waitForLoadState();
  await popup.waitForFunction(() => !document.querySelector('#simulate').disabled);
  assert.equal(await popup.locator('#text').inputValue(), '面板输入🙂');
  assert.match(await popup.locator('#target').innerText(), /普通文本框/);
  await popup.screenshot({path:path.join(artifacts, 'popup.png'), clip:{x:0,y:0,width:400,height:600}});
  await popup.locator('#simulate').click();
  if (!popup.isClosed()) await popup.close();
  await page.bringToFront(); await finished();
  assert.equal(await page.locator('#plain').inputValue(), '面板输入🙂');
  const storage = await worker.evaluate(() => chrome.storage.local.get(null));
  assert.deepEqual(Object.keys(storage), ['settings']);
});
