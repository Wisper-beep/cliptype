// Gecko DOM tests. Messaging is a local test double, not an installed-extension claim.
const {test,before,after} = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const http = require('node:http');
const {firefox} = require('playwright');
let browser,page,server;
async function command(message) {
  return page.evaluate(message => new Promise(resolve => globalThis.clipTypeListener({channel:'cliptype',...message},{id:'gecko-test'},resolve)),message);
}
async function reset(selector='textarea',value='',start=value.length,end=start) {
  await page.evaluate(({selector,value,start,end}) => {
    const el=document.querySelector(selector);el.value=value;el.focus();el.setSelectionRange(start,end);
  },{selector,value,start,end});
  const result=await command({action:'capture'});assert.ok(result.ok,result.error);return result;
}
async function start(capture,text,options={}) {
  assert.ok((await command({action:'start',id:capture.id,text,options:{delay:300,interval:5,...options}})).ok);
}
async function done() {
  for(let i=0;i<150;i++) { if((await command({action:'status'})).state==='idle')return;await page.waitForTimeout(20); }
  throw new Error('Gecko task timeout');
}
before(async()=>{
  server=http.createServer((req,res)=>{res.writeHead(200,{'Content-Type':'text/html; charset=utf-8'});res.end('<meta charset="utf-8"><textarea></textarea><input><div contenteditable="true" style="white-space:pre-wrap"></div>');});
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  browser=await firefox.launch({headless:true,...(process.env.CLIPTYPE_FIREFOX?{executablePath:process.env.CLIPTYPE_FIREFOX}:{})});
  page=await browser.newPage({viewport:null});
  await page.goto(`http://127.0.0.1:${server.address().port}`);
  await page.evaluate(()=>{
    globalThis.browser={runtime:{id:'gecko-test',onMessage:{addListener(listener){globalThis.clipTypeListener=listener;}}}};
  });
  await page.addScriptTag({path:path.resolve(__dirname,'../extension/core.js')});
  await page.addScriptTag({path:path.resolve(__dirname,'../extension/content.js')});
  console.log('Actual Gecko engine:',browser.version());
});
after(async()=>{await browser?.close();if(server)await new Promise(resolve=>server.close(resolve));});
test('Gecko textarea: middle selection, CJK, family emoji, flag and newline precision',async()=>{
  const capture=await reset('textarea','abcd',1,3);await start(capture,'中文👩‍💻🇨🇳\r\n行');await done();
  assert.equal(await page.locator('textarea').inputValue(),'a中文👩‍💻🇨🇳\n行d');
});
test('Gecko input normalizes line breaks and tabs',async()=>{
  const capture=await reset('input');await start(capture,'A\nB\tC');await done();assert.equal(await page.locator('input').inputValue(),'A B    C');
});
test('Gecko contenteditable inserts Unicode and line breaks',async()=>{
  await page.locator('div[contenteditable]').focus();const capture=await command({action:'capture'});assert.ok(capture.ok,capture.error);
  await start(capture,'甲🙂\n乙');await done();assert.equal(await page.locator('div[contenteditable]').innerText(),'甲🙂\n乙');
});
test('Gecko pause/resume preserves text, and Esc cancels',async()=>{
  let capture=await reset();await start(capture,'中文🙂'.repeat(8),{interval:30});
  await page.waitForFunction(()=>document.querySelector('textarea').value.length>=3);
  await command({action:'pause'});const before=await page.locator('textarea').inputValue();await page.waitForTimeout(120);
  assert.equal(await page.locator('textarea').inputValue(),before);await command({action:'resume'});await done();
  assert.equal(await page.locator('textarea').inputValue(),'中文🙂'.repeat(8));
  capture=await reset();await start(capture,'x'.repeat(80),{interval:30});
  await page.waitForFunction(()=>document.querySelector('textarea').value.length>=2);await page.keyboard.press('Escape');await done();
  assert.ok((await page.locator('textarea').inputValue()).length<80);
});
test('Gecko stale caret and changed focus refuse input without re-focusing',async()=>{
  let capture=await reset('textarea','abcd',1);
  await page.evaluate(()=>document.querySelector('textarea').setSelectionRange(3,3));await start(capture,'WRONG');await done();
  assert.equal(await page.locator('textarea').inputValue(),'abcd');
  capture=await reset();await start(capture,'x'.repeat(50),{interval:30});
  await page.waitForFunction(()=>document.querySelector('textarea').value.length>=2);await page.locator('input').focus();await done();
  assert.equal(await page.evaluate(()=>document.activeElement.tagName),'INPUT');
});
