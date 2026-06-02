using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DnnManager.Presentation.Tui;

/// <summary>
/// Saves a rectangular region of the Windows console screen buffer and
/// restores it later. Used by overlay dialogs so that the text under the
/// dialog box is preserved instead of being erased.
/// Falls back to a simple "fill with background" erase if the Win32 console
/// API is unavailable (e.g. inside a VS Code integrated terminal).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ConsoleRegionSnapshot
{
    private const int STD_OUTPUT_HANDLE = -11;

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD { public short X, Y; public COORD(short x, short y) { X = x; Y = y; } }

    [StructLayout(LayoutKind.Sequential)]
    private struct SMALL_RECT { public short Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CHAR_INFO { public char UnicodeChar; public ushort Attributes; }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ReadConsoleOutputW(
        IntPtr hConsoleOutput,
        [Out] CHAR_INFO[] lpBuffer,
        COORD dwBufferSize,
        COORD dwBufferCoord,
        ref SMALL_RECT lpReadRegion);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WriteConsoleOutputW(
        IntPtr hConsoleOutput,
        [In] CHAR_INFO[] lpBuffer,
        COORD dwBufferSize,
        COORD dwBufferCoord,
        ref SMALL_RECT lpWriteRegion);

    private readonly int _left, _top, _width, _height;
    private readonly CHAR_INFO[]? _buffer;
    private readonly bool _captured;

    private ConsoleRegionSnapshot(int left, int top, int width, int height, CHAR_INFO[]? buffer, bool captured)
    {
        _left = left; _top = top; _width = width; _height = height;
        _buffer = buffer; _captured = captured;
    }

    public static ConsoleRegionSnapshot Capture(int left, int top, int width, int height)
    {
        if (width <= 0 || height <= 0)
            return new ConsoleRegionSnapshot(left, top, width, height, null, false);

        try
        {
            var h = GetStdHandle(STD_OUTPUT_HANDLE);
            if (h == IntPtr.Zero || h == new IntPtr(-1))
                return new ConsoleRegionSnapshot(left, top, width, height, null, false);

            var buffer = new CHAR_INFO[width * height];
            var region = new SMALL_RECT
            {
                Left = (short)left,
                Top = (short)top,
                Right = (short)(left + width - 1),
                Bottom = (short)(top + height - 1)
            };
            var size = new COORD((short)width, (short)height);
            var coord = new COORD(0, 0);
            bool ok = ReadConsoleOutputW(h, buffer, size, coord, ref region);
            return new ConsoleRegionSnapshot(left, top, width, height, ok ? buffer : null, ok);
        }
        catch
        {
            return new ConsoleRegionSnapshot(left, top, width, height, null, false);
        }
    }

    /// <summary>Writes the captured region back to its original location.</summary>
    /// <returns>true if the buffer was restored from a snapshot; false if no snapshot was available.</returns>
    public bool Restore()
    {
        if (!_captured || _buffer is null) return false;

        try
        {
            var h = GetStdHandle(STD_OUTPUT_HANDLE);
            if (h == IntPtr.Zero || h == new IntPtr(-1)) return false;

            var region = new SMALL_RECT
            {
                Left = (short)_left,
                Top = (short)_top,
                Right = (short)(_left + _width - 1),
                Bottom = (short)(_top + _height - 1)
            };
            var size = new COORD((short)_width, (short)_height);
            var coord = new COORD(0, 0);
            return WriteConsoleOutputW(h, _buffer, size, coord, ref region);
        }
        catch
        {
            return false;
        }
    }
}
