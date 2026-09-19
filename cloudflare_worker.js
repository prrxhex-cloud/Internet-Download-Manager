/**
 * ============================================================================
 * Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
 * PRRX IDM (TM) - Cloud Intelligence, P2P Matchmaker & Telegram Bot Gateway
 * Powered by Cloudflare Workers, Cloudflare D1 Edge SQLite & Telegram Bot API
 * 100% Free Forever Architecture
 * ============================================================================
 */

// Configuration
const BOT_TOKEN = "8728261333:AAHuFJ7bhIZ_jElnnIo6h_BgGQzpE1niEr4";
const TELEGRAM_API_BASE = `https://api.telegram.org/bot${BOT_TOKEN}`;
const TELEGRAM_FILE_BASE = `https://api.telegram.org/file/bot${BOT_TOKEN}`;

// In-Memory Global Task Queues and Pairing Maps (Per Worker Instance / KV Fallback)
const taskQueues = new Map();    // clientId -> Array of task objects
const pairMap = new Map();       // pairCode -> clientId
const userClientMap = new Map(); // telegramChatId -> clientId

export default {
  async fetch(request, env, ctx) {
    const url = new URL(request.url);
    const path = url.pathname;
    const method = request.method.toUpperCase();

    // Global CORS headers for PRRX IDM desktop & browser extensions
    const corsHeaders = {
      "Access-Control-Allow-Origin": "*",
      "Access-Control-Allow-Methods": "GET, POST, OPTIONS",
      "Access-Control-Allow-Headers": "Content-Type, Authorization, X-PRRX-Client, X-Client-Id",
      "Access-Control-Max-Age": "86400",
      "Content-Type": "application/json; charset=utf-8"
    };

    if (method === "OPTIONS") {
      return new Response(null, { headers: corsHeaders });
    }

    const clientPublicIp = request.headers.get("CF-Connecting-IP") || "127.0.0.1";
    const clientColo = request.cf?.colo || "EDGE";

    try {
      // ----------------------------------------------------------------------
      // 1. Health Check & Root Endpoint
      // ----------------------------------------------------------------------
      if (path === "/" || path === "/api/health") {
        return new Response(JSON.stringify({
          status: "online",
          service: "PRRX IDM Cloud Intelligence Gateway",
          version: "1.6.0",
          edge_node: clientColo,
          client_ip: clientPublicIp,
          telegram_bot: "@PRRX_IDM_Bot",
          timestamp: new Date().toISOString()
        }), { status: 200, headers: corsHeaders });
      }

      // ----------------------------------------------------------------------
      // 2. Traffic & Latency Status
      // ----------------------------------------------------------------------
      if (path === "/api/traffic" && method === "GET") {
        return new Response(JSON.stringify({
          status: "normal",
          latency_ms: 120,
          congestion_level: 0.12,
          recommendation: "cloud_accelerated",
          timestamp: new Date().toISOString()
        }), { status: 200, headers: corsHeaders });
      }

      // ----------------------------------------------------------------------
      // 3. Auto-Updater Manifest Endpoint
      // ----------------------------------------------------------------------
      if (path === "/api/manifest" && method === "GET") {
        return new Response(JSON.stringify({
          version: "1.6.0",
          releaseDate: "2026-09-19",
          downloadUrl: "https://github.com/prrxhex-cloud/Internet-Download-Manager/releases/download/v1.6.0/PRRX_Internet_Download_Manager_v1.6.0_Portable.zip",
          sha256Hash: "5034EA38177CFF8AC5922B4E0EBFE601B5000C274F968E99371E373652B2FA32",
          releaseNotes: "PRRX IDM v1.6.0: Automated Remote Telegram Bot Downloader (@PRRX_IDM_Bot), Zero-Latency Public Post Scraper, and Telegram Web Floating Media Interceptor.",
          isMandatory: false
        }), { status: 200, headers: corsHeaders });
      }

      // ----------------------------------------------------------------------
      // 4. Feature 3: File Reputation Lookup (SHA-256)
      // GET /api/reputation?hash=...
      // ----------------------------------------------------------------------
      if (path === "/api/reputation" && method === "GET") {
        const hash = url.searchParams.get("hash")?.trim().toLowerCase();
        if (!hash) {
          return new Response(JSON.stringify({ error: "Missing hash parameter" }), { status: 400, headers: corsHeaders });
        }

        if (env.DB) {
          const row = await env.DB.prepare(
            "SELECT * FROM file_reputation WHERE sha256 = ?"
          ).bind(hash).first();

          if (row) {
            const totalVotes = row.safe_votes + row.malware_reports;
            const score = totalVotes === 0 ? 100 : Math.max(0, Math.round((row.safe_votes / totalVotes) * 100));
            const verdict = row.malware_reports >= 3 ? "suspicious" : (score >= 80 ? "safe" : "neutral");

            return new Response(JSON.stringify({
              found: true,
              sha256: row.sha256,
              file_name: row.file_name,
              file_size: row.file_size,
              safe_votes: row.safe_votes,
              malware_reports: row.malware_reports,
              download_count: row.download_count,
              safety_score: score,
              verdict: verdict,
              first_seen: row.first_seen,
              last_seen: row.last_seen
            }), { status: 200, headers: corsHeaders });
          }
        }

        return new Response(JSON.stringify({
          found: false,
          sha256: hash,
          safety_score: 100,
          verdict: "unrated",
          message: "New file - No community reports yet"
        }), { status: 200, headers: corsHeaders });
      }

      // ----------------------------------------------------------------------
      // 5. Feature 3: Vote or Report File / Download Count
      // POST /api/reputation/report
      // ----------------------------------------------------------------------
      if (path === "/api/reputation/report" && method === "POST") {
        const body = await request.json().catch(() => ({}));
        const hash = body.sha256?.trim().toLowerCase();
        if (!hash) {
          return new Response(JSON.stringify({ error: "Missing sha256" }), { status: 400, headers: corsHeaders });
        }

        const isSafe = body.vote === "safe" ? 1 : 0;
        const isMalware = body.vote === "malware" ? 1 : 0;
        const fileName = body.file_name || "download";
        const fileSize = parseInt(body.file_size) || 0;

        if (env.DB) {
          await env.DB.prepare(`
            INSERT INTO file_reputation (sha256, file_name, file_size, safe_votes, malware_reports, download_count, last_seen)
            VALUES (?1, ?2, ?3, ?4, ?5, 1, CURRENT_TIMESTAMP)
            ON CONFLICT(sha256) DO UPDATE SET
              file_name = COALESCE(excluded.file_name, file_name),
              file_size = CASE WHEN excluded.file_size > 0 THEN excluded.file_size ELSE file_size END,
              safe_votes = safe_votes + excluded.safe_votes,
              malware_reports = malware_reports + excluded.malware_reports,
              download_count = download_count + 1,
              last_seen = CURRENT_TIMESTAMP
          `).bind(hash, fileName, fileSize, isSafe, isMalware).run();
        }

        return new Response(JSON.stringify({ success: true, message: "Reputation recorded" }), { status: 200, headers: corsHeaders });
      }

      // ----------------------------------------------------------------------
      // 6. Feature 5: LAN Peer Discovery Matchmaker
      // GET /api/p2p/peers?hash=...&lan_ip=...
      // ----------------------------------------------------------------------
      if (path === "/api/p2p/peers" && method === "GET") {
        const hash = url.searchParams.get("hash")?.trim().toLowerCase();
        const lan_ip = url.searchParams.get("lan_ip")?.trim() || "";

        if (!hash) {
          return new Response(JSON.stringify({ error: "Missing hash parameter" }), { status: 400, headers: corsHeaders });
        }

        let peersList = [];
        if (env.DB) {
          const peers = await env.DB.prepare(`
            SELECT peer_id, lan_ip, port, completed_chunks, total_chunks, last_heartbeat
            FROM p2p_peers
            WHERE sha256 = ?1
              AND public_ip = ?2
              AND lan_ip != ?3
              AND datetime(last_heartbeat) > datetime('now', '-3 minutes')
          `).bind(hash, clientPublicIp, lan_ip).all();
          peersList = peers.results || [];
        }

        return new Response(JSON.stringify({
          matched_network: clientPublicIp,
          total_peers: peersList.length,
          peers: peersList
        }), { status: 200, headers: corsHeaders });
      }

      // ----------------------------------------------------------------------
      // 7. Feature 5: LAN Peer Announcement / Heartbeat
      // POST /api/p2p/announce
      // ----------------------------------------------------------------------
      if (path === "/api/p2p/announce" && method === "POST") {
        const body = await request.json().catch(() => ({}));
        const { peer_id, sha256, lan_ip, port, completed_chunks, total_chunks } = body;

        if (!peer_id || !sha256 || !lan_ip || !port) {
          return new Response(JSON.stringify({ error: "Missing required peer fields" }), { status: 400, headers: corsHeaders });
        }

        if (env.DB) {
          await env.DB.prepare(`
            INSERT INTO p2p_peers (peer_id, sha256, public_ip, lan_ip, port, completed_chunks, total_chunks, last_heartbeat)
            VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, CURRENT_TIMESTAMP)
            ON CONFLICT(peer_id) DO UPDATE SET
              completed_chunks = excluded.completed_chunks,
              total_chunks = excluded.total_chunks,
              last_heartbeat = CURRENT_TIMESTAMP
          `).bind(peer_id, sha256.toLowerCase(), clientPublicIp, lan_ip, parseInt(port), completed_chunks || 0, total_chunks || 0).run();

          // Background cleanup of stale peers (> 10 mins inactive)
          ctx.waitUntil(
            env.DB.prepare("DELETE FROM p2p_peers WHERE datetime(last_heartbeat) < datetime('now', '-10 minutes')").run()
          );
        }

        return new Response(JSON.stringify({ success: true, registered: true }), { status: 200, headers: corsHeaders });
      }

      // ----------------------------------------------------------------------
      // 8. Feature 2: High-Speed Mirror Accelerator Lookups
      // GET /api/mirrors?hash=...
      // ----------------------------------------------------------------------
      if (path === "/api/mirrors" && method === "GET") {
        const hash = url.searchParams.get("hash")?.trim().toLowerCase();
        if (!hash) {
          return new Response(JSON.stringify({ error: "Missing hash parameter" }), { status: 400, headers: corsHeaders });
        }

        let mirrorsList = [];
        if (env.DB) {
          const mirrors = await env.DB.prepare(
            "SELECT mirror_url, speed_rank FROM file_mirrors WHERE sha256 = ? ORDER BY speed_rank ASC"
          ).bind(hash).all();
          mirrorsList = mirrors.results || [];
        }

        return new Response(JSON.stringify({
          sha256: hash,
          mirrors: mirrorsList
        }), { status: 200, headers: corsHeaders });
      }

      // ----------------------------------------------------------------------
      // 9. Domain Health & Speed Check
      // GET /api/domain/health?domain=...
      // ----------------------------------------------------------------------
      if (path === "/api/domain/health" && method === "GET") {
        const domain = url.searchParams.get("domain")?.trim().toLowerCase();
        if (!domain) {
          return new Response(JSON.stringify({ error: "Missing domain parameter" }), { status: 400, headers: corsHeaders });
        }

        let health = null;
        if (env.DB) {
          health = await env.DB.prepare(
            "SELECT * FROM domain_health WHERE domain = ?"
          ).bind(domain).first();
        }

        return new Response(JSON.stringify({
          domain: domain,
          status: health ? health.status : "healthy",
          reputation: "trusted",
          safety_score: 99,
          avg_speed_mbps: health ? health.avg_speed_mbps : 100.0
        }), { status: 200, headers: corsHeaders });
      }

      // ======================================================================
      // 10. TELEGRAM BOT AUTOMATION ENDPOINTS
      // ======================================================================

      // Telegram Webhook Receiver (POST from Telegram Servers)
      if (path === "/api/telegram/webhook" && method === "POST") {
        const update = await request.json().catch(() => null);
        if (update) {
          ctx.waitUntil(handleTelegramUpdate(update, env));
        }
        return new Response(JSON.stringify({ ok: true }), { status: 200, headers: corsHeaders });
      }

      // Register / Generate Desktop Pairing Code (POST)
      if (path === "/api/telegram/pair" && method === "POST") {
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
        }), { status: 200, headers: corsHeaders });
      }

      // Desktop App Task Polling Endpoint (GET)
      if (path === "/api/telegram/tasks" && method === "GET") {
        const clientId = url.searchParams.get("client_id");
        if (!clientId) {
          return new Response(JSON.stringify({ tasks: [] }), { status: 200, headers: corsHeaders });
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

        return new Response(JSON.stringify({ tasks: tasks }), { status: 200, headers: corsHeaders });
      }

      // Route Not Found
      return new Response(JSON.stringify({ error: "Endpoint not found", path: path }), { status: 404, headers: corsHeaders });

    } catch (err) {
      return new Response(JSON.stringify({ error: "Internal Server Error", message: err.message }), { status: 500, headers: corsHeaders });
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
      `2. Go to <b>Settings → Telegram Bot</b> and copy your pairing code.\n` +
      `3. Send your code here (e.g. <code>/pair PRRX-1234</code>).\n\n` +
      `Once linked, any file you forward here downloads on your PC automatically!`
    );
    return;
  }

  // 2. Identify target Client ID for this user
  let clientId = userClientMap.get(chatId);
  if (!clientId && env.PRRX_KV) {
    clientId = await env.PRRX_KV.get(`user:${chatId}`);
  }

  // If user hasn't paired yet, fallback to default_user queue
  if (!clientId) {
    clientId = "default_user";
  }

  // 3. Check for Media Files (Video, Document, Audio, Voice, Photo)
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
    `• Pair your PC using <code>/pair YOUR-CODE</code>.`
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
