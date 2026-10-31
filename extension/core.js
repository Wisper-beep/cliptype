/* Shared by the popup, isolated content script and Node tests. */
(function (root) {
  "use strict";
  const MAX_LENGTH = 100000;
  const DEFAULTS = Object.freeze({interval: 30, delay: 500, newlines: "preserve"});
  function bounded(value, min, max, fallback) {
    return Number.isFinite(Number(value)) ? Math.max(min, Math.min(max, Math.round(Number(value)))) : fallback;
  }
  function settings(value = {}) {
    if (!value || typeof value !== "object") value = {};
    return {
      interval: bounded(value.interval ?? DEFAULTS.interval, 0, 500, DEFAULTS.interval),
      delay: bounded(value.delay ?? DEFAULTS.delay, 300, 5000, DEFAULTS.delay),
      newlines: value.newlines === "space" ? "space" : "preserve"
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
  const api = Object.freeze({MAX_LENGTH, DEFAULTS, settings, normalize, graphemes});
  root.ClipTypeCore = api;
  if (typeof module === "object" && module.exports) module.exports = api;
})(globalThis);
