using DnnManager.Application.Abstractions;

namespace DnnManager.Presentation.Tui;

/// <summary>Adapts the TUI to the application-layer reporter/prompt interfaces.</summary>
internal sealed class TuiProgressReporter : IProgressReporter
{
    private readonly StatusWriter _writer;
    public TuiProgressReporter(StatusWriter writer) => _writer = writer;
    public void Step(string title)   => _writer.Step(title);
    public void Info(string message) => _writer.Info(message);
    public void Success(string m)    => _writer.Success(m);
    public void Fail(string m)       => _writer.Fail(m);
    public void Progress(string m)   => _writer.Progress(m);
}

internal sealed class TuiUserPrompt : IUserPrompt
{
    private readonly ConfirmDialog _dialog;
    public TuiUserPrompt(ConfirmDialog dialog) => _dialog = dialog;
    public Task<bool> ConfirmAsync(string q, bool defaultYes, CancellationToken ct)
        => Task.FromResult(_dialog.Show(q, defaultYes));
}
