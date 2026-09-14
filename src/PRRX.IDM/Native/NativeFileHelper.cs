// ============================================================================
// Copyright (c) 2026 PRRX Cooperation. All Rights Reserved.
// PRRX IDM (TM) - Intelligent Download Manager Engine
// Watermark: PRRX-IDM-CORE-WATERMARK-SECURE-VAULT-2026
// Confidential and Proprietary - Licensed under PRRX Open Source Initiative
// ============================================================================

using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PRRX.IDM.Native
{
    /// <summary>
    /// High-performance Win32 filesystem helper that pre-allocates clusters on disk
    /// to eliminate allocation pauses, prevent fragmentation, and accelerate writes on fiber connections.
    /// </summary>
    public static class NativeFileHelper
    {
        private const int FileAllocationInfo = 5;

        [StructLayout(LayoutKind.Sequential)]
        private struct FILE_ALLOCATION_INFO
        {
            public long AllocationSize;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFileInformationByHandle(
            SafeFileHandle hFile,
            int FileInformationClass,
            ref FILE_ALLOCATION_INFO lpFileInformation,
            uint dwBufferSize);

        /// <summary>
        /// Pre-allocates contiguous disk clusters using Win32 SetFileInformationByHandle.
        /// Falls back to FileStream.SetLength if the native call fails or on unsupported filesystems.
        /// </summary>
        public static bool FastPreallocate(FileStream stream, long totalBytes)
        {
            if (stream == null || totalBytes <= 0) return false;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                try
                {
                    var allocInfo = new FILE_ALLOCATION_INFO { AllocationSize = totalBytes };
                    bool success = SetFileInformationByHandle(
                        stream.SafeFileHandle,
                        FileAllocationInfo,
                        ref allocInfo,
                        (uint)Marshal.SizeOf<FILE_ALLOCATION_INFO>());

                    if (success)
                    {
                        // Also set logical length so writes can seek anywhere within the preallocated range
                        stream.SetLength(totalBytes);
                        return true;
                    }
                }
                catch
                {
                    // Fall back to standard SetLength
                }
            }

            try
            {
                stream.SetLength(totalBytes);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
