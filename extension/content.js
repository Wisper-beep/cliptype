(() => {
  "use strict";
  if (globalThis.__clipTypeInstalled) return;
  globalThis.__clipTypeInstalled = true;
  const core = globalThis.ClipTypeCore;
  const api = globalThis.browser || globalThis.chrome;
  let captured = null;
  let job = null;
  let overlay = null;
  let dismissTimer = null;

  function activeElement(doc = document) {
    let element = doc.activeElement;
    while (element) {
      if (element.shadowRoot?.activeElement) { element = element.shadowRoot.activeElement; continue; }
      if (element.tagName === "IFRAME" || element.tagName === "FRAME") {
        try {
          if (element.contentDocument) return activeElement(element.contentDocument);
        } catch { /* Cross-origin frames need host access, which this extension does not request. */ }
      }
      break;
    }
    return element;
  }
  function selectionFor(element) {
    const root = element.getRootNode();
    return typeof root.getSelection === "function" ? root.getSelection() : element.ownerDocument.getSelection();
  }
  function rangeInside(range, element) {
    return range && element.contains(range.startContainer) && element.contains(range.endContainer);
  }
  function snapshot(element) {
    if (element.tagName === "INPUT" || element.tagName === "TEXTAREA") {
      if (element.disabled || element.readOnly || typeof element.selectionStart !== "number")
        throw new Error("这个输入框不支持定位文字光标，或处于只读状态。");
      return {kind: "text", start: element.selectionStart, end: element.selectionEnd, direction: element.selectionDirection, value: element.value};
    }
    if (element.isContentEditable) {
      let host = element;
      while (host.parentElement?.isContentEditable) host = host.parentElement;
      const selection = selectionFor(host);
      if (!selection?.rangeCount || !rangeInside(selection.getRangeAt(0), host))
        throw new Error("请先在富文本输入区放好光标。");
      return {kind: "rich", host, range: selection.getRangeAt(0).cloneRange(), html: host.innerHTML};
    }
    throw new Error("先点击网页中的输入框，再打开 ClipType。跨域内嵌框暂不支持。");
  }
  function capture() {
    if (job) throw new Error("已有输入任务正在进行，请先按 Esc 停止。");
    const active = activeElement();
    if (!active) throw new Error("找不到输入位置，请先点击网页输入框。");
    const saved = snapshot(active);
    const element = saved.host || active;
    const label = element.getAttribute("aria-label") || element.getAttribute("placeholder") || element.id || element.tagName.toLowerCase();
    captured = {id: crypto.randomUUID(), element, saved};
    return {ok: true, id: captured.id, label: label.replace(/\s+/g, " ").slice(0, 80), singleLine: element.tagName === "INPUT"};
  }
  function showStatus(text, running) {
    clearTimeout(dismissTimer);
    if (!overlay?.host.isConnected) {
      const host = document.createElement("div");
      host.setAttribute("data-cliptype-status", "");
      host.style.cssText = "all:initial;position:fixed;right:20px;bottom:20px;z-index:2147483647;display:block";
      const shadow = host.attachShadow({mode: "closed"});
      const style = document.createElement("style");
      style.textContent = ":host{color-scheme:light}.box{display:flex;gap:16px;align-items:center;max-width:440px;padding:14px 18px;background:#172338;color:#fff;border-radius:12px;box-shadow:0 6px 28px #0003;font:14px/1.6 system-ui,sans-serif}.message{white-space:pre-wrap}button{border:0;border-radius:7px;padding:7px 12px;cursor:pointer;background:#fff;color:#172338;font:inherit;white-space:nowrap}";
      const box = document.createElement("div"); box.className = "box";
      const message = document.createElement("span"); message.className = "message"; message.setAttribute("role", "status");
      const button = document.createElement("button"); button.textContent = "停止 Esc";
      const toggle = document.createElement("button"); toggle.textContent = "暂停";
      toggle.addEventListener("pointerdown", event => { event.preventDefault(); togglePause(); });
      toggle.addEventListener("click", event => { if (event.detail === 0) togglePause(); });
      button.addEventListener("pointerdown", event => { event.preventDefault(); stop("已停止输入，已输入的文字会保留。"); });
      button.addEventListener("click", () => { if (job) stop("已停止输入，已输入的文字会保留。"); else host.remove(); });
      box.append(message, toggle, button); shadow.append(style, box);
      (document.documentElement || document.body).append(host);
      overlay = {host, message, button, toggle};
    }
    overlay.message.textContent = "ClipType · " + text;
    overlay.button.textContent = running ? "停止 Esc" : "关闭";
    overlay.toggle.hidden = !running;
    overlay.toggle.textContent = job?.task.state === "paused" ? "继续" : "暂停";
    overlay.host.dataset.cliptypeState = running ? job?.task.state || "countdown" : "idle";
    if (!running) dismissTimer = setTimeout(() => overlay?.host.remove(), 4000);
  }
  function stop(reason) { if (job) { job.cancelled = true; job.reason = reason; } }
  function togglePause() {
    if (!job) return;
    if (job.task.state === "paused") {
      check(job);
      job.task.resume();
    } else job.task.pause();
    showStatus((job.task.state === "paused" ? "已暂停 " : "正在输入 ") + job.task.completed + " / " + job.task.total, true);
  }
  function check(current, checkSelection = true) {
    if (current.cancelled) throw new Error(current.reason || "输入已停止。");
    const {element} = current;
    if (!element.isConnected) throw new Error("输入框已被页面替换，已停止。");
    if (document.hidden || !document.hasFocus()) throw new Error("页面已失去焦点，已停止输入。");
    const active = activeElement();
    if (active !== element && !element.contains(active)) throw new Error("输入焦点已改变，已停止。");
    if (element.disabled || element.readOnly || current.saved.kind === "rich" && !element.isContentEditable)
      throw new Error("输入框已变为只读，已停止。");
    if (checkSelection && current.expected) {
      if (current.saved.kind === "text") {
        if (element.selectionStart !== current.expected.start || element.selectionEnd !== current.expected.end || element.value !== current.expected.value)
          throw new Error("光标位置或内容已改变，已停止。");
      } else {
        const selection = selectionFor(element);
        const range = selection?.rangeCount ? selection.getRangeAt(0) : null;
        const expected = current.expected;
        if (!range || range.startContainer !== expected.startContainer || range.startOffset !== expected.startOffset ||
            range.endContainer !== expected.endContainer || range.endOffset !== expected.endOffset || element.innerHTML !== current.expected.html)
          throw new Error("光标位置已改变，已停止。");
      }
    }
  }
  const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
  async function ready(current) {
    check(current);
    while (current.task.state === "paused") { await pause(20); check(current); }
    check(current);
  }
  async function wait(current, ms) {
    let remaining = Math.max(1, ms);
    while (remaining > 0) {
      await ready(current);
      const before = performance.now();
      await pause(Math.min(20, remaining));
      remaining -= performance.now() - before;
    }
    await ready(current);
  }
  function remember(current) {
    const {element} = current;
    if (current.saved.kind === "text") current.expected = {start: element.selectionStart, end: element.selectionEnd, value: element.value};
    else {
      const selection = selectionFor(element);
      current.expected = selection?.rangeCount ? selection.getRangeAt(0).cloneRange() : null;
      if (!rangeInside(current.expected, element)) throw new Error("页面改变了输入位置，已停止。");
      current.expected.html = element.innerHTML;
    }
  }
  function inputEvent(element, type, data, inputType, cancelable = false) {
    const view = element.ownerDocument.defaultView;
    return element.dispatchEvent(new view.InputEvent(type, {bubbles: true, composed: true, cancelable, data, inputType}));
  }
  function insert(current, text, simulate) {
    const {element} = current;
    const doc = element.ownerDocument;
    const view = doc.defaultView;
    const lineBreak = text === "\n" && current.saved.kind === "rich";
    const inputType = lineBreak ? "insertLineBreak" : "insertText";
    // Never dispatch Enter: a chat application could interpret it as sending a message.
    const sendKey = simulate && text !== "\n";
    try {
      if (sendKey && !element.dispatchEvent(new view.KeyboardEvent("keydown", {key: text, bubbles: true, composed: true, cancelable: true})))
        throw new Error("网页阻止了此次按键，已停止。可尝试桌面版。");
      if (!inputEvent(element, "beforeinput", lineBreak ? null : text, inputType, true))
        throw new Error("网页阻止了文字输入，已停止。");
      check(current);
      if (current.saved.kind === "text") {
        const start = element.selectionStart, end = element.selectionEnd;
        const next = element.value.slice(0, start) + text + element.value.slice(end);
        if (element.maxLength >= 0 && next.length > element.maxLength) throw new Error("已达到输入框字数上限，已停止。");
        const prototype = element.tagName === "TEXTAREA" ? view.HTMLTextAreaElement.prototype : view.HTMLInputElement.prototype;
        Object.getOwnPropertyDescriptor(prototype, "value").set.call(element, next);
        element.setSelectionRange(start + text.length, start + text.length);
        current.expected = {start: start + text.length, end: start + text.length, value: next};
        inputEvent(element, "input", text, "insertText");
      } else {
        let emitted = false;
        let inputError;
        const onInput = event => {
          if (!element.contains(event.target)) return;
          emitted = true;
          try { remember(current); } catch (error) { inputError = error; }
        };
        // Save the browser's insertion before target/document input handlers can move it.
        view.addEventListener("input", onInput, true);
        let success;
        try { success = doc.execCommand(lineBreak ? "insertLineBreak" : "insertText", false, lineBreak ? null : text); }
        finally { view.removeEventListener("input", onInput, true); }
        if (inputError) throw inputError;
        if (!success && !emitted) throw new Error("这个富文本编辑器不接受插入命令，可尝试桌面版。");
        if (!emitted) {
          remember(current);
          inputEvent(element, "input", lineBreak ? null : text, inputType);
        }
      }
    } finally {
      if (sendKey) element.dispatchEvent(new view.KeyboardEvent("keyup", {key: text, bubbles: true, composed: true}));
    }
    check(current);
    remember(current);
  }
  async function run(current) {
    const docs = new Set([document, current.element.ownerDocument]);
    const onKey = event => {
      if (!event.isTrusted) return;
      if (event.altKey && event.shiftKey && event.key.toLowerCase() === "p") return;
      if (event.key === "Escape") { event.preventDefault(); event.stopImmediatePropagation(); stop("已按 Esc 停止输入。"); }
      else if (!["Shift", "Control", "Alt", "Meta"].includes(event.key)) stop("检测到键盘操作，已停止输入。");
    };
    const onPointer = event => { if (event.isTrusted && !event.composedPath().includes(overlay?.host)) stop("检测到鼠标操作，已停止输入。"); };
    const onVisibility = () => { if (document.hidden) stop("已切换标签页，输入已停止。"); };
    try {
      // The action popup closes after acknowledging start. Give focus time to return to the page.
      const deadline = performance.now() + 2000;
      while (!document.hasFocus() && performance.now() < deadline && !current.cancelled) await pause(20);
      // Never restore focus/selection silently: a changed target must fail closed.
      current.expected = current.saved.kind === "text" ? {...current.saved} : current.saved.range.cloneRange();
      if (current.saved.kind === "rich") current.expected.html = current.saved.html;
      check(current);
      for (const doc of docs) { doc.addEventListener("keydown", onKey, true); doc.addEventListener("pointerdown", onPointer, true); }
      document.addEventListener("visibilitychange", onVisibility);
      showStatus("准备输入… 按 Esc 可停止", true);
      await wait(current, current.options.delay);
      current.task.running();
      for (let i = 0; i < current.parts.length; i++) {
        await ready(current);
        const part = current.parts[i];
        insert(current, current.flattenNewlines && part === "\n" ? " " : part, current.simulate);
        current.task.completed = i + 1;
        if ((i + 1) % 5 === 0 || i === current.parts.length - 1)
          showStatus("正在输入 " + (i + 1) + " / " + current.parts.length, true);
        if (i < current.parts.length - 1) await wait(current, core.delayAfter(current.parts[i], current.parts[i + 1], current.options));
      }
      current.task.finish("completed");
      showStatus(current.simulate ? "逐字输入完成" : "文字已插入", false);
    } catch (error) {
      current.task.finish(current.cancelled ? "cancelled" : "failed");
      showStatus(error.message || "输入失败，请重新选择输入框。", false);
    } finally {
      for (const doc of docs) { doc.removeEventListener("keydown", onKey, true); doc.removeEventListener("pointerdown", onPointer, true); }
      document.removeEventListener("visibilitychange", onVisibility);
      if (job === current) job = null;
      captured = null;
      updateQuick();
    }
  }
  function start(message) {
    if (job) throw new Error("已有任务正在输入。");
    if (!captured || message.id !== captured.id || !captured.element.isConnected)
      throw new Error("原输入位置已失效，请重新点击输入框并打开扩展。");
    if (typeof message.text !== "string" || message.text.length > core.MAX_LENGTH)
      throw new Error("单次最多输入 100,000 个字符。");
    const options = core.settings(message.options);
    const simulate = message.mode !== "insert";
    const flattenNewlines = captured.element.tagName === "INPUT" || options.newlines === "space";
    const text = core.normalize(message.text, !simulate && flattenNewlines);
    if (!text.length) throw new Error("没有可以输入的文字。");
    const parts = simulate ? core.graphemes(text) : [text];
    const current = {...captured, options, simulate, flattenNewlines, parts, cancelled: false, task: new core.Session(parts.length)};
    current.task.begin(); job = current;
    updateQuick();
    setTimeout(() => run(current), 80);
    return {ok: true};
  }
  let quick = null;
  let quickEnabled = false;
  function showQuick(visible) {
    if (!quick) return;
    quick.hidden = !visible;
    quick.style.setProperty("display", visible ? "block" : "none", "important");
  }
  function updateQuick() {
    if (!quickEnabled || job) { showQuick(false); return; }
    let element = activeElement();
    try { snapshot(element); } catch { showQuick(false); return; }
    if (!quick) {
      quick = document.createElement("button");
      quick.textContent = "ClipType"; quick.setAttribute("aria-label", "打开 ClipType 输入面板");
      quick.style.cssText = "all:initial;position:fixed;z-index:2147483646;padding:7px 10px;border-radius:7px;background:#2f5ddc;color:white;cursor:pointer;font:12px system-ui";
      quick.addEventListener("pointerdown", event => event.preventDefault());
      quick.addEventListener("click", () => api.runtime.sendMessage({channel:"cliptype",action:"open-panel"}).then(result => {
        if (!result?.ok) showStatus("请点击浏览器工具栏的 ClipType 图标。", false);
      }).catch(() => showStatus("请点击浏览器工具栏的 ClipType 图标。", false)));
      document.documentElement.append(quick);
    }
    const rect = element.getBoundingClientRect();
    showQuick(true);
    quick.style.top = Math.max(4, Math.min(innerHeight - 36, rect.bottom + 4)) + "px";
    quick.style.left = Math.max(4, Math.min(innerWidth - 90, rect.right - 85)) + "px";
  }
  document.addEventListener("focusin", updateQuick);
  document.addEventListener("scroll", updateQuick, true);
  window.addEventListener("resize", updateQuick);
  api.runtime.onMessage.addListener((message, sender, respond) => {
    if (sender.id !== api.runtime.id || !message || message.channel !== "cliptype") return;
    try {
      if (message.action === "capture") respond(capture());
      else if (message.action === "start") respond(start(message));
      else if (message.action === "stop") { stop("已停止输入。"); respond({ok: true}); }
      else if (message.action === "toggle-pause") { togglePause(); respond({ok:true}); }
      else if (message.action === "pause") { if (job) job.task.pause(); showStatus("已暂停", !!job); respond({ok:true}); }
      else if (message.action === "resume") { if (job?.task.state === "paused") togglePause(); respond({ok:true}); }
      else if (message.action === "status") respond({ok:true, state:job?.task.state || "idle", completed:job?.task.completed || 0, total:job?.task.total || 0});
      else if (message.action === "quick") { quickEnabled = message.enabled === true; updateQuick(); respond({ok:true}); }
    } catch (error) { respond({ok: false, error: error.message}); }
  });
})();
