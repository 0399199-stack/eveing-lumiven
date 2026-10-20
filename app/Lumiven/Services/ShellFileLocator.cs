using System.Runtime.InteropServices;

namespace Lumiven.Services;

internal static class ShellFileLocator
{
    // Called on the WinUI STA, where COM has already been initialized.
    public static void ShowFile(string path)
    {
        string fullPath = GetExistingFilePath(path);
        IntPtr item = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(fullPath, IntPtr.Zero, out item, 0, out _));
            // With cidl=0, the absolute PIDL identifies the file to select in its parent folder.
            Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(item, 0, IntPtr.Zero, 0));
        }
        finally
        {
            if (item != IntPtr.Zero) Marshal.FreeCoTaskMem(item);
        }
    }

    internal static string GetExistingFilePath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException(Localizer.T("视频文件已移动或删除"), fullPath);
        return fullPath;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr item, uint attributesIn, out uint attributesOut);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHOpenFolderAndSelectItems(IntPtr folder, uint count, IntPtr items, uint flags);
}
