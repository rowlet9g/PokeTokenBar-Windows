using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PokeTokenBar.Platform.Windows;

public static class UserDataMigration
{
    public static void EnsureMigrated(string legacy, string destination,
        Func<string, string>? resolvePhysicalPath = null)
    {
        legacy = Path.GetFullPath(legacy);
        destination = Path.GetFullPath(destination);
        resolvePhysicalPath ??= ResolvePhysicalPath;
        // Once created, the new directory is authoritative; never reimport stale data.
        if (Directory.Exists(destination))
        {
            if (!string.Equals(Path.TrimEndingDirectorySeparator(resolvePhysicalPath(destination)),
                    Path.TrimEndingDirectorySeparator(destination), StringComparison.OrdinalIgnoreCase))
                throw new IOException($"새 저장소의 실제 위치가 요청 경로와 다릅니다: {destination}");
            return;
        }
        if (!Directory.Exists(legacy)) return;
        var physical = resolvePhysicalPath(legacy);
        if (!string.Equals(Path.TrimEndingDirectorySeparator(physical),
                Path.TrimEndingDirectorySeparator(legacy), StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("기존 저장 경로가 다른 위치로 연결되어 이전을 중단했습니다. "
                + "PokeTokenBar를 시작 메뉴에서 한 번 실행하십시오. "
                + $"요청: {legacy} / 실제: {physical}");
        }

        var staging = destination + ".migration-" + Guid.NewGuid().ToString("N");
        try
        {
            CopyDirectory(legacy, staging);
            // Renaming the completed sibling directory publishes all files together.
            Directory.Move(staging, destination);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"저장소 이전 중 연결된 경로를 발견했습니다: {source}");
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"저장소 이전 중 연결된 경로를 발견했습니다: {entry}");
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if ((attributes & FileAttributes.Directory) != 0) CopyDirectory(entry, target);
            else
            {
                using var input = new FileStream(entry, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 4096, FileOptions.WriteThrough);
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
            }
        }
    }

    public static string ResolvePhysicalPath(string directory)
    {
        using var handle = CreateFile(directory, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var buffer = new StringBuilder(32768);
        var count = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (count == 0 || count >= buffer.Capacity)
            throw new IOException($"실제 저장 위치를 확인하지 못했습니다: {directory}", new Win32Exception(Marshal.GetLastWin32Error()));
        var path = buffer.ToString();
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + path[8..];
        return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..] : path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share,
        IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path,
        uint length, uint flags);
}
