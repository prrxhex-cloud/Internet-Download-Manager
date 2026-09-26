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
    
    // Restore persistent user-dragged coordinates if available
    try {
      if (isExtensionValid() && chrome.storage && chrome.storage.local) {
        chrome.storage.local.get({ floatingPanelPos: null }, (res) => {
          if (res && res.floatingPanelPos && typeof res.floatingPanelPos.x === "number") {
            panel.style.position = "fixed";
            panel.style.left = `${Math.max(10, Math.min(window.innerWidth - 180, res.floatingPanelPos.x))}px`;
            panel.style.top = `${Math.max(10, Math.min(window.innerHeight - 50, res.floatingPanelPos.y))}px`;
            panel.style.right = "auto";
            panel.style.bottom = "auto";
          }
        });
      }
    } catch (_) {}

    let logoUrl = "";
    try {
      if (isExtensionValid() && chrome.runtime && chrome.runtime.getURL) {
        logoUrl = chrome.runtime.getURL("icons/icon16.png");
      }
    } catch (e) { }

    const typeLabel = mediaType === "audio" ? "Download Audio" : mediaType === "document" ? "Download File" : "Download with PRRX";

    panel.innerHTML = `
      <div class="prrx-video-btn" title="Click to choose format or drag to reposition anywhere">
        <span class="prrx-drag-handle" title="Drag to move panel">⠿</span>
        ${logoUrl ? `<img src="${logoUrl}" width="16" height="16" alt="PRRX" style="vertical-align: middle; border-radius: 2px;">` : `<span class="prrx-video-icon">⚡</span>`}
        <span>${typeLabel}</span>
        <span style="font-size: 9px; opacity: 0.8;">▼</span>
      </div>
      <div class="prrx-video-dropdown">
        ${mediaType === "audio" ? `
          <div class="prrx-dropdown-header">🎵 Audio Formats</div>
          <div class="prrx-dropdown-item" data-format="mp3" data-type="audio" data-quality="mp3">
            <span>Extract MP3 Audio</span>
            <span class="prrx-item-badge" style="color: #00FF88; background: rgba(0,255,136,0.15)">320K</span>
          </div>
          <div class="prrx-dropdown-item" data-format="wav" data-type="audio" data-quality="wav">
            <span>Lossless WAV (PCM)</span>
            <span class="prrx-item-badge">Master</span>
          </div>
          <div class="prrx-dropdown-item" data-format="m4a" data-type="audio" data-quality="m4a">
            <span>Apple M4A (AAC)</span>
            <span class="prrx-item-badge">AAC</span>
          </div>
          <div class="prrx-dropdown-item" data-format="flac" data-type="audio" data-quality="flac">
            <span>Audiophile FLAC</span>
            <span class="prrx-item-badge">Hi-Res</span>
          </div>
          <div class="prrx-dropdown-item" data-format="ogg" data-type="audio" data-quality="original">
            <span>Original Audio Stream</span>
            <span class="prrx-item-badge">Direct</span>
          </div>
        ` : mediaType === "document" ? `
          <div class="prrx-dropdown-header">📄 Document Options</div>
          <div class="prrx-dropdown-item" data-format="original" data-type="document" data-quality="original">
            <span>Download Original File</span>
            <span class="prrx-item-badge" style="color: #00FF88; background: rgba(0,255,136,0.15)">Fiber</span>
          </div>
        ` : `
          <div class="prrx-dropdown-header">🎬 Video Formats (MP4 Default)</div>
          <div class="prrx-dropdown-item" data-format="mp4" data-type="video" data-quality="1080p">
            <span>MP4 (Universal)</span>
            <span class="prrx-item-badge" style="color: #00FF88; background: rgba(0,255,136,0.15)">Default</span>
          </div>
          <div class="prrx-dropdown-item" data-format="mkv" data-type="video" data-quality="best">
            <span>MKV (Matroska HD)</span>
            <span class="prrx-item-badge">MKV</span>
          </div>
          <div class="prrx-dropdown-item" data-format="webm" data-type="video" data-quality="best">
            <span>WebM (HTML5 Royalty-free)</span>
            <span class="prrx-item-badge">WebM</span>
          </div>
          <div class="prrx-dropdown-item" data-format="mov" data-type="video" data-quality="best">
            <span>MOV (Apple QuickTime)</span>
            <span class="prrx-item-badge">MOV</span>
          </div>
          <div class="prrx-dropdown-item" data-format="avi" data-type="video" data-quality="best">
            <span>AVI (Interleaved)</span>
            <span class="prrx-item-badge">AVI</span>
          </div>

          <div class="prrx-dropdown-header">🎵 Convert Video to Audio</div>
          <div class="prrx-dropdown-item" data-format="mp3" data-type="audio" data-quality="mp3">
            <span>Extract MP3 Audio</span>
            <span class="prrx-item-badge" style="color: #00FF88; background: rgba(0,255,136,0.15)">320K</span>
          </div>
          <div class="prrx-dropdown-item" data-format="wav" data-type="audio" data-quality="wav">
            <span>Lossless WAV Audio</span>
            <span class="prrx-item-badge">PCM</span>
          </div>
          <div class="prrx-dropdown-item" data-format="m4a" data-type="audio" data-quality="m4a">
            <span>Apple M4A Audio</span>
            <span class="prrx-item-badge">AAC</span>
          </div>
          <div class="prrx-dropdown-item" data-format="flac" data-type="audio" data-quality="flac">
            <span>Audiophile FLAC Audio</span>
            <span class="prrx-item-badge">Lossless</span>
          </div>
          <div class="prrx-dropdown-item" data-format="ogg" data-type="audio" data-quality="ogg">
            <span>OGG Vorbis Audio</span>
            <span class="prrx-item-badge">OGG</span>
          </div>
        `}
      </div>
    `;

    const btn = panel.querySelector(".prrx-video-btn");
    const dropdown = panel.querySelector(".prrx-video-dropdown");

    // Draggable grabber logic with persistent viewport clamping
    let isDragging = false;
    let hasMoved = false;
    let dragStartX = 0;
    let dragStartY = 0;
    let initialX = 0;
    let initialY = 0;

    function onPointerDown(e) {
      if (e.button !== 0 && !e.touches) return;
      const clientX = e.clientX || (e.touches && e.touches[0].clientX);
      const clientY = e.clientY || (e.touches && e.touches[0].clientY);
      dragStartX = clientX;
      dragStartY = clientY;
      hasMoved = false;

      const rect = panel.getBoundingClientRect();
      initialX = rect.left;
      initialY = rect.top;

      window.addEventListener("pointermove", onPointerMove);
      window.addEventListener("pointerup", onPointerUp);
      window.addEventListener("touchmove", onPointerMove, { passive: false });
      window.addEventListener("touchend", onPointerUp);
    }

    function onPointerMove(e) {
      const clientX = e.clientX || (e.touches && e.touches[0].clientX);
      const clientY = e.clientY || (e.touches && e.touches[0].clientY);
      const dx = clientX - dragStartX;
      const dy = clientY - dragStartY;

      if (!hasMoved && Math.hypot(dx, dy) > 5) {
        hasMoved = true;
        isDragging = true;
        panel.style.position = "fixed";
        panel.style.right = "auto";
        panel.style.bottom = "auto";
        panel.classList.add("prrx-dragging");
        dropdown.classList.remove("show");
      }

      if (hasMoved) {
        if (e.preventDefault) e.preventDefault();
        const maxW = window.innerWidth - panel.offsetWidth - 8;
        const maxH = window.innerHeight - panel.offsetHeight - 8;
        const newX = Math.max(8, Math.min(maxW, initialX + dx));
        const newY = Math.max(8, Math.min(maxH, initialY + dy));
        panel.style.left = `${newX}px`;
        panel.style.top = `${newY}px`;
      }
    }

    function onPointerUp() {
      window.removeEventListener("pointermove", onPointerMove);
      window.removeEventListener("pointerup", onPointerUp);
      window.removeEventListener("touchmove", onPointerMove);
      window.removeEventListener("touchend", onPointerUp);

      panel.classList.remove("prrx-dragging");

      if (hasMoved) {
        const rect = panel.getBoundingClientRect();
        try {
          if (isExtensionValid() && chrome.storage && chrome.storage.local) {
            chrome.storage.local.set({ floatingPanelPos: { x: rect.left, y: rect.top } });
          }
        } catch (_) { }
        setTimeout(() => { isDragging = false; hasMoved = false; }, 60);
      }
    }

    btn.addEventListener("pointerdown", onPointerDown);
    btn.addEventListener("touchstart", onPointerDown, { passive: true });

    btn.addEventListener("click", (e) => {
      if (isDragging || hasMoved) {
        e.stopPropagation();
        return;
      }
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
            quality: item.dataset.quality || "best",
            targetFormat: item.dataset.format || "mp4",
            mediaType: item.dataset.type || "video",
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
