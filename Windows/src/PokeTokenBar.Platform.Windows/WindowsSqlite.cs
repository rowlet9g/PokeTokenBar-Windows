using System.Runtime.InteropServices;

namespace PokeTokenBar.Platform.Windows;

internal static class WindowsSqlite
{
    private const int SqliteOpenReadOnly = 0x00000001;
    private const int SqliteRow = 100;

    public static bool TryOpenReadOnly(string path, out Database database)
    {
        var result = sqlite3_open_v2(path, out var handle, SqliteOpenReadOnly, IntPtr.Zero);
        if (result == 0 && handle != IntPtr.Zero)
        {
            database = new Database(handle);
            return true;
        }

        if (handle != IntPtr.Zero)
        {
            sqlite3_close(handle);
        }

        database = null!;
        return false;
    }

    internal sealed class Database : IDisposable
    {
        private IntPtr _handle;

        public Database(IntPtr handle) => _handle = handle;

        public bool TryScalarInt64(string sql, out long value)
        {
            value = 0;
            if (!TryPrepare(sql, out var statement))
            {
                return false;
            }

            using (statement)
            {
                if (!statement.Step())
                {
                    return true;
                }

                value = statement.ColumnInt64(0);
                return true;
            }
        }

        public bool TryPrepare(string sql, out Statement statement)
        {
            var result = sqlite3_prepare_v2(_handle, sql, -1, out var handle, IntPtr.Zero);
            if (result == 0 && handle != IntPtr.Zero)
            {
                statement = new Statement(handle);
                return true;
            }

            if (handle != IntPtr.Zero)
            {
                sqlite3_finalize(handle);
            }

            statement = null!;
            return false;
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                sqlite3_close(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }

    internal sealed class Statement : IDisposable
    {
        private IntPtr _handle;

        public Statement(IntPtr handle) => _handle = handle;

        public void BindInt64(int index, long value) => sqlite3_bind_int64(_handle, index, value);

        public bool Step() => sqlite3_step(_handle) == SqliteRow;

        public bool StepChecked()
        {
            var result = sqlite3_step(_handle);
            return result switch
            {
                SqliteRow => true,
                101 => false,
                _ => throw new IOException($"SQLite read failed (code {result})."),
            };
        }

        public long ColumnInt64(int index) => sqlite3_column_int64(_handle, index);

        public string? ColumnText(int index)
        {
            var pointer = sqlite3_column_text(_handle, index);
            if (pointer == IntPtr.Zero)
            {
                return null;
            }

            var length = sqlite3_column_bytes(_handle, index);
            return Marshal.PtrToStringUTF8(pointer, length);
        }

        public byte[]? ColumnBlob(int index)
        {
            var length = sqlite3_column_bytes(_handle, index);
            if (length < 0)
            {
                return null;
            }

            if (length == 0)
            {
                return [];
            }

            var pointer = sqlite3_column_blob(_handle, index);
            if (pointer == IntPtr.Zero)
            {
                return null;
            }

            var bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            return bytes;
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                sqlite3_finalize(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filename,
        out IntPtr database,
        int flags,
        IntPtr virtualFileSystem);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr database);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(
        IntPtr database,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sql,
        int bytes,
        out IntPtr statement,
        IntPtr tail);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_step(IntPtr statement);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_finalize(IntPtr statement);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_int64(IntPtr statement, int index, long value);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern long sqlite3_column_int64(IntPtr statement, int index);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_text(IntPtr statement, int index);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_column_blob(IntPtr statement, int index);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_column_bytes(IntPtr statement, int index);
}
