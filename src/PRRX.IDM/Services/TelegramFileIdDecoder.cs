// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;

namespace PRRX.IDM.Services
{
    public class TelegramDecodedFileId
    {
        public int TypeId { get; set; }
        public int DcId { get; set; }
        public long Id { get; set; }
        public long AccessHash { get; set; }
        public byte[]? FileReference { get; set; }
        public bool IsValid => Id != 0 && DcId > 0 && DcId <= 10;
    }

    public static class TelegramFileIdDecoder
    {
        public static TelegramDecodedFileId? Decode(string fileId)
        {
            if (string.IsNullOrWhiteSpace(fileId)) return null;
            try
            {
                // 1. Base64URL decode
                string base64 = fileId.Trim().Replace('-', '+').Replace('_', '/');
                switch (base64.Length % 4)
                {
                    case 2: base64 += "=="; break;
                    case 3: base64 += "="; break;
                }
                byte[] rawBytes = Convert.FromBase64String(base64);

                // 2. RLE decode
                var decodedList = new List<byte>();
                byte? last = null;
                foreach (var b in rawBytes)
                {
                    if (last == 0)
                    {
                        for (int i = 0; i < b; i++) decodedList.Add(0);
                        last = null;
                    }
                    else
                    {
                        if (last.HasValue) decodedList.Add(last.Value);
                        last = b;
                    }
                }
                if (last.HasValue) decodedList.Add(last.Value);
                byte[] data = decodedList.ToArray();

                if (data.Length < 24) return null;

                // 3. Parse binary TL stream
                using var ms = new MemoryStream(data);
                using var reader = new BinaryReader(ms);

                uint typeIdRaw = reader.ReadUInt32();
                bool hasReference = (typeIdRaw & (1 << 25)) != 0;
                int typeId = (int)(typeIdRaw & ~((1 << 24) | (1 << 25)));

                uint dcId = reader.ReadUInt32();

                byte[]? fileRef = null;
                if (hasReference)
                {
                    fileRef = ReadTlBytes(reader);
                }

                long id = reader.ReadInt64();
                long accessHash = reader.ReadInt64();

                return new TelegramDecodedFileId
                {
                    TypeId = typeId,
                    DcId = (int)dcId,
                    Id = id,
                    AccessHash = accessHash,
                    FileReference = fileRef
                };
            }
            catch
            {
                return null;
            }
        }

        private static byte[] ReadTlBytes(BinaryReader reader)
        {
            byte first = reader.ReadByte();
            int length;
            int padding;
            if (first < 254)
            {
                length = first;
                padding = (4 - ((length + 1) % 4)) % 4;
            }
            else
            {
                length = reader.ReadByte() | (reader.ReadByte() << 8) | (reader.ReadByte() << 16);
                padding = (4 - (length % 4)) % 4;
            }

            byte[] bytes = reader.ReadBytes(length);
            if (padding > 0)
            {
                reader.ReadBytes(padding);
            }
            return bytes;
        }
    }
}
