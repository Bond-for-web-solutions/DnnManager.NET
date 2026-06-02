namespace DnnManager.Presentation.Tui;

internal sealed class ConfirmDialog
{
    private readonly ConsoleScreen _screen;
    public ConfirmDialog(ConsoleScreen screen) => _screen = screen;

    public bool Show(string question, bool defaultYes = false)
    {
        var width  = Math.Min(_screen.Width - 4, Math.Max(40, question.Length + 6));
        var height = 7;
        var left   = (_screen.Width  - width)  / 2;
        var top    = (_screen.Height - height) / 2;

        // Snapshot the screen rectangle the dialog is about to cover so we
        // can put the original text back when we close \u2014 otherwise erasing
        // the dialog would also wipe whatever logs were underneath it.
        var snapshot = ConsoleRegionSnapshot.Capture(left, top, width, height);

        // Remember where the caller was writing so we can put the cursor back
        // after we erase the dialog \u2014 otherwise subsequent log lines either
        // print inside the dialog area or appear in the wrong place.
        int savedLeft = 0, savedTop = 0;
        try { savedLeft = Console.CursorLeft; savedTop = Console.CursorTop; } catch { }

        bool yes = defaultYes;
        _screen.HideCursor();
        ConsoleInput.Flush();
        try
        {
            while (true)
            {
                _screen.DrawBox(left, top, width, height, Theme.HeaderFg);
                _screen.Write(left + 2, top + 1, "Confirm", Theme.HeaderFg);
                _screen.Write(left + 2, top + 2, question.Length > width - 4 ? question[..(width - 4)] : question, Theme.Foreground);

                var yesText = " [Yes] ";
                var noText  = " [No] ";
                var yx = left + 2;
                var nx = yx + yesText.Length + 3;
                var ry = top + 4;

                _screen.Write(yx, ry, yesText,
                    yes ? Theme.Selection : Theme.Foreground,
                    yes ? Theme.SelectionBg : Theme.Background);
                _screen.Write(nx, ry, noText,
                    !yes ? Theme.Selection : Theme.Foreground,
                    !yes ? Theme.SelectionBg : Theme.Background);

                _screen.Write(left + 2, top + height - 2, "\u2190/\u2192 \u00b7 Y/N \u00b7 Enter", Theme.Hint);

                var key = Console.ReadKey(true);
                switch (key.Key)
                {
                    case ConsoleKey.LeftArrow:  yes = true;  break;
                    case ConsoleKey.RightArrow: yes = false; break;
                    case ConsoleKey.Y:          return true;
                    case ConsoleKey.N:          return false;
                    case ConsoleKey.Enter:      return yes;
                    case ConsoleKey.Escape:     return false;
                }
            }
        }
        finally
        {
            // Try to put back what was on screen before; if the snapshot was
            // unavailable (non-Windows console host) just blank the region.
            if (!snapshot.Restore())
                Erase(left, top, width, height);
            try { Console.SetCursorPosition(savedLeft, savedTop); } catch { }
            _screen.ShowCursor();
        }
    }

    private void Erase(int left, int top, int width, int height)
    {
        var blank = new string(' ', width);
        for (int y = 0; y < height; y++)
            _screen.Write(left, top + y, blank, Theme.Background, Theme.Background);
    }
}

internal sealed class TextPrompt
{
    private readonly ConsoleScreen _screen;
    public TextPrompt(ConsoleScreen screen) => _screen = screen;

    public string? Show(string label, string? initial = null, bool allowEmpty = false)
    {
        Console.ForegroundColor = Theme.HeaderFg;
        Console.Write($"  {label}: ");
        Console.ForegroundColor = Theme.Foreground;
        _screen.ShowCursor();
        var value = initial ?? string.Empty;
        if (!string.IsNullOrEmpty(value)) Console.Write(value);
        ConsoleInput.Flush();

        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                if (!allowEmpty && string.IsNullOrWhiteSpace(value)) return null;
                return value;
            }
            if (key.Key == ConsoleKey.Escape) { Console.WriteLine(); return null; }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Length > 0)
                {
                    value = value[..^1];
                    Console.Write("\b \b");
                }
                continue;
            }
            if (!char.IsControl(key.KeyChar))
            {
                value += key.KeyChar;
                Console.Write(key.KeyChar);
            }
        }
    }
}

internal sealed class StatusWriter
{
    private readonly ConsoleScreen _screen;
    public StatusWriter(ConsoleScreen screen) => _screen = screen;

    // True while a Progress() line is being overwritten in place (no trailing
    // newline yet). The next non-progress write closes it with a newline first.
    private bool _progressOpen;

    public void Step(string title)
    {
        CloseProgress();
        Console.WriteLine();
        Console.ForegroundColor = Theme.HeaderFg;
        Console.WriteLine($"  ── {title} ──");
        Console.ResetColor();
    }
    public void Info(string m)    { CloseProgress(); Console.ForegroundColor = Theme.Info;    Console.WriteLine($"  \u2022 {Ansi.Linkify(m)}"); Console.ResetColor(); }
    public void Success(string m) { CloseProgress(); Console.ForegroundColor = Theme.Success; Console.WriteLine($"  \u2713 {Ansi.Linkify(m)}"); Console.ResetColor(); }
    public void Fail(string m)    { CloseProgress(); Console.ForegroundColor = Theme.Error;   Console.WriteLine($"  \u2717 {Ansi.Linkify(m)}"); Console.ResetColor(); }

    public void Progress(string m)
    {
        var line = $"  \u2022 {m}";
        var width = Math.Max(1, _screen.Width - 1);
        if (line.Length > width) line = line[..width];
        Console.ForegroundColor = Theme.Info;
        Console.Write("\r" + line.PadRight(width));
        Console.ResetColor();
        _progressOpen = true;
    }

    // Finalize an in-place progress line so the next message starts on its own line.
    private void CloseProgress()
    {
        if (!_progressOpen) return;
        Console.WriteLine();
        _progressOpen = false;
    }

    public void Pause(string message = "Press any key to return…")
    {
        Console.WriteLine();
        Console.ForegroundColor = Theme.Hint;
        Console.Write($"  {message}");
        Console.ResetColor();
        ConsoleInput.Flush();
        Console.ReadKey(true);
    }
}
