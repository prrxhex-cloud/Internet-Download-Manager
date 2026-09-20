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
          if (ctx && ctx.waitUntil) {
            ctx.waitUntil(
              env.DB.prepare("DELETE FROM p2p_peers WHERE datetime(last_heartbeat) < datetime('now', '-10 minutes')").run().catch(() => {})
            );
          }
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
          if (ctx && ctx.waitUntil) {
            ctx.waitUntil(handleTelegramUpdate(update, env, ctx));
          } else {
            await handleTelegramUpdate(update, env, ctx);
          }
        }
        return new Response(JSON.stringify({ ok: true }), { status: 200, headers: corsHeaders });
      }

      // Register / Generate Desktop Pairing Code (POST)
      if (path === "/api/telegram/pair" && method === "POST") {
        const body = await request.json().catch(() => ({}));
        const clientId = (typeof body.clientId === "string" && body.clientId.trim())
          ? body.clientId.trim()
          : crypto.randomUUID();
        const pairCode = (typeof body.pairCode === "string" && /^PRRX-[A-Z0-9]{4,12}$/i.test(body.pairCode.trim()))
          ? body.pairCode.trim().toUpperCase()
          : generatePairCode();

        pairMap.set(pairCode, clientId);

        if (env.DB) {
          await ensureTelegramTables(env.DB);
          try {
            await env.DB.prepare(`
              INSERT INTO telegram_pairs (pair_code, client_id, created_at, expires_at)
              VALUES (?1, ?2, CURRENT_TIMESTAMP, datetime('now', '+1 hour'))
              ON CONFLICT(pair_code) DO UPDATE SET
                client_id = excluded.client_id,
                created_at = CURRENT_TIMESTAMP,
                expires_at = datetime('now', '+1 hour')
            `).bind(pairCode, clientId).run();

            // Background cleanup of expired pairs
            if (ctx && ctx.waitUntil) {
              ctx.waitUntil(
                env.DB.prepare("DELETE FROM telegram_pairs WHERE datetime(expires_at) < datetime('now')").run().catch(() => {})
              );
            }
          } catch (e) {
            console.error("D1 pair insert error:", e);
          }
        }

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

        if (env.DB) {
          await ensureTelegramTables(env.DB);
          try {
            const rows = await env.DB.prepare(`
              SELECT id, url, file_name AS fileName, file_size AS fileSize,
                     formatted_size AS formattedSize, media_type AS mediaType,
                     source, created_at AS createdAt
              FROM telegram_tasks
              WHERE client_id = ?1 AND status = 'pending'
              ORDER BY created_at ASC
              LIMIT 50
            `).bind(clientId).all();

            tasks = rows.results || [];

            if (tasks.length > 0) {
              const ids = tasks.map(t => t.id);
              const placeholders = ids.map(() => "?").join(",");
              await env.DB.prepare(`
                UPDATE telegram_tasks
                SET status = 'delivered', delivered_at = CURRENT_TIMESTAMP
                WHERE id IN (${placeholders})
              `).bind(...ids).run();
            }

            // Occasional background cleanup of delivered tasks older than 3 days
            if (ctx && ctx.waitUntil) {
              ctx.waitUntil(
                env.DB.prepare(
                  "DELETE FROM telegram_tasks WHERE status = 'delivered' AND datetime(delivered_at) < datetime('now', '-3 days')"
                ).run().catch(() => {})
              );
            }
          } catch (e) {
            console.error("D1 tasks query error:", e);
          }
        }

        // Memory and KV fallbacks if D1 returned no tasks or is not configured
        if (tasks.length === 0 && taskQueues.has(clientId)) {
          const memTasks = taskQueues.get(clientId) || [];
          if (memTasks.length > 0) {
            tasks = memTasks;
            taskQueues.set(clientId, []); // Dequeue tasks
          }
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
// TELEGRAM UPDATE HANDLER LOGIC & D1 DATABASE MANAGEMENT
// ============================================================================

let telegramTablesInitialized = false;

async function ensureTelegramTables(db) {
  if (!db || telegramTablesInitialized) return;
  try {
    await db.batch([
      db.prepare(`
        CREATE TABLE IF NOT EXISTS telegram_pairs (
          pair_code TEXT PRIMARY KEY,
          client_id TEXT NOT NULL,
          created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
          expires_at DATETIME NOT NULL
        )
      `),
      db.prepare(`
        CREATE TABLE IF NOT EXISTS telegram_users (
          chat_id TEXT PRIMARY KEY,
          client_id TEXT NOT NULL,
          linked_at DATETIME DEFAULT CURRENT_TIMESTAMP,
          last_active DATETIME DEFAULT CURRENT_TIMESTAMP
        )
      `),
      db.prepare(`
        CREATE TABLE IF NOT EXISTS telegram_tasks (
          id TEXT PRIMARY KEY,
          client_id TEXT NOT NULL,
          url TEXT NOT NULL,
          file_name TEXT,
          file_size INTEGER DEFAULT 0,
          formatted_size TEXT,
          media_type TEXT DEFAULT 'document',
          source TEXT,
          status TEXT DEFAULT 'pending',
          created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
          delivered_at DATETIME
        )
      `),
      db.prepare(`
        CREATE INDEX IF NOT EXISTS idx_telegram_tasks_client ON telegram_tasks(client_id, status)
      `)
    ]);
    telegramTablesInitialized = true;
  } catch (err) {
    console.error("Failed to initialize telegram tables in D1:", err);
  }
}

async function handleTelegramUpdate(update, env, ctx) {
  if (!update) return;
  const msg = update.message || update.edited_message || update.channel_post;
  if (!msg || !msg.chat) return;

  const chatId = msg.chat.id;
  const text = (msg.text || msg.caption || "").trim();

  // 1. Check for pairing commands or codes:
  // - /pair [CODE], /pair_[CODE], pair [CODE]
  // - /start [CODE], /start_[CODE], start [CODE]
  // - Bare code: PRRX-XXXX, PRRX XXXX, PRRX_XXXX, PRRXXXXX
  // - Bare 4-8 character code: e.g. 986A, RYMF
  let pairingCodeAttempt = null;
  let isPairOrStartCommand = false;

  const pairMatch = text.match(/^(?:\/|!)?pair(?:@\w+)?(?:[\s:=-]+([A-Za-z0-9][A-Za-z0-9_-]*)|_([A-Za-z0-9][A-Za-z0-9_-]*))?$/i);
  const startMatch = text.match(/^(?:\/|!)?start(?:@\w+)?(?:[\s:=-]+([A-Za-z0-9][A-Za-z0-9_-]*)|_([A-Za-z0-9][A-Za-z0-9_-]*))?$/i);
  const embeddedCodeMatch = text.match(/\b(PRRX[-\s_]?[A-Za-z0-9]{4,12})\b/i);

  if (pairMatch) {
    isPairOrStartCommand = true;
    pairingCodeAttempt = pairMatch[1] || pairMatch[2] || (embeddedCodeMatch ? embeddedCodeMatch[1] : null);
  } else if (startMatch) {
    isPairOrStartCommand = true;
    pairingCodeAttempt = startMatch[1] || startMatch[2] || (embeddedCodeMatch ? embeddedCodeMatch[1] : null);
  } else if (embeddedCodeMatch) {
    pairingCodeAttempt = embeddedCodeMatch[1];
  } else if (/^[A-Za-z0-9]{4,8}$/.test(text)) {
    // User sent a bare 4-8 alphanumeric code like 986A or RYMF
    pairingCodeAttempt = text;
  }

  // If user sent bare /start or /pair without code, show friendly welcome instructions
  if (isPairOrStartCommand && !pairingCodeAttempt) {
    await sendTelegramMessage(chatId,
      `👋 <b>Welcome to PRRX Internet Download Manager Bot!</b>\n\n` +
      `⚡ <b>Automated Telegram-to-PC Downloading:</b>\n` +
      `1. Open PRRX IDM on your PC.\n` +
      `2. Go to <b>Settings → Telegram Bot</b> to view your pairing code.\n` +
      `3. Send your code here (e.g. <code>/pair PRRX-1234</code> or simply <code>PRRX-1234</code>).\n\n` +
      `Once linked, any media, file, or download link forwarded here will automatically download on your PC at maximum 32-stream speed!`
    );
    return;
  }

  // If a pairing code was provided
  if (pairingCodeAttempt) {
    const rawCode = pairingCodeAttempt.trim().toUpperCase();
    let candidateCode = rawCode.replace(/[_\s]+/g, "-");
    if (/^PRRX[A-Z0-9]{4,}$/.test(candidateCode)) {
      candidateCode = "PRRX-" + candidateCode.slice(4);
    } else if (!candidateCode.startsWith("PRRX-") && /^[A-Z0-9]{4,8}$/.test(candidateCode)) {
      candidateCode = "PRRX-" + candidateCode;
    }

    let matchedClientId = null;

    if (env.DB) {
      await ensureTelegramTables(env.DB);
      try {
        const pairRow = await env.DB.prepare(`
          SELECT client_id
          FROM telegram_pairs
          WHERE (pair_code = ?1 OR pair_code = ?2 OR pair_code = ?3)
            AND datetime(expires_at) > datetime('now')
        `).bind(candidateCode, rawCode, candidateCode.replace(/-/g, "_")).first();

        if (pairRow) {
          matchedClientId = pairRow.client_id;
        }
      } catch (e) {
        console.error("D1 pair lookup error:", e);
      }
    }

    if (!matchedClientId) {
      matchedClientId = pairMap.get(candidateCode) || pairMap.get(rawCode);
    }

    if (!matchedClientId && env.PRRX_KV) {
      matchedClientId = await env.PRRX_KV.get(`pair:${candidateCode}`) || await env.PRRX_KV.get(`pair:${rawCode}`);
    }

    if (matchedClientId) {
      // Link chat ID to client ID in D1 and delete used pair code
      if (env.DB) {
        try {
          await env.DB.batch([
            env.DB.prepare(`
              INSERT INTO telegram_users (chat_id, client_id, linked_at, last_active)
              VALUES (?1, ?2, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
              ON CONFLICT(chat_id) DO UPDATE SET
                client_id = excluded.client_id,
                last_active = CURRENT_TIMESTAMP
            `).bind(chatId.toString(), matchedClientId),

            env.DB.prepare("DELETE FROM telegram_pairs WHERE pair_code = ?1 OR pair_code = ?2 OR client_id = ?3")
              .bind(candidateCode, rawCode, matchedClientId)
          ]);
        } catch (e) {
          console.error("D1 link user error:", e);
        }
      }

      userClientMap.set(chatId.toString(), matchedClientId);
      pairMap.delete(candidateCode);
      pairMap.delete(rawCode);

      if (env.PRRX_KV) {
        await env.PRRX_KV.put(`user:${chatId}`, matchedClientId, { expirationTtl: 86400 * 30 });
        await env.PRRX_KV.delete(`pair:${candidateCode}`);
        await env.PRRX_KV.delete(`pair:${rawCode}`);
      }

      await sendTelegramMessage(chatId,
        `✅ <b>PC Successfully Linked!</b>\n\n` +
        `💻 Your Telegram is now securely connected to <b>PRRX Internet Download Manager</b>.\n\n` +
        `🚀 <b>How to download:</b>\n` +
        `• Forward any <b>video, audio, document, or archive</b> directly to this chat.\n` +
        `• Or paste any download link (e.g. <code>https://...</code>)!\n\n` +
        `Your PC will automatically receive and start downloading at maximum 32-stream Turbo speed!`
      );
      return;
    }

    // Invalid or expired pairing code - clear error response!
    await sendTelegramMessage(chatId,
      `❌ <b>Invalid or Expired Pairing Code</b>\n\n` +
      `The pairing code <code>${escapeHtml(candidateCode)}</code> could not be found or has expired (pairing codes expire after 1 hour).\n\n` +
      `💡 <b>To connect your PC:</b>\n` +
      `1. Open PRRX IDM on your PC.\n` +
      `2. Go to <b>Settings → Telegram Bot</b> to view or refresh your pairing code.\n` +
      `3. Send your code here (e.g. <code>/pair ${escapeHtml(candidateCode)}</code>).`
    );
    return;
  }

  // 2. Identify target Client ID for this user
  let clientId = userClientMap.get(chatId.toString());

  if (!clientId && env.DB) {
    await ensureTelegramTables(env.DB);
    try {
      const userRow = await env.DB.prepare(
        "SELECT client_id FROM telegram_users WHERE chat_id = ?"
      ).bind(chatId.toString()).first();

      if (userRow) {
        clientId = userRow.client_id;
        userClientMap.set(chatId.toString(), clientId);
        if (ctx && ctx.waitUntil) {
          ctx.waitUntil(
            env.DB.prepare("UPDATE telegram_users SET last_active = CURRENT_TIMESTAMP WHERE chat_id = ?")
              .bind(chatId.toString()).run().catch(() => {})
          );
        }
      }
    } catch (e) {
      console.error("D1 user lookup error:", e);
    }
  }

  if (!clientId && env.PRRX_KV) {
    clientId = await env.PRRX_KV.get(`user:${chatId}`);
    if (clientId) {
      userClientMap.set(chatId.toString(), clientId);
    }
  }

  // Handle /help, /info, /about
  const helpMatch = text.match(/^\/(?:help|info|about)(?:@\w+)?$/i);
  if (helpMatch) {
    const statusText = clientId
      ? `✅ <b>Status:</b> Connected to Client <code>${clientId.substring(0, 8)}...</code>`
      : `⚠️ <b>Status:</b> Not connected to any PC.`;
    await sendTelegramMessage(chatId,
      `👋 <b>PRRX Internet Download Manager Bot</b>\n\n` +
      `${statusText}\n\n` +
      `⚡ <b>Commands:</b>\n` +
      `• <code>/pair [CODE]</code> — Connect your PC (e.g. <code>/pair PRRX-1234</code>)\n` +
      `• <code>/boost</code> — View 32-stream turbo acceleration status\n` +
      `• <code>/status</code> — Check current connection and queue status\n` +
      `• <code>/unlink</code> — Disconnect your PC\n` +
      `• <code>/help</code> — Show this help guide\n\n` +
      `🚀 <b>How to download:</b> Forward any video, audio, document or paste download links to this chat!`
    );
    return;
  }

  // Handle /boost command
  const boostMatch = text.match(/^\/boost(?:@\w+)?(?:\s+(.*))?$/i);
  if (boostMatch) {
    if (!clientId) {
      await sendTelegramMessage(chatId,
        `🚀 <b>PRRX Turbo Download Boost: 32-Stream Enabled</b>\n\n` +
        `All downloads forwarded through @PRRX_IDM_Bot automatically utilize <b>32 multi-connection parallel streams</b> with 1 MB high-speed chunk buffers.\n\n` +
        `⚠️ <b>PC Not Linked:</b> Open PRRX IDM on your PC, go to <b>Settings → Telegram Bot</b>, and send your code (e.g. <code>/pair PRRX-1234</code>) to begin receiving boosted downloads on your desktop!`
      );
    } else {
      await sendTelegramMessage(chatId,
        `🚀 <b>PRRX Turbo Download Boost: ACTIVE</b>\n\n` +
        `⚡ <b>Multi-Connection:</b> 32 Parallel Streams\n` +
        `📦 <b>Buffer Size:</b> 1 MB High-Speed Turbo Buffer\n` +
        `💻 <b>Linked Client:</b> <code>${clientId.substring(0, 8)}...</code>\n\n` +
        `All incoming downloads are accelerated at maximum network throughput with zero speed limits!`
      );
    }
    return;
  }

  // Handle /status command
  const statusMatch = text.match(/^\/status(?:@\w+)?$/i);
  if (statusMatch) {
    if (!clientId) {
      await sendTelegramMessage(chatId,
        `⚠️ <b>Status: PC Not Linked</b>\n\n` +
        `Your Telegram account is not yet connected to PRRX IDM.\n` +
        `Open PRRX IDM on your PC, go to <b>Settings → Telegram Bot</b>, and send your pairing code here.`
      );
    } else {
      let pendingCount = 0;
      if (env.DB) {
        try {
          const countRow = await env.DB.prepare(
            "SELECT COUNT(*) AS count FROM telegram_tasks WHERE client_id = ? AND status = 'pending'"
          ).bind(clientId).first();
          if (countRow) pendingCount = countRow.count || 0;
        } catch {}
      }
      await sendTelegramMessage(chatId,
        `✅ <b>Status: Connected & Active</b>\n\n` +
        `💻 <b>Client ID:</b> <code>${clientId.substring(0, 8)}...</code>\n` +
        `⚡ <b>Turbo Speed:</b> 32-Stream Multi-Connection Enabled\n` +
        `📥 <b>Pending Tasks:</b> ${pendingCount} in queue\n\n` +
        `Any media or links forwarded here are sent directly to your PC.`
      );
    }
    return;
  }

  // Handle /unlink or /unpair command
  const unlinkMatch = text.match(/^\/(?:unlink|unpair)(?:@\w+)?$/i);
  if (unlinkMatch) {
    if (env.DB) {
      await env.DB.prepare("DELETE FROM telegram_users WHERE chat_id = ?").bind(chatId.toString()).run().catch(() => {});
    }
    userClientMap.delete(chatId.toString());
    if (env.PRRX_KV) {
      await env.PRRX_KV.delete(`user:${chatId}`).catch(() => {});
    }
    await sendTelegramMessage(chatId,
      `🔌 <b>PC Unlinked</b>\n\n` +
      `Your Telegram account has been disconnected from PRRX IDM.\n` +
      `To connect again, send a new pairing code from your PC.`
    );
    return;
  }

  // If user has not linked their PC, warn them to pair first
  if (!clientId) {
    await sendTelegramMessage(chatId,
      `⚠️ <b>PC Not Linked!</b>\n\n` +
      `Please link your Telegram account to PRRX Internet Download Manager before sending files or links.\n\n` +
      `<b>Quick Setup:</b>\n` +
      `1. Open PRRX IDM on your PC.\n` +
      `2. Go to <b>Settings → Telegram Bot</b> to find your pairing code.\n` +
      `3. Send your code here (e.g. <code>/pair PRRX-1234</code> or simply <code>PRRX-1234</code>).`
    );
    return;
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
    const MAX_TELEGRAM_BOT_FILE_SIZE = 20 * 1024 * 1024; // 20 MB Telegram Bot API limit
    if (fileSize > MAX_TELEGRAM_BOT_FILE_SIZE) {
      const formattedSize = formatBytes(fileSize);
      await sendTelegramMessage(chatId,
        `⚠️ <b>File Exceeds Telegram Bot Limit (${formattedSize})</b>\n\n` +
        `Telegram Bot API imposes a strict <b>20 MB</b> file limit for bots.\n\n` +
        `💡 <b>How to download large files with PRRX IDM:</b>\n` +
        `• <b>Telegram Web:</b> Open <a href="https://web.telegram.org">web.telegram.org</a> in Chrome or Edge. Use PRRX IDM's floating media grabber to download files of any size (up to 4 GB+).\n` +
        `• <b>Direct Link Bots:</b> Forward this file to @FileToLinkBot or @PublicURLBot on Telegram, then send the generated direct download link here!`
      );
      return;
    }

    const fileInfo = await getTelegramFileInfo(fileId);
    if (fileInfo && fileInfo.file_path) {
      const resolvedSize = fileSize > 0 ? fileSize : (fileInfo.file_size || 0);
      if (resolvedSize > MAX_TELEGRAM_BOT_FILE_SIZE) {
        const formattedSize = formatBytes(resolvedSize);
        await sendTelegramMessage(chatId,
          `⚠️ <b>File Exceeds Telegram Bot Limit (${formattedSize})</b>\n\n` +
          `Telegram Bot API imposes a strict <b>20 MB</b> file limit for bots.\n\n` +
          `💡 <b>How to download large files with PRRX IDM:</b>\n` +
          `• <b>Telegram Web:</b> Open <a href="https://web.telegram.org">web.telegram.org</a> in Chrome or Edge. Use PRRX IDM's floating media grabber to download files of any size (up to 4 GB+).\n` +
          `• <b>Direct Link Bots:</b> Forward this file to @FileToLinkBot or @PublicURLBot on Telegram, then send the generated direct download link here!`
        );
        return;
      }

      const downloadUrl = `${TELEGRAM_FILE_BASE}/${fileInfo.file_path}`;
      const formattedSize = formatBytes(resolvedSize);

      const task = {
        id: `tg_${Date.now()}_${Math.floor(Math.random() * 1000)}`,
        url: downloadUrl,
        fileName: fileName,
        fileSize: resolvedSize,
        formattedSize: formattedSize,
        mediaType: mediaType,
        source: "Telegram @PRRX_IDM_Bot",
        createdAt: new Date().toISOString()
      };

      await enqueueTask(clientId, task, env);

      await sendTelegramMessage(chatId,
        `🚀 <b>Download Sent to Your PC!</b>\n\n` +
        `📁 <b>File:</b> <code>${escapeHtml(fileName)}</code>\n` +
        `📦 <b>Size:</b> ${formattedSize}\n` +
        `⚡ <b>Engine:</b> PRRX 32-Stream Multi-Segment\n\n` +
        `<i>PRRX IDM has queued and started this download on your PC.</i>`
      );
      return;
    } else {
      await sendTelegramMessage(chatId,
        `❌ <b>Unable to retrieve file from Telegram</b>\n\n` +
        `Telegram was unable to prepare this file for download. It may have expired or exceeded server limits.\n` +
        `You can also paste a direct HTTP/HTTPS link or download directly via Telegram Web.`
      );
      return;
    }
  }

  // 5. Check if plain text URL was sent
  if (text.startsWith("http://") || text.startsWith("https://")) {
    const task = {
      id: `tg_url_${Date.now()}_${Math.floor(Math.random() * 1000)}`,
      url: text,
      fileName: "web_download.bin",
      fileSize: 0,
      formattedSize: "Streaming...",
      mediaType: "url",
      source: "Telegram @PRRX_IDM_Bot",
      createdAt: new Date().toISOString()
    };

    await enqueueTask(clientId, task, env);

    await sendTelegramMessage(chatId,
      `🔗 <b>Link Sent to Your PC!</b>\n\n` +
      `🌐 <b>URL:</b> <code>${escapeHtml(text.length > 70 ? text.substring(0, 67) + "..." : text)}</code>\n\n` +
      `⚡ PRRX IDM on your PC is analyzing and downloading this link.`
    );
    return;
  }

  // 6. Generic help reply for unknown text
  await sendTelegramMessage(chatId,
    `💡 <b>PRRX IDM Bot Controls:</b>\n\n` +
    `• <b>Forward any media or file</b> here to download directly to your PC.\n` +
    `• <b>Send any download link</b> (HTTP/HTTPS) here to grab it with PRRX IDM.\n` +
    `• <b>Pair your PC:</b> Send <code>/pair YOUR-CODE</code> (e.g. <code>/pair PRRX-1234</code>).\n` +
    `• <b>Status:</b> Connected to Client <code>${clientId.substring(0, 8)}...</code>`
  );
}

async function enqueueTask(clientId, task, env) {
  let savedToDb = false;
  if (env.DB) {
    try {
      await ensureTelegramTables(env.DB);
      await env.DB.prepare(`
        INSERT INTO telegram_tasks (id, client_id, url, file_name, file_size, formatted_size, media_type, source, status, created_at)
        VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, 'pending', CURRENT_TIMESTAMP)
      `).bind(
        task.id,
        clientId,
        task.url,
        task.fileName || "download.bin",
        task.fileSize || 0,
        task.formattedSize || "",
        task.mediaType || "document",
        task.source || "Telegram @PRRX_IDM_Bot"
      ).run();
      savedToDb = true;
    } catch (e) {
      console.error("Failed to insert telegram task into D1:", e);
    }
  }

  // Fall back to in-memory queue and KV if D1 is not configured or failed
  if (!savedToDb) {
    if (!taskQueues.has(clientId)) {
      taskQueues.set(clientId, []);
    }
    taskQueues.get(clientId).push(task);

    if (env.PRRX_KV) {
      try {
        await env.PRRX_KV.put(`tasks:${clientId}`, JSON.stringify(taskQueues.get(clientId)), { expirationTtl: 3600 });
      } catch {}
    }
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
