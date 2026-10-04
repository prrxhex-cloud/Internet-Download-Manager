// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PRRX.IDM.Services
{
    public enum HealingActionType
    {
        None,
        BypassSslCertificate,
        StripRangeAndCustomHeaders,
        ForceSingleStream,
        RouteThroughCloudResolver,
        RotateUserAgent,
        ExponentialBackoffRetry,
        ReduceConcurrency
    }

    public class AiRemediationPlan
    {
        [JsonPropertyName("action")]
        public string ActionName { get; set; } = "none";

        [JsonPropertyName("healing_type")]
        public HealingActionType HealingType { get; set; } = HealingActionType.None;

        [JsonPropertyName("recommended_action")]
        public string RecommendedAction { get; set; } = string.Empty;

        [JsonPropertyName("bypass_ssl")]
        public bool BypassSsl { get; set; }

        [JsonPropertyName("strip_range")]
        public bool StripRange { get; set; }

        [JsonPropertyName("strip_custom_headers")]
        public bool StripCustomHeaders { get; set; }

        [JsonPropertyName("force_single_stream")]
        public bool ForceSingleStream { get; set; }

        [JsonPropertyName("use_cloud_resolver")]
        public bool UseCloudResolver { get; set; }

        [JsonPropertyName("concurrency_override")]
        public int ConcurrencyOverride { get; set; } = 0;

        [JsonPropertyName("explanation")]
        public string Explanation { get; set; } = string.Empty;

        [JsonPropertyName("source")]
        public string Source { get; set; } = "Local Heuristic AI";
    }

    /// <summary>
    /// Autonomous AI Self-Healing & Diagnostic Engine.
    /// Internal-only AI processing (No Chatbots/Assistants).
    /// Diagnoses download network faults, SSL mismatches, HTTP 400 Bad Requests,
    /// and streaming bot protections, automatically applying self-healing remediation.
    /// </summary>
    public class AiSelfHealingEngine
    {
        private static readonly Lazy<AiSelfHealingEngine> _lazyInstance = new(() => new AiSelfHealingEngine());
        public static AiSelfHealingEngine Current => _lazyInstance.Value;

        private readonly HttpClient _cloudClient;
        public const string RemediationProxyUrl = "https://prrx-api.sayurusenavirathna70.workers.dev/api/cloud/ai/remediate";

        public event EventHandler<AiRemediationPlan>? RemediationApplied;

        public AiSelfHealingEngine(HttpClient? httpClient = null)
        {
            _cloudClient = httpClient ?? new HttpClient(new SocketsHttpHandler
            {
                UseProxy = false,
                ConnectTimeout = TimeSpan.FromSeconds(3),
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (_, _, _, _) => true,
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
                }
            })
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
        }

        /// <summary>
        /// Instantly classifies error through local pattern matching heuristics.
        /// </summary>
        public AiRemediationPlan DiagnoseAndRemediate(
            string url,
            Exception? exception,
            int? statusCode = null,
            string? errorText = null)
        {
            var msg = (exception?.Message ?? "") + " " + (exception?.InnerException?.Message ?? "") + " " + (errorText ?? "");
            var plan = new AiRemediationPlan();

            // 1. SSL Handshake / Certificate Failure
            if (msg.Contains("SSL", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("certificate", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("RemoteCertificate", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("AuthenticationException", StringComparison.OrdinalIgnoreCase) ||
                (exception is System.Security.Authentication.AuthenticationException))
            {
                plan.HealingType = HealingActionType.BypassSslCertificate;
                plan.ActionName = "bypass_ssl_tls";
                plan.BypassSsl = true;
                plan.RecommendedAction = "Bypass untrusted SSL certificate and enable resilient TLS 1.2/1.3 fallback.";
                plan.Explanation = "Target host certificate failed standard chain verification (common with APK/media mirrors).";
            }
            // 2. HTTP 400 Bad Request or HTTP 416 Range Not Satisfiable
            else if (statusCode == 400 || statusCode == 416 ||
                     msg.Contains("400 (Bad Request)", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("400 (Bad request)", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("416", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("BadRequest", StringComparison.OrdinalIgnoreCase))
            {
                plan.HealingType = HealingActionType.StripRangeAndCustomHeaders;
                plan.ActionName = "strip_range_and_headers";
                plan.StripRange = true;
                plan.StripCustomHeaders = true;
                plan.ForceSingleStream = true;
                plan.ConcurrencyOverride = 1;
                plan.RecommendedAction = "Strip Range and custom headers, and fall back to clean single-stream HTTP GET.";
                plan.Explanation = "Target server rejects multi-part Range chunking or strict client headers.";
            }
            // 3. YouTube / Streaming Media Bot Verification Block
            else if (msg.Contains("confirm you're not a bot", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("LOGIN_REQUIRED", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("bot protection", StringComparison.OrdinalIgnoreCase) ||
                     (url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase)))
            {
                plan.HealingType = HealingActionType.RouteThroughCloudResolver;
                plan.ActionName = "route_cloud_resolver";
                plan.UseCloudResolver = true;
                plan.RecommendedAction = "Route streaming request through SASA Cloud Resolver Gateway.";
                plan.Explanation = "Direct extraction encountered bot verification wall; bypassing via Cloudflare Edge.";
            }
            // 4. Network socket stalls / packet drops
            else if (exception is TimeoutException ||
                     exception is OperationCanceledException ||
                     msg.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
                     msg.Contains("connection reset", StringComparison.OrdinalIgnoreCase))
            {
                plan.HealingType = HealingActionType.ExponentialBackoffRetry;
                plan.ActionName = "retry_with_reduced_concurrency";
                plan.ConcurrencyOverride = 8;
                plan.RecommendedAction = "Apply exponential backoff and reduce concurrency to 8 threads.";
                plan.Explanation = "Network socket timeout or packet drop detected on active route.";
            }
            else
            {
                plan.HealingType = HealingActionType.ForceSingleStream;
                plan.ActionName = "force_single_stream";
                plan.ForceSingleStream = true;
                plan.RecommendedAction = "Execute single-stream fallback with standard browser headers.";
                plan.Explanation = "Unrecognized download error; fallback to conservative single-stream GET.";
            }

            RemediationApplied?.Invoke(this, plan);
            Debug.WriteLine($"[AI Self-Healing] {plan.RecommendedAction} ({plan.Explanation})");
            return plan;
        }

        /// <summary>
        /// Queries the backend Cloudflare Worker AI remediation proxy (Claude on SASA DEV APIS)
        /// for deep analysis of complex error telemetry.
        /// </summary>
        public async Task<AiRemediationPlan> QueryCloudRemediationAsync(
            string url,
            string errorText,
            int? statusCode = null,
            CancellationToken ct = default)
        {
            var localFallback = DiagnoseAndRemediate(url, null, statusCode, errorText);

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(4));

                var payload = new
                {
                    url,
                    error = errorText,
                    statusCode = statusCode?.ToString() ?? "",
                    context = "PRRX IDM Download Error Diagnostic"
                };

                var jsonContent = new StringContent(
                    JsonSerializer.Serialize(payload),
                    System.Text.Encoding.UTF8,
                    "application/json");

                using var response = await _cloudClient.PostAsync(RemediationProxyUrl, jsonContent, cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    var resJson = await response.Content.ReadAsStringAsync(cts.Token);
                    using var doc = JsonDocument.Parse(resJson);
                    if (doc.RootElement.TryGetProperty("recommendation", out var recEl))
                    {
                        var plan = new AiRemediationPlan
                        {
                            Source = "Cloud AI (Claude @ SASA DEV APIS)",
                            ActionName = recEl.TryGetProperty("action", out var a) ? a.GetString() ?? "" : localFallback.ActionName,
                            RecommendedAction = recEl.TryGetProperty("recommended_action", out var ra) ? ra.GetString() ?? "" : localFallback.RecommendedAction,
                            Explanation = recEl.TryGetProperty("explanation", out var exp) ? exp.GetString() ?? "" : localFallback.Explanation,
                            BypassSsl = recEl.TryGetProperty("bypass_ssl", out var bs) && bs.GetBoolean(),
                            StripRange = recEl.TryGetProperty("strip_range", out var sr) && sr.GetBoolean(),
                            StripCustomHeaders = recEl.TryGetProperty("strip_custom_headers", out var sc) && sc.GetBoolean(),
                            ForceSingleStream = recEl.TryGetProperty("force_single_stream", out var fs) && fs.GetBoolean(),
                            UseCloudResolver = recEl.TryGetProperty("use_cloud_resolver", out var uc) && uc.GetBoolean()
                        };

                        plan.HealingType = plan.ActionName switch
                        {
                            "bypass_ssl_tls" => HealingActionType.BypassSslCertificate,
                            "strip_range_and_headers" => HealingActionType.StripRangeAndCustomHeaders,
                            "route_cloud_resolver" => HealingActionType.RouteThroughCloudResolver,
                            "retry_with_reduced_concurrency" => HealingActionType.ReduceConcurrency,
                            _ => localFallback.HealingType
                        };

                        RemediationApplied?.Invoke(this, plan);
                        return plan;
                    }
                }
            }
            catch
            {
                // Return fast local heuristic plan if cloud request times out or is offline
            }

            return localFallback;
        }
    }
}
