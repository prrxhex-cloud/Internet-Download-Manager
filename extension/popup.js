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
  const dotSync = document.getElementById("dotSync");
  const txtSyncStatus = document.getElementById("txtSyncStatus");

  const HTTP_PING_URL = "http://127.0.0.1:46543/api/ping";
  const HTTP_SYNC_URL = "http://127.0.0.1:46543/api/sync";
  const CLOUD_API_HEALTH = "https://prrx-api.sayurusenavirathna70.workers.dev/api/health";

  // Load preferences
  chrome.storage.local.get({ enableInterception: true, enableFloatingPanel: true }, (items) => {
    chkInterception.checked = items.enableInterception;
    chkFloatingPanel.checked = items.enableFloatingPanel;
  });

  chkInterception.addEventListener("change", () => {
    const val = chkInterception.checked;
    chrome.storage.local.set({ enableInterception: val });
    // Push sync to desktop
    syncWithDesktop({ enableInterception: val });
  });

  chkFloatingPanel.addEventListener("change", () => {
    chrome.storage.local.set({ enableFloatingPanel: chkFloatingPanel.checked });
  });

  // Fast direct desktop check with timeout
  async function queryDesktopStatus() {
    try {
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), 1500);
      const res = await fetch(HTTP_PING_URL, { method: "GET", signal: controller.signal });
      clearTimeout(timeoutId);
      if (res.ok) {
        const data = await res.json();
        return { status: "online", version: data.version || "1.6.0" };
      }
    } catch (e) {
      // Direct fetch failed, try background service worker fallback
    }

    return new Promise((resolve) => {
      const timer = setTimeout(() => resolve({ status: "offline" }), 1200);
      try {
        chrome.runtime.sendMessage({ action: "ping_desktop" }, (resp) => {
          clearTimeout(timer);
          if (chrome.runtime.lastError || !resp || resp.status !== "online") {
            resolve({ status: "offline" });
          } else {
            resolve(resp);
          }
        });
      } catch (err) {
        clearTimeout(timer);
        resolve({ status: "offline" });
      }
    });
  }

  // Fast direct Cloudflare check with timeout
  async function queryCloudStatus() {
    try {
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), 1800);
      const res = await fetch(CLOUD_API_HEALTH, {
        method: "GET",
        headers: { "Accept": "application/json" },
        signal: controller.signal
      });
      clearTimeout(timeoutId);
      if (res.ok) {
        return await res.json();
      }
    } catch (e) {
      // Direct fetch failed, try background fallback
    }

    return new Promise((resolve) => {
      const timer = setTimeout(() => resolve({ status: "offline" }), 1500);
      try {
        chrome.runtime.sendMessage({ action: "get_cloud_status" }, (resp) => {
          clearTimeout(timer);
          if (chrome.runtime.lastError || !resp) {
            resolve({ status: "offline" });
          } else {
            resolve(resp);
          }
        });
      } catch (err) {
        clearTimeout(timer);
        resolve({ status: "offline" });
      }
    });
  }

  // Auto-sync configuration and state with desktop IDM engine
  async function syncWithDesktop(payloadToPush = null) {
    if (dotSync && txtSyncStatus) {
      dotSync.className = "status-dot";
      txtSyncStatus.textContent = "Syncing...";
    }

    try {
      const controller = new AbortController();
      const timeoutId = setTimeout(() => controller.abort(), 1500);
      const options = {
        method: payloadToPush ? "POST" : "GET",
        signal: controller.signal
      };
      if (payloadToPush) {
        options.headers = { "Content-Type": "application/json" };
        options.body = JSON.stringify(payloadToPush);
      }

      const res = await fetch(HTTP_SYNC_URL, options);
      clearTimeout(timeoutId);
      if (res.ok) {
        const data = await res.json();
        if (dotSync && txtSyncStatus) {
          dotSync.className = "status-dot online";
          txtSyncStatus.textContent = "Synced";
        }
        return data;
      }
    } catch (e) { }

    if (dotSync && txtSyncStatus) {
      dotSync.className = "status-dot offline";
      txtSyncStatus.textContent = "Standby";
    }
    return null;
  }

  // Execute status verifications in parallel
  queryDesktopStatus().then((res) => {
    if (res.status === "online") {
      dotDesktop.className = "status-dot online";
      txtDesktopStatus.textContent = `Online (v${res.version || "1.6.0"})`;
      syncWithDesktop();
    } else {
      dotDesktop.className = "status-dot offline";
      txtDesktopStatus.textContent = "Offline (Standby)";
      if (dotSync && txtSyncStatus) {
        dotSync.className = "status-dot offline";
        txtSyncStatus.textContent = "Standby";
      }
    }
  });

  queryCloudStatus().then((res) => {
    if (res && (res.status === "online" || res.status === "healthy")) {
      dotCloud.className = "status-dot online";
      const edge = res.edge_node ? ` (${res.edge_node})` : "";
      txtCloudStatus.textContent = `Connected${edge}`;
    } else {
      dotCloud.className = "status-dot offline";
      txtCloudStatus.textContent = "Edge Standby";
    }
  });
});
