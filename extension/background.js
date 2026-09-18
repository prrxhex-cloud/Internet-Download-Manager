/**
 * ============================================================================
 * Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
 * PRRX IDM (TM) - Intelligent Download Manager Extension
 * Watermark: PRRX-IDM-EXT-WATERMARK-SECURE-VAULT-2026
 * Confidential and Proprietary - Licensed under PRRX Open Source Initiative
 * ============================================================================
 */
// PRRX IDM Integration Module v1.4.0 - High-Reliability Dual-Channel Background Worker
const HOST_NAME = "com.prrx.idm";
const HTTP_BRIDGE_URL = "http://127.0.0.1:46543/api/download";
const HTTP_PING_URL = "http://127.0.0.1:46543/api/ping";
const CLOUD_API_BASE = "https://prrx-api.sayurusenavirathna70.workers.dev";

// 1. Register Context Menus Safely
function setupContextMenus() {
  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({
      id: "prrx_download_link",
      title: "Download with PRRX IDM",
      contexts: ["link", "video", "audio", "image", "selection"]
    }, () => {
      if (chrome.runtime.lastError) {
        // Silently ignore if already registered
      }
    });

    chrome.contextMenus.create({
      id: "prrx_download_all",
      title: "Download All Links with PRRX IDM",
      contexts: ["page"]
    }, () => {
      if (chrome.runtime.lastError) {
        // Silently ignore if already registered
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

// 2. Cloud Intelligence & Health Query Utilities
async function checkCloudHealth() {
  try {
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), 1800);
    const res = await fetch(`${CLOUD_API_BASE}/api/health`, {
      method: "GET",
      headers: { "Accept": "application/json" },
      signal: controller.signal
    });
    clearTimeout(timeoutId);
    if (res.ok) {
      return await res.json();
    }
  } catch (err) {
    console.debug("PRRX IDM: Cloud health check offline or timed out:", err.message);
  }
  return { status: "offline" };
}

async function checkDomainHealth(domain) {
  if (!domain) return null;
  try {
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), 1500);
    const cleanDomain = domain.replace(/^www\./i, "").trim();
    const res = await fetch(`${CLOUD_API_BASE}/api/domain/health?domain=${encodeURIComponent(cleanDomain)}`, {
      method: "GET",
      headers: { "Accept": "application/json" },
      signal: controller.signal
    });
    clearTimeout(timeoutId);
    if (res.ok) {
      return await res.json();
    }
  } catch (err) {
    console.debug("PRRX IDM: Domain health check skipped or timed out:", err.message);
  }
  return null;
}

async function checkFileReputation(sha256Hash) {
  if (!sha256Hash) return null;
  try {
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), 1500);
    const res = await fetch(`${CLOUD_API_BASE}/api/reputation?hash=${encodeURIComponent(sha256Hash)}`, {
      method: "GET",
      headers: { "Accept": "application/json" },
      signal: controller.signal
    });
    clearTimeout(timeoutId);
    if (res.ok) {
      return await res.json();
    }
  } catch (err) {
    console.debug("PRRX IDM: File reputation check skipped:", err.message);
  }
  return null;
}

async function checkDesktopBridge() {
  try {
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), 1200);
    const res = await fetch(HTTP_PING_URL, {
      method: "GET",
      signal: controller.signal
    });
    clearTimeout(timeoutId);
    if (res.ok) {
      return await res.json();
    }
  } catch (err) {
    // Desktop bridge offline
  }
  return { status: "offline" };
}

// 3. Handle Context Menu Clicks
chrome.contextMenus.onClicked.addListener(async (info, tab) => {
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
      let cookiesStr = "";
      try {
        if (chrome.cookies) {
          const cookieList = await chrome.cookies.getAll({ url: targetUrl });
          if (cookieList && cookieList.length > 0) {
            cookiesStr = cookieList.map(c => `${c.name}=${c.value}`).join("; ");
          }
        }
      } catch (e) { }

      sendToPrrxIdm({
        action: "download",
        url: targetUrl,
        pageTitle: tab?.title || "Web Link",
        referer: tab?.url || info.pageUrl || "",
        userAgent: navigator.userAgent,
        cookies: cookiesStr
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
            referer: tab.url,
            userAgent: navigator.userAgent,
            links: response.links
          });
        }
      });
    }
  }
});

// 4. Intercept Standard Browser Downloads
chrome.downloads.onDeterminingFilename.addListener((downloadItem, suggest) => {
  chrome.storage.local.get({ enableInterception: true }, async (items) => {
    if (items.enableInterception && downloadItem.url) {
      let targetUrl = downloadItem.finalUrl || downloadItem.url;

      // Safeguard against in-browser blob memory streams (e.g. YouTube media buffer)
      if (targetUrl.startsWith("blob:") || targetUrl.startsWith("data:")) {
        if (downloadItem.referrer && (downloadItem.referrer.startsWith("http://") || downloadItem.referrer.startsWith("https://"))) {
          targetUrl = downloadItem.referrer;
        } else {
          // Direct browser memory blob cannot be re-fetched by external process
          suggest();
          return;
        }
      }

      chrome.downloads.cancel(downloadItem.id, () => {
        chrome.downloads.erase({ id: downloadItem.id });
      });

      let detectedSize = 0;
      if (typeof downloadItem.fileSize === 'number' && downloadItem.fileSize > 0) {
        detectedSize = downloadItem.fileSize;
      } else if (typeof downloadItem.totalBytes === 'number' && downloadItem.totalBytes > 0) {
        detectedSize = downloadItem.totalBytes;
      }

      let cookiesStr = "";
      try {
        if (chrome.cookies) {
          const cookieList = await chrome.cookies.getAll({ url: targetUrl });
          if (cookieList && cookieList.length > 0) {
            cookiesStr = cookieList.map(c => `${c.name}=${c.value}`).join("; ");
          }
        }
      } catch (e) { }

      sendToPrrxIdm({
        action: "download",
        url: targetUrl,
        fileName: downloadItem.filename || "",
        totalBytes: detectedSize,
        referer: downloadItem.referrer || "",
        userAgent: navigator.userAgent,
        cookies: cookiesStr
      });
    } else {
      suggest();
    }
  });
  return true;
});

// 5. Handle Messages from Content Script & Popup
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message.action === "download_video" || message.action === "download_url") {
    (async () => {
      let cookiesStr = "";
      try {
        if (chrome.cookies && message.url) {
          const cookieList = await chrome.cookies.getAll({ url: message.url });
          if (cookieList && cookieList.length > 0) {
            cookiesStr = cookieList.map(c => `${c.name}=${c.value}`).join("; ");
          }
        }
      } catch (e) { }

      sendToPrrxIdm({
        action: "download",
        url: message.url,
        fileName: message.fileName || "",
        pageTitle: sender.tab?.title || "",
        referer: message.referer || sender.tab?.url || "",
        userAgent: navigator.userAgent,
        cookies: cookiesStr
      });
      sendResponse({ status: "sent" });
    })();
    return true;
  } else if (message.action === "ping_desktop") {
    checkDesktopBridge().then((res) => sendResponse(res)).catch(() => sendResponse({ status: "offline" }));
    return true;
  } else if (message.action === "get_cloud_status") {
    checkCloudHealth().then((res) => sendResponse(res)).catch(() => sendResponse({ status: "offline" }));
    return true;
  } else if (message.action === "check_domain_health") {
    checkDomainHealth(message.domain).then((res) => sendResponse(res)).catch(() => sendResponse(null));
    return true;
  } else if (message.action === "sync_desktop") {
    fetch("http://127.0.0.1:46543/api/sync", { method: "GET" })
      .then(res => res.json())
      .then(data => sendResponse({ status: "ok", data }))
      .catch(() => sendResponse({ status: "offline" }));
    return true;
  }
  return true;
});

// 6. Dual-Channel Transmitter: Fast Local HTTP Bridge + Native Messaging Fallback
async function sendToPrrxIdm(payload) {
  if (payload && !payload.userAgent) {
    payload.userAgent = navigator.userAgent;
  }

  // Pre-flight cloud domain health & reputation check
  if (payload && payload.url) {
    try {
      const parsedUrl = new URL(payload.url);
      const domainHealth = await checkDomainHealth(parsedUrl.hostname);
      if (domainHealth) {
        payload.domainHealth = domainHealth;
        console.log(`PRRX IDM: Domain health verified for ${parsedUrl.hostname}:`, domainHealth.status);
      }
    } catch (e) {
      // Ignore URL parsing errors for non-standard links
    }
  }

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
