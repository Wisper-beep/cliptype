"use strict";
const $ = id => document.getElementById(id);
const core = globalThis.ClipTypeCore;
const api = globalThis.browser || globalThis.chrome;
let tabId;
let target;
let working = false;
let saveTimer;
function optionsFromForm() {
  return core.settings({interval:$("interval").value,delay:$("delay").value,newlines:$("newlines").value,
    randomize:$("randomize").checked,variation:$("variation").value,punctuation:$("punctuation").value,
    sentence:$("sentence").value,paragraph:$("paragraph").value,quickButton:$("quickButton").checked});
}
async function saveOptions() {
  const options = optionsFromForm();
  try {
    await api.storage.local.set({settings:options});
    if (tabId) await api.tabs.sendMessage(tabId, {channel:"cliptype",action:"quick",enabled:options.quickButton}).catch(() => {});
  } catch { status("无法保存设置；本次仍可使用。", true); }
}
function status(text, error = false) { $("status").textContent = text; $("status").classList.toggle("error", error); }
function update() {
  $("count").textContent = core.graphemes($("text").value).length.toLocaleString() + " 个字符";
  for (const id of ["simulate", "insert"]) $(id).disabled = working || !target || !$("text").value.length;
}
async function readClipboard() {
  try {
    const text = await navigator.clipboard.readText();
    if (text.length > core.MAX_LENGTH) throw new Error("剪贴板超过 100,000 个字符，请分段复制。");
    $("text").value = text;
    status(text ? "准备就绪。选择输入方式，面板会自动关闭。" : "剪贴板里没有文字。也可以在上方手动粘贴。");
    update();
  } catch (error) { status("无法读取剪贴板，请点击「读取剪贴板」重试，或手动粘贴到上方。" + (error.message.includes("100,000") ? " 单次最多 100,000 个字符。" : ""), true); }
}
async function start(mode) {
  if (working || !target || !$("text").value.length) return;
  working = true; update();
  try {
    const options = optionsFromForm();
    clearTimeout(saveTimer);
    await api.storage.local.set({settings: options});
    const result = await api.tabs.sendMessage(tabId, {channel: "cliptype", action: "start", id: target.id, text: $("text").value, options, mode});
    if (!result?.ok) throw new Error(result?.error || "无法启动输入，请重新打开扩展。");
    window.close();
  } catch (error) { status(error.message, true); working = false; update(); }
}
async function initialize() {
  try {
    const stored = await api.storage.local.get("settings");
    const options = core.settings(stored.settings);
    $("interval").value = options.interval; $("delay").value = options.delay; $("newlines").value = options.newlines;
    for (const key of ["variation","punctuation","sentence","paragraph"]) $(key).value = options[key];
    $("randomize").checked = options.randomize; $("quickButton").checked = options.quickButton;
    const [tab] = await api.tabs.query({active: true, currentWindow: true});
    if (!tab?.id) throw new Error("找不到当前网页。");
    tabId = tab.id;
    await api.scripting.executeScript({target: {tabId}, files: ["core.js", "content.js"]});
    const result = await api.tabs.sendMessage(tabId, {channel: "cliptype", action: "capture"});
    if (!result?.ok) throw new Error(result?.error || "找不到原输入位置。");
    target = result;
    $("target").textContent = "输入位置：" + target.label + (target.singleLine ? " · 单行" : "");
    await api.tabs.sendMessage(tabId, {channel:"cliptype",action:"quick",enabled:options.quickButton});
  } catch (error) {
    $("target").textContent = /Cannot access|Missing host|not allowed|extensions gallery/i.test(error.message)
      ? "这个页面不支持扩展输入。请在普通网页使用；本地文件需要开启文件网址访问。"
      : error.message;
  }
  await readClipboard();
  if (!target) status("点击目标网页的输入框后，再打开扩展。浏览器设置页和扩展商店不支持。", true);
  update();
}
$("text").addEventListener("input", update);
$("read").addEventListener("click", readClipboard);
$("simulate").addEventListener("click", () => start("simulate"));
$("insert").addEventListener("click", () => start("insert"));
for (const id of ["interval","delay","newlines","randomize","variation","punctuation","sentence","paragraph","quickButton"])
  $(id).addEventListener("change", saveOptions);
$("preset").addEventListener("change", () => {
  const presets = {slow:100,normal:30,fast:5};
  if ($( "preset").value in presets) $("interval").value = presets[$("preset").value];
  saveOptions();
});
initialize();
