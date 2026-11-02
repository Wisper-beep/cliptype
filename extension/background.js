"use strict";
const api = globalThis.browser || globalThis.chrome;
api.runtime.onInstalled.addListener(() => {
  // Only extension pages need settings access; page scripts never read stored settings.
  if (api.storage.local.setAccessLevel) api.storage.local.setAccessLevel({accessLevel: "TRUSTED_CONTEXTS"}).catch(() => {});
});
api.commands.onCommand.addListener(async command => {
  if (command !== "toggle-pause") return;
  const [tab] = await api.tabs.query({active:true,currentWindow:true});
  if (tab?.id) api.tabs.sendMessage(tab.id, {channel:"cliptype",action:"toggle-pause"}).catch(() => {});
});
api.runtime.onMessage.addListener((message, sender, respond) => {
  if (sender.id !== api.runtime.id || !sender.tab || message?.channel !== "cliptype" || message.action !== "open-panel") return;
  if (!api.action.openPopup) { respond({ok:false}); return; }
  api.action.openPopup().then(() => respond({ok:true}), () => respond({ok:false}));
  return true;
});
