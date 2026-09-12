/**
 * ============================================================================
 * Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
 * PRRX IDM (TM) - Intelligent Download Manager Extension
 * Watermark: PRRX-IDM-EXT-WATERMARK-SECURE-VAULT-2026
 * Confidential and Proprietary - Licensed under PRRX Open Source Initiative
 * ============================================================================
 */
// PRRX IDM Integration Module - High-Reliability Dual-Channel Background Worker
const HOST_NAME = "com.prrx.idm";
const HTTP_BRIDGE_URL = "http://127.0.0.1:46543/api/download";

// 1. Register Context Menus Safely
function setupContextMenus() {
  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({
      id: "prrx_download_link",
      title: "Download with PRRX IDM",
      contexts: ["link", "video", "audio", "image", "selection"]
    }, () => {
      if (chrome.runtime.lastError) {
        // Silently ignore if already created
      }
    });

    chrome.contextMenus.create({
      id: "prrx_download_all",
      title: "Download All Links with PRRX IDM",
      contexts: ["page"]
    }, () => {
      if (chrome.runtime.lastError) {
        // Silently ignore if already created
      }
    });
  });
}

chrome.runtime.onInstalled.addListener(() => {
  setupContextMenus();
});

chrome.runtime.onStartup.addListener(() => {
  setupContextMenus();
});

// Also ensure context menus are initialized when service worker wakes up
setupContextMenus();

// 2. Handle Context Menu Clicks
chrome.contextMenus.onClicked.addListener((info, tab) => {
  if (info.menuItemId === "prrx_download_link") {
    // Robust URL resolution: linkUrl -> srcUrl -> selected URL -> pageUrl
    let targetUrl = info.linkUrl || info.srcUrl;
    if (!targetUrl && info.selectionText) {
      const selected = info.selectionText.trim();
      if (selected.startsWith("http://") || selected.startsWith("https://")) {
        targetUrl = selected;
      }
    }
    if (!targetUrl) {
      targetUrl = info.pageUrl;
    }

    if (targetUrl) {
      console.log("PRRX IDM: Captured URL from context menu:", targetUrl);
      sendToPrrxIdm({
        action: "download",
        url: targetUrl,
        pageTitle: tab?.title || "Web Link"
      });
    }
  } else if (info.menuItemId === "prrx_download_all") {
    if (tab?.id) {
      chrome.tabs.sendMessage(tab.id, { action: "collect_all_links" }, (response) => {
        if (response && response.links && response.links.length > 0) {
          sendToPrrxIdm({
            action: "batch",
            url: tab.url,
            pageTitle: tab.title,
            links: response.links
          });
        }
      });
    }
  }
});

// 3. Intercept Standard Browser Downloads
chrome.downloads.onDeterminingFilename.addListener((downloadItem, suggest) => {
  chrome.storage.local.get({ enableInterception: true }, (items) => {
    if (items.enableInterception && downloadItem.url && !downloadItem.url.startsWith("blob:") && !downloadItem.url.startsWith("data:")) {
      chrome.downloads.cancel(downloadItem.id, () => {
        chrome.downloads.erase({ id: downloadItem.id });
      });

      sendToPrrxIdm({
        action: "download",
        url: downloadItem.finalUrl || downloadItem.url,
        fileName: downloadItem.filename || "",
        totalBytes: downloadItem.fileSize || 0
      });
    } else {
      suggest();
    }
  });
  return true;
});

// 4. Handle Messages from Content Script (e.g. Floating Video Panel)
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message.action === "download_video" || message.action === "download_url") {
    sendToPrrxIdm({
      action: "download",
      url: message.url,
      fileName: message.fileName || "",
      pageTitle: sender.tab?.title || ""
    });
    sendResponse({ status: "sent" });
  }
  return true;
});

// 5. Dual-Channel Transmitter: Fast Local HTTP Bridge + Native Messaging Fallback
async function sendToPrrxIdm(payload) {
  try {
    // Channel A: Zero-latency Localhost HTTP Server (100% reliable across all browsers)
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), 1200);

    const response = await fetch(HTTP_BRIDGE_URL, {
      method: "POST",
      headers: {
        "Content-Type": "application/json"
      },
      body: JSON.stringify(payload),
      signal: controller.signal
    });
    clearTimeout(timeoutId);

    if (response.ok) {
      console.log("PRRX IDM: Successfully dispatched via high-speed HTTP bridge.");
      return;
    }
  } catch (err) {
    console.log("PRRX IDM: Local HTTP bridge offline or timed out, falling back to Native Messaging Host...");
  }

  // Channel B: Windows Native Messaging Host Fallback (launches application if closed)
  try {
    chrome.runtime.sendNativeMessage(HOST_NAME, payload, (resp) => {
      if (chrome.runtime.lastError) {
        console.warn("Native Messaging Notice:", chrome.runtime.lastError.message);
      } else {
        console.log("PRRX IDM: Dispatched via Native Messaging Host.", resp);
      }
    });
  } catch (err) {
    console.error("PRRX IDM: Both dispatch channels encountered error:", err);
  }
}
