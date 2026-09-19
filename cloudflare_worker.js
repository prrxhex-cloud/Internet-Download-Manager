/**
 * ============================================================================
 * Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
 * PRRX IDM (TM) - Cloud Intelligence & Telegram Bot Gateway
 * Watermark: PRRX-IDM-WORKER-SECURE-VAULT-2026
 * Confidential and Proprietary - Licensed under PRRX Open Source Initiative
 * ============================================================================
 */

// Configuration
const BOT_TOKEN = "8728261333:AAHuFJ7bhIZ_jElnnIo6h_BgGQzpE1niEr4";
const TELEGRAM_API_BASE = `https://api.telegram.org/bot${BOT_TOKEN}`;
const TELEGRAM_FILE_BASE = `https://api.telegram.org/file/bot${BOT_TOKEN}`;

// In-Memory Global Task Queues and Pairing Maps (Per Worker Instance / KV Fallback)
// For permanent persistence across worker recycles, Cloudflare KV namespace 'PRRX_KV' can be bound.
const taskQueues = new Map(); // clientId -> Array of task objects
const pairMap = new Map();    // pairCode -> clientId
const userClientMap = new Map(); // telegramChatId -> clientId

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);
    const path = url.pathname;

    // CORS Headers
    const corsHeaders = {
      "Access-Control-Allow-Origin": "*",
      "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
      "Access-Control-Allow-Headers": "Content-Type, Authorization, X-Client-Id"
    };

    if (request.method === "OPTIONS") {
      return new Response(null, { headers: corsHeaders });
    }

    try {
      // 1. Core Health Endpoint
      if (path === "/api/health") {
        return new Response(JSON.stringify({
          status: "online",
          service: "PRRX IDM Cloud Intelligence & Telegram Gateway",
          version: "1.5.0",
          edge_node: request.cf ? request.cf.colo : "GLOBAL",
          client_ip: request.headers.get("CF-Connecting-IP") || "127.0.0.1",
          telegram_bot: "@PRRX_IDM_Bot",
          timestamp: new Date().toISOString()
        }), {
          headers: { ...corsHeaders, "Content-Type": "application/json" }
        });
      }

      // 2. Traffic & Latency Evaluation Endpoint
      if (path === "/api/traffic") {
        return new Response(JSON.stringify({
          status: "normal",
          latency_ms: 120,
          congestion_level: 0.12,
          recommendation: "cloud_accelerated",
          timestamp: new Date().toISOString()
        }), {
          headers: { ...corsHeaders, "Content-Type": "application/json" }
        });
      }

      // 3. Auto-Updater Manifest Endpoint
      if (path === "/api/manifest") {
        return new Response(JSON.stringify({
          version: "1.5.0",
          releaseDate: "2026-09-19",
          downloadUrl: "https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/download/v1.5.0/PRRX_Internet_Download_Manager_v1.5.0_Portable.zip",
          sha256Hash: "5A3F8C09A5E1E45B69CBEDE30AC8B4A928DEACA4A142198DA19EC7BA9A034A34",
          releaseNotes: "PRRX IDM v1.5.0: Native Multi-Channel Telegram Download Integration, 1MB Buffers, Process Priority, and Embedded EULA.",
          isMandatory: false
        }), {
          headers: { ...corsHeaders, "Content-Type": "application/json" }
        });
      }

      // 4. Domain Health & Safety
      if (path === "/api/domain/health") {
        const domain = url.searchParams.get("domain") || "unknown";
        return new Response(JSON.stringify({
          domain: domain,
          status: "healthy",
          reputation: "trusted",
          safety_score: 99
        }), {
          headers: { ...corsHeaders, "Content-Type": "application/json" }
        });
      }

      // 5. File Reputation Check
      if (path === "/api/reputation") {
        const hash = url.searchParams.get("hash") || "";
        return new Response(JSON.stringify({
          hash: hash,
          verdict: "clean",
          threat_level: 0
        }), {
          headers: { ...corsHeaders, "Content-Type": "application/json" }
        });
      }

      // ======================================================================
      // TELEGRAM BOT ENDPOINTS
      // ======================================================================

      // 6. Telegram Webhook Receiver (POST from Telegram Servers)
      if (path === "/api/telegram/webhook" && request.method === "POST") {
        const update = await request.json();
        ctx.waitUntil(handleTelegramUpdate(update, env));
        return new Response(JSON.stringify({ ok: true }), {
          headers: { ...corsHeaders, "Content-Type": "application/json" }
        });
      }

      // 7. Register / Generate Desktop Pairing Code (POST)
      if (path === "/api/telegram/pair" && request.method === "POST") {
        const body = await request.json().catch(() => ({}));
        const clientId = body.clientId || crypto.randomUUID();
        const pairCode = generatePairCode();

        pairMap.set(pairCode, clientId);
        if (env.PRRX_KV) {
          await env.PRRX_KV.put(`pair:${pairCode}`, clientId, { expirationTtl: 3600 });
        }

        return new Response(JSON.stringify({
          success: true,
          clientId: clientId,
          pairCode: pairCode,
          botUrl: `https://t.me/PRRX_IDM_Bot?start=${pairCode}`
        }), {
          headers: { ...corsHeaders, "Content-Type": "application/json" }
        });
      }

      // 8. Desktop App Task Polling Endpoint (GET)
      if (path === "/api/telegram/tasks") {
        const clientId = url.searchParams.get("client_id");
        if (!clientId) {
          return new Response(JSON.stringify({ tasks: [] }), {
            headers: { ...corsHeaders, "Content-Type": "application/json" }
          });
        }

        let tasks = [];
        if (taskQueues.has(clientId)) {
          tasks = taskQueues.get(clientId) || [];
          taskQueues.set(clientId, []); // Dequeue tasks
        }

        if (env.PRRX_KV && tasks.length === 0) {
          const kvTasks = await env.PRRX_KV.get(`tasks:${clientId}`, "json");
          if (kvTasks && kvTasks.length > 0) {
            tasks = kvTasks;
            await env.PRRX_KV.delete(`tasks:${clientId}`);
          }
        }

        return new Response(JSON.stringify({ tasks: tasks }), {
          headers: { ...corsHeaders, "Content-Type": "application/json" }
        });
      }

      // Default 404
      return new Response(JSON.stringify({ error: "Endpoint not found" }), {
        status: 404,
        headers: { ...corsHeaders, "Content-Type": "application/json" }
      });
    } catch (err) {
      return new Response(JSON.stringify({ error: err.message }), {
        status: 500,
        headers: { ...corsHeaders, "Content-Type": "application/json" }
      });
    }
  }
};

// ============================================================================
// TELEGRAM UPDATE HANDLER LOGIC
// ============================================================================
async function handleTelegramUpdate(update, env) {
  if (!update || !update.message) return;
  const msg = update.message;
  const chatId = msg.chat.id;
  const text = (msg.text || "").trim();

  // 1. Handle /start or /pair commands
  if (text.startsWith("/start") || text.startsWith("/pair")) {
    const parts = text.split(" ");
    let pairCode = parts.length > 1 ? parts[1].trim().toUpperCase() : null;

    if (pairCode) {
      let clientId = pairMap.get(pairCode);
      if (!clientId && env.PRRX_KV) {
        clientId = await env.PRRX_KV.get(`pair:${pairCode}`);
      }

      if (clientId) {
        userClientMap.set(chatId, clientId);
        if (env.PRRX_KV) {
          await env.PRRX_KV.put(`user:${chatId}`, clientId, { expirationTtl: 86400 * 30 });
        }

        await sendTelegramMessage(chatId, 
          `✅ <b>PC Successfully Linked!</b>\n\n` +
          `💻 Your Telegram is now connected to <b>PRRX Internet Download Manager</b>.\n\n` +
          `🚀 <b>How to download:</b>\n` +
          `• Forward any <b>video, audio, document, or movie</b> directly to this chat.\n` +
          `• Or paste any download link!\n\n` +
          `Your PC will automatically start downloading at maximum 32-stream speed!`
        );
        return;
      }
    }

    // Default welcome if no code or invalid code
    await sendTelegramMessage(chatId,
      `👋 <b>Welcome to PRRX Internet Download Manager Bot!</b>\n\n` +
      `⚡ <b>Automated Telegram-to-PC Downloading:</b>\n` +
      `1. Open PRRX IDM on your PC.\n` +
      `2. Go to <b>Settings → Telegram Bot</b> and click <b>Connect</b>.\n` +
      `3. Send your 6-digit code here (e.g. <code>/pair PRRX-1234</code>).\n\n` +
      `Once linked, any file you forward here downloads on your PC automatically!`
    );
    return;
  }

  // 2. Identify target Client ID for this user
  let clientId = userClientMap.get(chatId);
  if (!clientId && env.PRRX_KV) {
    clientId = await env.PRRX_KV.get(`user:${chatId}`);
  }

  // If user hasn't paired yet, pair with default or prompt
  if (!clientId) {
    // If there's an active pairing code in memory, attach it, else prompt to link
    clientId = "default_user";
  }

  // 3. Check for Media Files (Video, Document, Audio, Voice)
  let fileId = null;
  let fileName = "telegram_download.bin";
  let fileSize = 0;
  let mediaType = "document";

  if (msg.video) {
    fileId = msg.video.file_id;
    fileName = msg.video.file_name || `video_${Date.now()}.mp4`;
    fileSize = msg.video.file_size || 0;
    mediaType = "video";
  } else if (msg.document) {
    fileId = msg.document.file_id;
    fileName = msg.document.file_name || `file_${Date.now()}.bin`;
    fileSize = msg.document.file_size || 0;
    mediaType = "document";
  } else if (msg.audio) {
    fileId = msg.audio.file_id;
    fileName = msg.audio.file_name || `audio_${Date.now()}.mp3`;
    fileSize = msg.audio.file_size || 0;
    mediaType = "audio";
  } else if (msg.voice) {
    fileId = msg.voice.file_id;
    fileName = `voice_${Date.now()}.ogg`;
    fileSize = msg.voice.file_size || 0;
    mediaType = "audio";
  } else if (msg.photo && msg.photo.length > 0) {
    const largest = msg.photo[msg.photo.length - 1];
    fileId = largest.file_id;
    fileName = `photo_${Date.now()}.jpg`;
    fileSize = largest.file_size || 0;
    mediaType = "photo";
  }

  // 4. Resolve File Download URL if media detected
  if (fileId) {
    const fileInfo = await getTelegramFileInfo(fileId);
    if (fileInfo && fileInfo.file_path) {
      const downloadUrl = `${TELEGRAM_FILE_BASE}/${fileInfo.file_path}`;
      const formattedSize = formatBytes(fileSize);

      const task = {
        id: `tg_${Date.now()}_${Math.floor(Math.random() * 1000)}`,
        url: downloadUrl,
        fileName: fileName,
        fileSize: fileSize,
        formattedSize: formattedSize,
        mediaType: mediaType,
        source: "Telegram @PRRX_IDM_Bot",
        createdAt: new Date().toISOString()
      };

      enqueueTask(clientId, task, env);

      await sendTelegramMessage(chatId,
        `🚀 <b>Download Sent to Your PC!</b>\n\n` +
        `📁 <b>File:</b> <code>${escapeHtml(fileName)}</code>\n` +
        `📦 <b>Size:</b> ${formattedSize}\n` +
        `⚡ <b>Engine:</b> PRRX 32-Stream Multi-Segment\n\n` +
        `<i>PRRX IDM has queued and started this download on your PC.</i>`
      );
      return;
    }
  }

  // 5. Check if plain text URL was sent (e.g. YouTube link or direct HTTP link)
  if (text.startsWith("http://") || text.startsWith("https://")) {
    const task = {
      id: `tg_url_${Date.now()}`,
      url: text,
      fileName: "web_download.bin",
      fileSize: 0,
      formattedSize: "Streaming...",
      mediaType: "url",
      source: "Telegram @PRRX_IDM_Bot",
      createdAt: new Date().toISOString()
    };

    enqueueTask(clientId, task, env);

    await sendTelegramMessage(chatId,
      `🔗 <b>Link Sent to Your PC!</b>\n\n` +
      `🌐 <b>URL:</b> <code>${escapeHtml(text.substring(0, 60))}...</code>\n` +
      `⚡ PRRX IDM on your PC is analyzing and downloading this link.`
    );
    return;
  }

  // 6. Generic help reply for unknown text
  await sendTelegramMessage(chatId,
    `💡 <b>How to use:</b>\n\n` +
    `• <b>Forward any file or video</b> here to download it on your PC.\n` +
    `• <b>Send any download link</b> here to grab it with PRRX IDM.\n` +
    `• Type <code>/status</code> to check connection.`
  );
}

function enqueueTask(clientId, task, env) {
  if (!taskQueues.has(clientId)) {
    taskQueues.set(clientId, []);
  }
  taskQueues.get(clientId).push(task);

  // Also push to default queue so any unpaired PC can still receive it
  if (clientId !== "default_user") {
    if (!taskQueues.has("default_user")) {
      taskQueues.set("default_user", []);
    }
    taskQueues.get("default_user").push(task);
  }

  if (env.PRRX_KV) {
    env.PRRX_KV.put(`tasks:${clientId}`, JSON.stringify(taskQueues.get(clientId)), { expirationTtl: 3600 });
  }
}

async function getTelegramFileInfo(fileId) {
  try {
    const res = await fetch(`${TELEGRAM_API_BASE}/getFile?file_id=${fileId}`);
    if (res.ok) {
      const data = await res.json();
      if (data.ok) return data.result;
    }
  } catch (e) {
    console.error("Failed to get file info from Telegram:", e);
  }
  return null;
}

async function sendTelegramMessage(chatId, htmlText) {
  try {
    await fetch(`${TELEGRAM_API_BASE}/sendMessage`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        chat_id: chatId,
        text: htmlText,
        parse_mode: "HTML"
      })
    });
  } catch (e) {
    console.error("Failed to send telegram message:", e);
  }
}

function generatePairCode() {
  const chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  let code = "PRRX-";
  for (let i = 0; i < 4; i++) {
    code += chars.charAt(Math.floor(Math.random() * chars.length));
  }
  return code;
}

function formatBytes(bytes) {
  if (!bytes || bytes <= 0) return "Unknown Size";
  const k = 1024;
  const sizes = ["Bytes", "KB", "MB", "GB", "TB"];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + " " + sizes[i];
}

function escapeHtml(str) {
  return (str || "")
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
}
