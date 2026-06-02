namespace DnnManager.Presentation.Tui;

/// <summary>Lightweight wrapper around <see cref="Console"/> primitives.</summary>
internal sealed class ConsoleScreen
{
    public int Width  => Math.Max(40, Console.WindowWidth);
    public int Height => Math.Max(10, Console.WindowHeight);

    public void Clear()
    {
        Console.BackgroundColor = Theme.Background;
        Console.ForegroundColor = Theme.Foreground;
        Console.Clear();
    }

    public void HideCursor() { try { Console.CursorVisible = false; } catch { } }
    public void ShowCursor() { try { Console.CursorVisible = true; } catch { } }

    public void Write(int left, int top, string text, ConsoleColor fg, ConsoleColor? bg = null)
    {
        if (top < 0 || top >= Height) return;
        Console.SetCursorPosition(Math.Max(0, left), top);
        Console.ForegroundColor = fg;
        Console.BackgroundColor = bg ?? Theme.Background;
        var maxLen = Math.Max(0, Width - left - 1);
        if (text.Length > maxLen) text = text[..maxLen];
        Console.Write(text);
    }

    public void WriteLine(string text, ConsoleColor fg, ConsoleColor? bg = null)
    {
        Console.ForegroundColor = fg;
        Console.BackgroundColor = bg ?? Theme.Background;
        Console.WriteLine(text);
    }

    public void DrawBox(int left, int top, int width, int height, ConsoleColor fg)
    {
        if (width < 2 || height < 2) return;
        var top1 = "┌" + new string('─', width - 2) + "┐";
        var mid  = "│" + new string(' ', width - 2) + "│";
        var bot1 = "└" + new string('─', width - 2) + "┘";
        Write(left, top, top1, fg);
        for (int y = 1; y < height - 1; y++) Write(left, top + y, mid, fg);
        Write(left, top + height - 1, bot1, fg);
    }

    public void DrawCentredTitle(int top, string title, ConsoleColor fg)
    {
        // Plain centered title text spanning the terminal width: no background,
        // no horizontal rule \u2014 just the title positioned in the middle of the row.
        var width = Math.Max(1, Width - 1);
        var safe = title.Length > width ? title[..width] : title;
        var x = Math.Max(0, (width - safe.Length) / 2);
        Write(x, top, safe, fg);
    }

    public void Reset()
    {
        Console.ResetColor();
    }
}
