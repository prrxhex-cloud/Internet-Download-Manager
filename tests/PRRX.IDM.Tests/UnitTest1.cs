// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace PRRX.IDM.Tests;

public class UnitTest1
{
    [Fact]
    public void ExtensionKey_DerivesToFixedExtensionId()
    {
        var base64Key = "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAveacOpVKoYsZLnuVLgi7CXleQxsUhP82qwmRxfCItAYKp1pdVyOwqQD3Rw+l8TknZeGwebXAty1NYmHNV0bYQA5XF/TO1l5WOQYevoGoAGVQJK+kv/tB3aqwkQ7s5kC4jitjmI7MtlZmev99rwlGP9k/9jcqGq+8o3GHDyfEa/2psyxkDWFe9ev7i22VcJw7DSpri8UmChUEY3ha8cfU0Q+DruRKGrTPrZL1/HIvPQEWCCBUjYJKBBbhqdFnS+juXO+C38i72pxryzqwVA7WtjvJSEU9AagC0VVmJJQISXYI06Bu1drr7OAjrKLpE6IIFw0yOx9Up7oVb4WR0biPOQIDAQAB";
        var pubKeyBytes = Convert.FromBase64String(base64Key);

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(pubKeyBytes);
        var sb = new StringBuilder(32);
        for (int i = 0; i < 16; i++)
        {
            byte b = hash[i];
            sb.Append((char)('a' + ((b >> 4) & 0x0F)));
            sb.Append((char)('a' + (b & 0x0F)));
        }

        var derivedId = sb.ToString();
        Assert.Equal(PRRX.IDM.Services.BrowserIntegrationService.FixedExtensionId, derivedId);
    }
}