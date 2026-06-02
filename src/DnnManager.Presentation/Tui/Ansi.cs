using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DnnManager.Presentation.Tui;

/// <summary>
/// Helpers for emitting ANSI/VT escape sequences that modern terminals
/// (Windows Terminal, conhost on recent Windows 10/11, VS Code, most
/// xterm-compatible terminals) understand.
/// </summary>
internal static class Ansi
{
    private const string ESC = "\u001b";
    private static readonly Regex UrlRegex = new(
        @"https?://[^\s\)\]\>""']+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Rewrites every http(s) URL inside <paramref name="text"/> as an OSC 8
    /// clickable hyperlink while leaving the displayed text unchanged.
    /// Terminals that don't understand OSC 8 simply render the URL as plain
    /// text (the escape sequences are silently dropped).
    /// </summary>
    public static string Linkify(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("http", StringComparison.OrdinalIgnoreCase))
            return text;

        return UrlRegex.Replace(text, m =>
        {
            // OSC 8 ; ; URL ST <text> OSC 8 ; ; ST   (ST = ESC \)
            var url = m.Value;
            return $"{ESC}]8;;{url}{ESC}\\{url}{ESC}]8;;{ESC}\\";
        });
    }

    /// <summary>
    /// Ensures the current console honors VT escape sequences (needed on older
    /// Windows conhost builds; modern Windows Terminal already enables it).
    /// </summary>
    public static void EnableVirtualTerminalProcessing()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var handle = GetStdHandle(STD_OUTPUT_HANDLE);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return;
            if (!GetConsoleMode(handle, out var mode)) return;
            SetConsoleMode(handle, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
        }
        catch
        {
            // Best-effort: if VT mode can't be enabled, hyperlinks just render
            // as raw escape sequences (or as plain URLs on most terminals).
        }
    }

    private const int STD_OUTPUT_HANDLE = -11;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);
}
