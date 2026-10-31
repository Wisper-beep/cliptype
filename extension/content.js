(() => {
  "use strict";
  if (globalThis.__clipTypeInstalled) return;
  globalThis.__clipTypeInstalled = true;
  const core = globalThis.ClipTypeCore;
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
      return {kind: "text", start: element.selectionStart, end: element.selectionEnd, direction: element.selectionDirection};
    }
    if (element.isContentEditable) {
      let host = element;
      while (host.parentElement?.isContentEditable) host = host.parentElement;
      const selection = selectionFor(host);
      if (!selection?.rangeCount || !rangeInside(selection.getRangeAt(0), host))
        throw new Error("请先在富文本输入区放好光标。");
      return {kind: "rich", host, range: selection.getRangeAt(0).cloneRange()};
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
      button.addEventListener("pointerdown", event => { event.preventDefault(); stop("已停止输入，已输入的文字会保留。"); });
      button.addEventListener("click", () => { if (job) stop("已停止输入，已输入的文字会保留。"); else host.remove(); });
      box.append(message, button); shadow.append(style, box);
      (document.documentElement || document.body).append(host);
      overlay = {host, message, button};
    }
    overlay.message.textContent = "ClipType · " + text;
    overlay.button.textContent = running ? "停止 Esc" : "关闭";
    if (!running) dismissTimer = setTimeout(() => overlay?.host.remove(), 4000);
  }
  function stop(reason) { if (job) { job.cancelled = true; job.reason = reason; } }
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
        if (element.selectionStart !== current.expected.start || element.selectionEnd !== current.expected.end)
          throw new Error("光标位置已改变，已停止。");
      } else {
        const selection = selectionFor(element);
        const range = selection?.rangeCount ? selection.getRangeAt(0) : null;
        const expected = current.expected;
        if (!range || range.startContainer !== expected.startContainer || range.startOffset !== expected.startOffset ||
            range.endContainer !== expected.endContainer || range.endOffset !== expected.endOffset)
          throw new Error("光标位置已改变，已停止。");
      }
    }
  }
  const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
  async function wait(current, ms) {
    const end = performance.now() + ms;
    do {
      check(current);
      await pause(Math.max(1, Math.min(20, end - performance.now())));
    } while (performance.now() < end);
  }
  function remember(current) {
    const {element} = current;
    if (current.saved.kind === "text") current.expected = {start: element.selectionStart, end: element.selectionEnd};
    else {
      const selection = selectionFor(element);
      current.expected = selection?.rangeCount ? selection.getRangeAt(0).cloneRange() : null;
      if (!rangeInside(current.expected, element)) throw new Error("页面改变了输入位置，已停止。");
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
    if (sendKey && !element.dispatchEvent(new view.KeyboardEvent("keydown", {key: text, bubbles: true, composed: true, cancelable: true})))
      throw new Error("网页阻止了此次按键，已停止。可尝试桌面版。");
    try {
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
        inputEvent(element, "input", text, "insertText");
      } else {
        let emitted = false;
        const onInput = () => { emitted = true; };
        element.addEventListener("input", onInput);
        let success;
        try { success = doc.execCommand(lineBreak ? "insertLineBreak" : "insertText", false, lineBreak ? null : text); }
        finally { element.removeEventListener("input", onInput); }
        if (!success && !emitted) throw new Error("这个富文本编辑器不接受插入命令，可尝试桌面版。");
        if (!emitted) inputEvent(element, "input", lineBreak ? null : text, inputType);
      }
    } finally {
      if (sendKey) element.dispatchEvent(new view.KeyboardEvent("keyup", {key: text, bubbles: true, composed: true}));
    }
    remember(current);
  }
  async function run(current) {
    const docs = new Set([document, current.element.ownerDocument]);
    const onKey = event => {
      if (!event.isTrusted) return;
      if (event.key === "Escape") { event.preventDefault(); event.stopImmediatePropagation(); stop("已按 Esc 停止输入。"); }
      else if (!["Shift", "Control", "Alt", "Meta"].includes(event.key)) stop("检测到键盘操作，已停止输入。");
    };
    const onPointer = event => { if (event.isTrusted) stop("检测到鼠标操作，已停止输入。"); };
    const onVisibility = () => { if (document.hidden) stop("已切换标签页，输入已停止。"); };
    try {
      // The action popup closes after acknowledging start. Give focus time to return to the page.
      const deadline = performance.now() + 2000;
      while (!document.hasFocus() && performance.now() < deadline && !current.cancelled) await pause(20);
      current.element.focus({preventScroll: true});
      check(current, false);
      if (current.saved.kind === "text") {
        current.element.setSelectionRange(current.saved.start, current.saved.end, current.saved.direction);
      } else {
        const selection = selectionFor(current.element);
        selection.removeAllRanges(); selection.addRange(current.saved.range);
      }
      remember(current);
      for (const doc of docs) { doc.addEventListener("keydown", onKey, true); doc.addEventListener("pointerdown", onPointer, true); }
      document.addEventListener("visibilitychange", onVisibility);
      showStatus("准备输入… 按 Esc 可停止", true);
      await wait(current, current.options.delay);
      for (let i = 0; i < current.parts.length; i++) {
        check(current);
        insert(current, current.parts[i], current.simulate);
        if ((i + 1) % 5 === 0 || i === current.parts.length - 1)
          showStatus("正在输入 " + (i + 1) + " / " + current.parts.length, true);
        if (i < current.parts.length - 1) await wait(current, Math.max(1, current.options.interval));
      }
      showStatus(current.simulate ? "逐字输入完成" : "文字已插入", false);
    } catch (error) {
      showStatus(error.message || "输入失败，请重新选择输入框。", false);
    } finally {
      for (const doc of docs) { doc.removeEventListener("keydown", onKey, true); doc.removeEventListener("pointerdown", onPointer, true); }
      document.removeEventListener("visibilitychange", onVisibility);
      if (job === current) job = null;
      captured = null;
    }
  }
  function start(message) {
    if (job) throw new Error("已有任务正在输入。");
    if (!captured || message.id !== captured.id || !captured.element.isConnected)
      throw new Error("原输入位置已失效，请重新点击输入框并打开扩展。");
    if (typeof message.text !== "string" || message.text.length > core.MAX_LENGTH)
      throw new Error("单次最多输入 100,000 个字符。");
    const options = core.settings(message.options);
    const text = core.normalize(message.text, captured.element.tagName === "INPUT" || options.newlines === "space");
    if (!text.length) throw new Error("没有可以输入的文字。");
    const simulate = message.mode !== "insert";
    job = {...captured, options, simulate, parts: simulate ? core.graphemes(text) : [text], cancelled: false};
    setTimeout(() => run(job), 80);
    return {ok: true};
  }
  chrome.runtime.onMessage.addListener((message, sender, respond) => {
    if (sender.id !== chrome.runtime.id || !message || message.channel !== "cliptype") return;
    try {
      if (message.action === "capture") respond(capture());
      else if (message.action === "start") respond(start(message));
      else if (message.action === "stop") { stop("已停止输入。"); respond({ok: true}); }
    } catch (error) { respond({ok: false, error: error.message}); }
  });
})();
