/**
 * ============================================================================
 * Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
 * PRRX IDM (TM) - Intelligent Download Manager Extension
 * Watermark: PRRX-IDM-EXT-WATERMARK-SECURE-VAULT-2026
 * Confidential and Proprietary - Licensed under PRRX Open Source Initiative
 * ============================================================================
 */
document.addEventListener("DOMContentLoaded", () => {
  const chkInterception = document.getElementById("chkInterception");
  const chkFloatingPanel = document.getElementById("chkFloatingPanel");

  const dotDesktop = document.getElementById("dotDesktop");
  const txtDesktopStatus = document.getElementById("txtDesktopStatus");
  const dotCloud = document.getElementById("dotCloud");
  const txtCloudStatus = document.getElementById("txtCloudStatus");

  // Load preferences
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

  // Query Desktop Bridge Status
  chrome.runtime.sendMessage({ action: "ping_desktop" }, (response) => {
    if (chrome.runtime.lastError || !response || response.status !== "online") {
      dotDesktop.className = "status-dot offline";
      txtDesktopStatus.textContent = "Offline (Standby)";
    } else {
      dotDesktop.className = "status-dot online";
      txtDesktopStatus.textContent = `Online (v${response.version || "1.4.0"})`;
    }
  });

  // Query Cloudflare Edge & D1 Status
  chrome.runtime.sendMessage({ action: "get_cloud_status" }, (response) => {
    if (chrome.runtime.lastError || !response || (response.status !== "online" && response.status !== "healthy")) {
      dotCloud.className = "status-dot offline";
      txtCloudStatus.textContent = "Edge Standby";
    } else {
      dotCloud.className = "status-dot online";
      const edge = response.edge_node ? ` (${response.edge_node})` : "";
      txtCloudStatus.textContent = `Active${edge}`;
    }
  });
});
