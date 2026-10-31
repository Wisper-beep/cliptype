"use strict";
chrome.runtime.onInstalled.addListener(() => {
  // Only extension pages need settings access; page scripts never read stored settings.
  chrome.storage.local.setAccessLevel({accessLevel: "TRUSTED_CONTEXTS"});
});
