"use strict";
const core = globalThis.ClipTypeCore;
const api = globalThis.browser || globalThis.chrome;
const $ = id => document.getElementById(id);
let session;
let stopped = false;
$("pause").onclick = () => { if (session?.state === "paused") session.resume(); else session?.pause(); };
$("stop").onclick = () => { stopped = true; };
document.addEventListener("keydown", event => { if (event.key === "Escape") stopped = true; });
$("run").onclick = async () => {
  const options = core.settings((await api.storage.local.get("settings")).settings);
  options.interval = core.settings({interval:$("base").value}).interval;
  const parts = core.graphemes(core.normalize($("source").value));
  if (parts.length > core.MAX_LENGTH) { $("progress").textContent = "文字过长"; return; }
  session = new core.Session(parts.length); session.begin(); session.running(); stopped = false;
  $("run").disabled = true; $("pause").disabled = $("stop").disabled = false; $("output").value = "";
  const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
  const ready = async () => { while (!stopped && session.state === "paused") { $("progress").textContent = "已暂停"; await sleep(20); } };
  for (let i = 0; i < parts.length && !stopped; i++) {
    await ready(); if (stopped) break;
    $("output").value += parts[i]; session.completed++;
    $("progress").textContent = session.completed + " / " + parts.length;
    let remaining = core.delayAfter(parts[i], parts[i+1] || "", options);
    while (remaining > 0 && !stopped) { await ready(); const slice = Math.min(20,remaining); await sleep(slice); remaining -= slice; }
  }
  session.finish(stopped ? "cancelled" : "completed");
  $("progress").textContent = (stopped ? "已停止 " : "已完成 ") + session.completed + " / " + parts.length;
  $("run").disabled = false; $("pause").disabled = $("stop").disabled = true;
};
