namespace DnnManager.Presentation.Tui;

/// <summary>A keyboard-navigable single-select list. Arrows + Enter + Esc.</summary>
internal sealed class SelectableList<T>
{
    public required string Title { get; init; }
    public required IReadOnlyList<T> Items { get; init; }
    public required Func<T, string> Display { get; init; }
    public string? Hint { get; init; } = "↑/↓ navigate · Enter select · Esc cancel";

    private readonly ConsoleScreen _screen;

    public SelectableList(ConsoleScreen screen) => _screen = screen;

    private const int ItemsTop = 4;

    public T? Show(int startIndex = 0)
    {
        if (Items.Count == 0) return default;
        int selected = Math.Clamp(startIndex, 0, Items.Count - 1);
        _screen.HideCursor();

        RenderAll(selected);
        ConsoleInput.Flush();

        int lastWidth = _screen.Width;
        int lastHeight = _screen.Height;

        while (true)
        {
            // Poll for terminal resize while waiting for a key. The whole
            // menu is re-rendered so the divider, items and hint also
            // re-flow at the new width.
            while (!Console.KeyAvailable)
            {
                if (_screen.Width != lastWidth || _screen.Height != lastHeight)
                {
                    lastWidth = _screen.Width;
                    lastHeight = _screen.Height;
                    RenderAll(selected);
                }
                Thread.Sleep(50);
            }

            var key = Console.ReadKey(intercept: true);
            int previous = selected;
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:   selected = (selected - 1 + Items.Count) % Items.Count; break;
                case ConsoleKey.DownArrow: selected = (selected + 1) % Items.Count; break;
                case ConsoleKey.Home:      selected = 0; break;
                case ConsoleKey.End:       selected = Items.Count - 1; break;
                case ConsoleKey.Enter:     _screen.ShowCursor(); return Items[selected];
                case ConsoleKey.Escape:    _screen.ShowCursor(); return default;
                default:
                    // numeric quick-select 1..9
                    if (key.KeyChar >= '1' && key.KeyChar <= '9')
                    {
                        var idx = key.KeyChar - '1';
                        if (idx < Items.Count) { _screen.ShowCursor(); return Items[idx]; }
                    }
                    break;
            }

            if (selected != previous)
            {
                RenderRow(previous, selected: false);
                RenderRow(selected, selected: true);
            }
        }
    }

    private void RenderAll(int selected)
    {
        _screen.Clear();
        _screen.DrawCentredTitle(1, Title, Theme.HeaderFg);
        _screen.Write(0, 2, new string('─', _screen.Width - 1), Theme.Muted);

        for (int i = 0; i < Items.Count; i++)
            RenderRow(i, selected: i == selected);

        if (Hint != null)
            _screen.Write(2, _screen.Height - 2, Hint, Theme.Hint);
    }

    private void RenderRow(int index, bool selected)
    {
        var prefix = selected ? " > " : "   ";
        var label = $"{prefix}{Display(Items[index])}";
        var padded = label.PadRight(_screen.Width - 5);
        if (selected)
            _screen.Write(2, ItemsTop + index, padded, Theme.Selection, Theme.SelectionBg);
        else
            _screen.Write(2, ItemsTop + index, padded, Theme.Foreground);
    }
}
