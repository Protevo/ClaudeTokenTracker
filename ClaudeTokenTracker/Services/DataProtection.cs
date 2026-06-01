using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ClaudeTokenTracker.Services;

/// <summary>
/// Windows DPAPI (Data Protection API) wrapper via P/Invoke, so we don't need the
/// System.Security.Cryptography.ProtectedData NuGet package. Encrypts/decrypts under
/// the current user account — the ciphertext is only usable by the same Windows user.
/// </summary>
internal static class DataProtection
{
    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DATA_BLOB pDataIn, string? szDataDescr, ref DATA_BLOB pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DATA_BLOB pDataIn, IntPtr ppszDataDescr, ref DATA_BLOB pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    public static byte[] Protect(byte[] data, byte[] entropy) => Transform(data, entropy, protect: true);

    public static byte[] Unprotect(byte[] data, byte[] entropy) => Transform(data, entropy, protect: false);

    private static byte[] Transform(byte[] data, byte[] entropy, bool protect)
    {
        DATA_BLOB inBlob = default, entropyBlob = default, outBlob = default;
        try
        {
            FillBlob(data, ref inBlob);
            FillBlob(entropy, ref entropyBlob);

            bool ok = protect
                ? CryptProtectData(ref inBlob, "ClaudeTokenTracker", ref entropyBlob,
                    IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob)
                : CryptUnprotectData(ref inBlob, IntPtr.Zero, ref entropyBlob,
                    IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, ref outBlob);

            if (!ok)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var result = new byte[outBlob.cbData];
            Marshal.Copy(outBlob.pbData, result, 0, outBlob.cbData);
            return result;
        }
        finally
        {
            if (inBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(inBlob.pbData);
            if (entropyBlob.pbData != IntPtr.Zero) Marshal.FreeHGlobal(entropyBlob.pbData);
            if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
        }
    }

    private static void FillBlob(byte[] data, ref DATA_BLOB blob)
    {
        blob.cbData = data.Length;
        blob.pbData = Marshal.AllocHGlobal(Math.Max(1, data.Length));
        if (data.Length > 0)
            Marshal.Copy(data, 0, blob.pbData, data.Length);
    }
}
