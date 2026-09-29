using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace ClankerWorld.Viewer.Control;

/// <summary>Installation-local protection, never part of a shareable world save.</summary>
internal static class ProviderCredentialFile
{
    private const string Header = "ClankerWorld-DPAPI-CurrentUser-v1\n";

    public static bool IsProtected(string text) => text.StartsWith(Header, StringComparison.Ordinal);

    public static string Encode(string json)
    {
        if (!OperatingSystem.IsWindows()) return json;
        var clear = Encoding.UTF8.GetBytes(json);
        try { return Header + Convert.ToBase64String(Transform(clear, protect: true)); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public static string Decode(string text)
    {
        if (!IsProtected(text)) return text; // Validated legacy JSON is migrated by the store.
        if (!OperatingSystem.IsWindows())
            throw new InvalidDataException("These provider keys belong to a Windows user. Re-enter keys on this installation; do not replace the protected file with empty state.");
        byte[] encrypted;
        try { encrypted = Convert.FromBase64String(text[Header.Length..]); }
        catch (FormatException) { throw new InvalidDataException("The protected provider file is damaged. Restore a trusted copy for the same Windows user."); }
        var clear = Transform(encrypted, protect: false);
        try { return Encoding.UTF8.GetString(clear); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    [SupportedOSPlatform("windows")]
    private static byte[] Transform(byte[] data, bool protect)
    {
        var input = new DataBlob { Length = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        DataBlob output = default;
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            // UI_FORBIDDEN only: deliberately do not set LOCAL_MACHINE.
            var success = protect
                ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success)
                throw new CryptographicException("Windows could not protect or unlock the provider keys for this user. Preserve the file and use the original Windows account or restore a trusted copy.");
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            ClearNative(input);
            Marshal.FreeHGlobal(input.Data);
            if (output.Data != IntPtr.Zero)
            {
                ClearNative(output);
                _ = LocalFree(output.Data);
            }
        }
    }

    private static void ClearNative(DataBlob blob)
    {
        if (blob.Data != IntPtr.Zero && blob.Length > 0)
            Marshal.Copy(new byte[blob.Length], 0, blob.Data, blob.Length);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
