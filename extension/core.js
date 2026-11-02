/* Shared by the popup, isolated content script and Node tests. */
(function (root) {
  "use strict";
  const MAX_LENGTH = 100000;
  const DEFAULTS = Object.freeze({interval: 30, delay: 500, newlines: "preserve", randomize: false,
    variation: 20, punctuation: 0, sentence: 0, paragraph: 0, quickButton: false});
  function bounded(value, min, max, fallback) {
    return Number.isFinite(Number(value)) ? Math.max(min, Math.min(max, Math.round(Number(value)))) : fallback;
  }
  function settings(value = {}) {
    if (!value || typeof value !== "object") value = {};
    return {
      interval: bounded(value.interval ?? DEFAULTS.interval, 0, 500, DEFAULTS.interval),
      delay: bounded(value.delay ?? DEFAULTS.delay, 300, 5000, DEFAULTS.delay),
      newlines: value.newlines === "space" ? "space" : "preserve",
      randomize: value.randomize === true,
      variation: bounded(value.variation ?? DEFAULTS.variation, 0, 90, 20),
      punctuation: bounded(value.punctuation ?? 0, 0, 5000, 0),
      sentence: bounded(value.sentence ?? 0, 0, 5000, 0),
      paragraph: bounded(value.paragraph ?? 0, 0, 10000, 0),
      quickButton: value.quickButton === true
    };
  }
  function normalize(value, singleLine = false) {
    const text = String(value).replace(/\r\n?|[\u2028\u2029]/g, "\n")
      .replace(/\t/g, "    ").replace(/[\x00-\x08\x0B\x0C\x0E-\x1F\x7F-\x9F]/g, "");
    return singleLine ? text.replace(/\n/g, " ") : text;
  }
  function graphemes(text) {
    if (typeof Intl.Segmenter === "function") {
      return Array.from(new Intl.Segmenter(undefined, {granularity: "grapheme"}).segment(text), item => item.segment);
    }
    return Array.from(text);
  }
  function delayAfter(text, next, options, sample = Math.random()) {
    const o = settings(options);
    let delay = Math.round(o.interval * (o.randomize ? 1 + (Math.max(0, Math.min(1, sample)) * 2 - 1) * o.variation / 100 : 1));
    if (text === "\n" && next === "\n") delay += o.paragraph;
    else if (/^[.!?。！？]/u.test(text)) delay += o.sentence;
    else if (/^[,;:，；：、]/u.test(text)) delay += o.punctuation;
    return delay;
  }
  class Session {
    constructor(total) { this.state = "idle"; this.total = total; this.completed = 0; }
    begin() { if (this.state !== "idle") throw new Error("任务已开始"); this.state = "countdown"; }
    running() { if (this.state === "countdown") this.state = "running"; }
    pause() {
      if (!["countdown", "running"].includes(this.state)) return false;
      this.previous = this.state; this.state = "paused"; return true;
    }
    resume() { if (this.state !== "paused") return false; this.state = this.previous; return true; }
    finish(state) {
      if (!["completed", "cancelled", "failed"].includes(state)) throw new Error("Invalid terminal state");
      this.state = state;
    }
  }
  const api = Object.freeze({MAX_LENGTH, DEFAULTS, settings, normalize, graphemes, delayAfter, Session});
  root.ClipTypeCore = api;
  if (typeof module === "object" && module.exports) module.exports = api;
})(globalThis);
