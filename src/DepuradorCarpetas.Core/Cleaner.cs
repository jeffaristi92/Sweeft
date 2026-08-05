using System.Runtime.InteropServices;

namespace DepuradorCarpetas.Core;

/// <summary>Deletion strategy to apply to a finding.</summary>
public enum DeleteMode
{
    /// <summary>Sends to the Recycle Bin (recoverable). Recommended.</summary>
    RecycleBin,

    /// <summary>Permanent, irreversible deletion.</summary>
    Permanent,
}

/// <summary>Result of attempting to delete an item.</summary>
public sealed record DeleteOutcome(string Path, bool Success, long FreedBytes, string? Error);

/// <summary>Safely deletes the selected items.</summary>
public sealed class Cleaner
{
    /// <summary>Deletes a single finding using the given strategy.</summary>
    public DeleteOutcome Delete(Finding finding, DeleteMode mode)
    {
        try
        {
            if (mode == DeleteMode.RecycleBin && OperatingSystem.IsWindows())
            {
                RecycleBin.Send(finding.Path, finding.IsDirectory);
            }
            else
            {
                if (finding.IsDirectory)
                    Directory.Delete(finding.Path, recursive: true);
                else
                    File.Delete(finding.Path);
            }
            return new DeleteOutcome(finding.Path, Success: true, finding.SizeBytes, Error: null);
        }
        catch (Exception ex)
        {
            return new DeleteOutcome(finding.Path, Success: false, FreedBytes: 0, Error: ex.Message);
        }
    }

    /// <summary>Deletes a set of findings, reporting progress per item.</summary>
    public IReadOnlyList<DeleteOutcome> DeleteMany(
        IEnumerable<Finding> findings,
        DeleteMode mode,
        Action<DeleteOutcome>? onEach = null)
    {
        var outcomes = new List<DeleteOutcome>();
        foreach (var finding in findings)
        {
            var outcome = Delete(finding, mode);
            outcomes.Add(outcome);
            onEach?.Invoke(outcome);
        }
        return outcomes;
    }
}

/// <summary>Sends items to the Recycle Bin via the native Windows API (SHFileOperation).</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class RecycleBin
{
    private const int FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOERRORUI = 0x0400;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    public static void Send(string path, bool isDirectory)
    {
        // pFrom must be terminated with a double null character.
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + '\0' + '\0',
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI,
        };

        int result = SHFileOperation(ref op);
        if (result != 0)
            throw new IOException($"Could not send to the Recycle Bin (code {result}): {path}");
        if (op.fAnyOperationsAborted)
            throw new IOException($"The Recycle Bin operation was aborted: {path}");
    }
}
