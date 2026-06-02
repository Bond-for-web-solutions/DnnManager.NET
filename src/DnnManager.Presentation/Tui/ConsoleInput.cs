namespace DnnManager.Presentation.Tui;

internal static class ConsoleInput
{
    /// <summary>
    /// Discards every queued keystroke from the console input buffer.
    /// Call this immediately before a ReadKey that solicits a fresh decision
    /// from the user so keys mashed during a long-running operation don't
    /// auto-select a menu item or dismiss a dialog.
    /// </summary>
    public static void Flush()
    {
        try
        {
            while (Console.KeyAvailable) Console.ReadKey(intercept: true);
        }
        catch
        {
            // Redirected stdin / no console: nothing to flush.
        }
    }
}
