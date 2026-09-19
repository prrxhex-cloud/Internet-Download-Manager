/**
 * ============================================================================
 * Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
 * PRRX IDM (TM) - Intelligent Download Manager Extension
 * Watermark: PRRX-IDM-EXT-WATERMARK-SECURE-VAULT-2026
 * Confidential and Proprietary - Licensed under PRRX Open Source Initiative
 * ============================================================================
 */
// PRRX IDM Content Script - Universal Floating Video & Telegram Media Grabber

(function () {
  let activePanel = null;
  let scanIntervalId = null;

  // Safe check if Chrome extension context is still active & valid
  function isExtensionValid() {
    try {
      return Boolean(
        typeof chrome !== "undefined" &&
        chrome.runtime &&
        chrome.runtime.id
      );
    } catch (e) {
      return false;
    }
  }

  // Clear timers if extension was reloaded or updated
  function stopPeriodicScanners() {
    if (scanIntervalId) {
      clearInterval(scanIntervalId);
      scanIntervalId = null;
    }
  }

  // 1. Scan for Video & Media Elements (YouTube, Telegram Web, SPAs)
  function scanAndAttachVideoPanels() {
    if (!isExtensionValid()) {
      stopPeriodicScanners();
      return;
    }

    try {
      if (typeof chrome !== "undefined" && chrome.storage && chrome.storage.local) {
        chrome.storage.local.get({ enableFloatingPanel: true }, (items) => {
          if (!isExtensionValid() || (chrome.runtime && chrome.runtime.lastError)) {
            stopPeriodicScanners();
            return;
          }
          if (!items || !items.enableFloatingPanel) return;
          attachToMediaElements();
        });
      } else {
        attachToMediaElements();
      }
    } catch (err) {
      // Context invalidated or detached
      stopPeriodicScanners();
    }
  }

  function attachToMediaElements() {
    if (!isExtensionValid()) return;
    const isTelegram = window.location.hostname.includes("telegram.org");

    // A. Standard Video Elements (YouTube, Twitter, general web)
    const videos = document.querySelectorAll("video");
    videos.forEach((video) => {
      if (video.dataset.prrxAttached) return;
      video.dataset.prrxAttached = "true";

      const container = video.parentElement;
      if (!container) return;

      const computedPos = window.getComputedStyle(container).position;
      if (computedPos === "static") {
        container.style.position = "relative";
      }

      attachFloatingPanel(container, video, "video");
    });

    // B. Telegram Web Media Elements (WebK & WebA)
    if (isTelegram) {
      // Telegram Audio / Voice messages
      const audios = document.querySelectorAll("audio, .audio-player, .voice-message, .bubble-content-voice");
      audios.forEach((audioEl) => {
        if (audioEl.dataset.prrxAttached) return;
        audioEl.dataset.prrxAttached = "true";

        const container = audioEl.closest(".message-content") || audioEl.closest(".bubble-content") || audioEl.parentElement;
        if (!container) return;
        if (window.getComputedStyle(container).position === "static") {
          container.style.position = "relative";
        }
        attachFloatingPanel(container, audioEl, "audio");
      });

      // Telegram Document Files
      const docs = document.querySelectorAll(".document-container, .media-document, .bubble-content-document, .file-container");
      docs.forEach((docEl) => {
        if (docEl.dataset.prrxAttached) return;
        docEl.dataset.prrxAttached = "true";

        if (window.getComputedStyle(docEl).position === "static") {
          docEl.style.position = "relative";
        }
        attachFloatingPanel(docEl, docEl, "document");
      });
    }
  }

  function attachFloatingPanel(container, mediaElement, mediaType = "video") {
    const panel = document.createElement("div");
    panel.className = "prrx-video-panel-container";
    
    let logoUrl = "";
    try {
      if (isExtensionValid() && chrome.runtime && chrome.runtime.getURL) {
        logoUrl = chrome.runtime.getURL("icons/icon16.png");
      }
    } catch (e) {
      // Fallback if context is invalid
    }

    const typeLabel = mediaType === "audio" ? "Download Audio" : mediaType === "document" ? "Download File" : "Download with PRRX";

    panel.innerHTML = `
      <div class="prrx-video-btn" title="Download with PRRX Internet Download Manager">
        ${logoUrl ? `<img src="${logoUrl}" width="16" height="16" alt="PRRX" style="vertical-align: middle; border-radius: 2px;">` : `<span class="prrx-video-icon">⚡</span>`}
        <span>${typeLabel}</span>
        <span style="font-size: 9px; opacity: 0.8;">▼</span>
      </div>
      <div class="prrx-video-dropdown">
        <div class="prrx-dropdown-header">${mediaType === "audio" ? "Audio Extraction" : mediaType === "document" ? "Document Options" : "Select Download Quality"}</div>
        ${mediaType === "audio" ? `
          <div class="prrx-dropdown-item" data-quality="mp3">
            <span>Extract MP3 Audio</span>
            <span class="prrx-item-badge" style="color: #00FF88; background: rgba(0,255,136,0.15)">320K</span>
          </div>
          <div class="prrx-dropdown-item" data-quality="original">
            <span>Original Audio</span>
            <span class="prrx-item-badge">OGG/M4A</span>
          </div>
        ` : mediaType === "document" ? `
          <div class="prrx-dropdown-item" data-quality="original">
            <span>Download Original File</span>
            <span class="prrx-item-badge" style="color: #00FF88; background: rgba(0,255,136,0.15)">Fiber</span>
          </div>
        ` : `
          <div class="prrx-dropdown-item" data-quality="1080p">
            <span>1080p Full HD</span>
            <span class="prrx-item-badge">MP4</span>
          </div>
          <div class="prrx-dropdown-item" data-quality="720p">
            <span>720p HD</span>
            <span class="prrx-item-badge">MP4</span>
          </div>
          <div class="prrx-dropdown-item" data-quality="480p">
            <span>480p</span>
            <span class="prrx-item-badge">MP4</span>
          </div>
          <div class="prrx-dropdown-item" data-quality="360p">
            <span>360p</span>
            <span class="prrx-item-badge">MP4</span>
          </div>
          <div class="prrx-dropdown-item" data-quality="mp3">
            <span>Extract MP3 Audio</span>
            <span class="prrx-item-badge" style="color: #00FF88; background: rgba(0,255,136,0.15)">320K</span>
          </div>
        `}
      </div>
    `;

    const btn = panel.querySelector(".prrx-video-btn");
    const dropdown = panel.querySelector(".prrx-video-dropdown");

    btn.addEventListener("click", (e) => {
      e.stopPropagation();
      dropdown.classList.toggle("show");
    });

    panel.querySelectorAll(".prrx-dropdown-item").forEach((item) => {
      item.addEventListener("click", (e) => {
        e.stopPropagation();
        dropdown.classList.remove("show");

        if (!isExtensionValid()) {
          console.info("PRRX IDM: Extension was updated. Refresh the page to use the floating grabber.");
          return;
        }

        let targetUrl = mediaElement.currentSrc || mediaElement.src;
        if (!targetUrl && mediaElement.querySelector) {
          const innerMedia = mediaElement.querySelector("video, audio, source, a[href]");
          if (innerMedia) targetUrl = innerMedia.currentSrc || innerMedia.src || innerMedia.href;
        }
        if (!targetUrl) targetUrl = window.location.href;

        let detectedTitle = document.title || "media_file";
        const titleEl = container.querySelector(".document-title, .audio-title, .title, .name");
        if (titleEl && titleEl.textContent) {
          detectedTitle = titleEl.textContent.trim();
        }

        try {
          chrome.runtime.sendMessage({
            action: "download_video",
            url: targetUrl,
            quality: item.dataset.quality,
            fileName: detectedTitle
          }, () => {
            if (chrome.runtime && chrome.runtime.lastError) {
              // Ignore or handle cleanly
            }
          });
        } catch (e) {
          console.debug("PRRX IDM: Could not send message to background worker:", e.message);
        }
      });
    });

    document.addEventListener("click", () => {
      dropdown.classList.remove("show");
    });

    container.appendChild(panel);
  }

  // Periodic scanner for dynamically loaded videos and Telegram messages
  scanIntervalId = setInterval(scanAndAttachVideoPanels, 2000);
  scanAndAttachVideoPanels();

  // 2. Link Collector for "Download All Links with PRRX IDM"
  try {
    if (isExtensionValid() && chrome.runtime && chrome.runtime.onMessage) {
      chrome.runtime.onMessage.addListener((request, sender, sendResponse) => {
        if (!isExtensionValid()) return false;
        if (request && request.action === "collect_all_links") {
          const links = [];
          const seenUrls = new Set();
          const anchors = document.querySelectorAll("a[href]");

          anchors.forEach((a) => {
            const href = a.href;
            if (!href || href.startsWith("javascript:") || href.startsWith("#") || seenUrls.has(href)) {
              return;
            }
            seenUrls.add(href);

            const cleanUrl = href.split("?")[0].split("#")[0];
            const lastSeg = cleanUrl.substring(cleanUrl.lastIndexOf("/") + 1);
            const ext = lastSeg.includes(".") ? lastSeg.substring(lastSeg.lastIndexOf(".")) : "";
            const filename = lastSeg || "download.bin";

            links.push({
              url: href,
              fileName: decodeURIComponent(filename),
              extension: ext.toLowerCase(),
              linkText: (a.innerText || a.title || filename).trim().substring(0, 60),
              fileSizeFormatted: "Pending...",
              isSelected: true
            });
          });

          try {
            sendResponse({ links: links.slice(0, 200) });
          } catch (_) { }
        }
        return true;
      });
    }
  } catch (e) {
    // Gracefully handle listener setup failure in invalidated context
  }
})();
