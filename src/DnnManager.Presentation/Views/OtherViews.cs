using DnnManager.Application.Abstractions;
using DnnManager.Application.UseCases;
using DnnManager.Presentation.Tui;

namespace DnnManager.Presentation.Views;

internal sealed class RemoveView
{
    private readonly ConsoleScreen _screen;
    private readonly RemoveProjectUseCase _useCase;
    private readonly IProjectRepository _repo;
    private readonly StatusWriter _status;
    private readonly TuiProgressReporter _reporter;

    public RemoveView(ConsoleScreen screen, RemoveProjectUseCase useCase, IProjectRepository repo,
        StatusWriter status, TuiProgressReporter reporter)
    { _screen = screen; _useCase = useCase; _repo = repo; _status = status; _reporter = reporter; }

    public async Task RunAsync(CancellationToken ct)
    {
        var projects = _repo.ListAllProjectDirectories();
        if (projects.Count == 0)
        {
            _screen.Clear();
            _status.Fail("No DNN projects found.");
            _status.Pause();
            return;
        }
        var menu = new SelectableList<string>(_screen)
        {
            Title = "Select a project to remove",
            Items = projects,
            Display = p => p
        };
        var chosen = menu.Show();
        if (chosen is null) return;
        _screen.Clear();
        var r = await _useCase.ExecuteAsync(chosen, _reporter, ct);
        if (!r.Success) _status.Fail(r.Error ?? "Remove failed.");
        _status.Pause();
    }
}

internal sealed class PrerequisitesView
{
    private readonly ConsoleScreen _screen;
    private readonly CheckPrerequisitesUseCase _useCase;
    private readonly StatusWriter _status;
    private readonly TuiProgressReporter _reporter;

    public PrerequisitesView(ConsoleScreen screen, CheckPrerequisitesUseCase useCase, StatusWriter status, TuiProgressReporter reporter)
    { _screen = screen; _useCase = useCase; _status = status; _reporter = reporter; }

    public async Task RunAsync(CancellationToken ct)
    {
        _screen.Clear();
        _screen.DrawCentredTitle(1, "Check prerequisites", Theme.HeaderFg);
        Console.SetCursorPosition(0, 3);
        var r = await _useCase.ExecuteAsync(_reporter, ct);
        if (!r.Success) _status.Fail(r.Error ?? "Some prerequisites are missing.");
        _status.Pause();
    }
}

internal sealed class ProjectsListView
{
    private readonly ConsoleScreen _screen;
    private readonly ListProjectsUseCase _useCase;
    private readonly StatusWriter _status;

    public ProjectsListView(ConsoleScreen screen, ListProjectsUseCase useCase, StatusWriter status)
    { _screen = screen; _useCase = useCase; _status = status; }

    public async Task RunAsync(CancellationToken ct)
    {
        _screen.Clear();
        _screen.DrawCentredTitle(1, "DNN Projects", Theme.HeaderFg);
        Console.SetCursorPosition(0, 3);

        var list = await _useCase.ExecuteAsync(ct);
        if (list.Count == 0) { _status.Info("No projects found."); _status.Pause(); return; }

        foreach (var p in list)
        {
            Console.WriteLine();
            Console.ForegroundColor = Theme.HeaderFg;
            Console.WriteLine($"  ▌ {p.Name}");
            Console.ResetColor();
            WriteRow("Path",        p.ProjectDirectory);
            WriteRow("Size",        $"{p.DirectorySizeBytes / 1024d / 1024d:N1} MB");
            WriteRow("IIS site",    p.IisSiteExists ? $"present ({p.IisSiteState})" : "(none)");
            WriteRow("SQL running", p.ContainerRunning ? "yes" : "no");
            WriteRow("Database",    p.DatabaseName ?? "(unknown)");
            WriteRow("DB user",     p.DatabaseUser ?? "(unknown)");
            WriteRow("SQL port",    p.SqlPort?.ToString() ?? "(unknown)");
        }
        _status.Pause();
    }

    private static void WriteRow(string k, string v)
    {
        Console.ForegroundColor = Theme.Hint;
        Console.Write($"     {k,-12} ");
        Console.ForegroundColor = Theme.Foreground;
        Console.WriteLine(v);
        Console.ResetColor();
    }
}
