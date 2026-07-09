using System.Runtime.InteropServices;

namespace KnownFolderRelocator.Shell;

internal static partial class NativeMethods
{
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int SHSetKnownFolderPath(
        ref Guid rfid,
        uint dwFlags,
        IntPtr hToken,
        string pszPath);

    [LibraryImport("shell32.dll")]
    internal static partial int SHGetKnownFolderPath(
        ref Guid rfid,
        uint dwFlags,
        IntPtr hToken,
        out IntPtr ppszPath);

    [LibraryImport("ole32.dll")]
    internal static partial void CoTaskMemFree(IntPtr pv);

    [LibraryImport("shell32.dll")]
    internal static partial void SHChangeNotify(
        uint wEventId,
        uint uFlags,
        IntPtr dwItem1,
        IntPtr dwItem2);
}
