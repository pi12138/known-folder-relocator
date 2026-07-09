using System.ComponentModel;
using System.Runtime.InteropServices;

namespace KnownFolderRelocator.Shell;

public sealed class KnownFolderService
{
    private const uint ShcneAssocChanged = 0x08000000;

    public string GetPath(Guid knownFolderId)
    {
        var id = knownFolderId;
        var hr = NativeMethods.SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out var pathPointer);
        if (hr != 0)
        {
            throw new Win32Exception(hr, $"SHGetKnownFolderPath failed for {knownFolderId} with HRESULT {FormatHResult(hr)}.");
        }

        try
        {
            return Marshal.PtrToStringUni(pathPointer) ?? string.Empty;
        }
        finally
        {
            if (pathPointer != IntPtr.Zero)
            {
                NativeMethods.CoTaskMemFree(pathPointer);
            }
        }
    }

    public void SetPath(Guid knownFolderId, string path)
    {
        var id = knownFolderId;
        var hr = NativeMethods.SHSetKnownFolderPath(ref id, 0, IntPtr.Zero, path);
        if (hr != 0)
        {
            throw new Win32Exception(hr, $"SHSetKnownFolderPath failed for {knownFolderId} -> {path} with HRESULT {FormatHResult(hr)}.");
        }
    }

    public void NotifyShellChanged() =>
        NativeMethods.SHChangeNotify(ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero);

    private static string FormatHResult(int hr) => $"0x{unchecked((uint)hr):X8}";
}
