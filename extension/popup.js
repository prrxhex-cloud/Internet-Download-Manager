document.addEventListener("DOMContentLoaded", () => {
  const chkInterception = document.getElementById("chkInterception");
  const chkFloatingPanel = document.getElementById("chkFloatingPanel");

  chrome.storage.local.get({ enableInterception: true, enableFloatingPanel: true }, (items) => {
    chkInterception.checked = items.enableInterception;
    chkFloatingPanel.checked = items.enableFloatingPanel;
  });

  chkInterception.addEventListener("change", () => {
    chrome.storage.local.set({ enableInterception: chkInterception.checked });
  });

  chkFloatingPanel.addEventListener("change", () => {
    chrome.storage.local.set({ enableFloatingPanel: chkFloatingPanel.checked });
  });
});
