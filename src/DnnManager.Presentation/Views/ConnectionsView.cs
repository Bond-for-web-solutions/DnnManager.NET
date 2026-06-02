using DnnManager.Application.Abstractions;
using DnnManager.Presentation.Tui;

namespace DnnManager.Presentation.Views;

/// <summary>Lets the user browse and edit the saved FTP/SQL connections (connections.json).</summary>
internal sealed class ConnectionsView
{
    private readonly ConsoleScreen _screen;
    private readonly IFtpProfileStore _ftp;
    private readonly ISqlProfileStore _sql;
    private readonly StatusWriter _status;
    private readonly TextPrompt _text;

    public ConnectionsView(ConsoleScreen screen, IFtpProfileStore ftp, ISqlProfileStore sql,
        StatusWriter status, TextPrompt text)
    {
        _screen = screen; _ftp = ftp; _sql = sql; _status = status; _text = text;
    }

    private sealed record NameChoice(string Name);
    private sealed record KindChoice(bool IsFtp, string Label);

    public Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var projects = _ftp.ListProjects()
                .Concat(_sql.ListProjects())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (projects.Count == 0)
            {
                _screen.Clear();
                _screen.DrawCentredTitle(1, "Saved connections", Theme.HeaderFg);
                Console.SetCursorPosition(0, 3);
                _status.Fail("No saved connections yet. Clone a project to create one.");
                _status.Pause();
                return Task.CompletedTask;
            }

            var projMenu = new SelectableList<NameChoice>(_screen)
            {
                Title = "Saved connections — choose project",
                Hint = "↑/↓ · Enter · Esc to go back",
                Items = projects.Select(p => new NameChoice(p)).ToArray(),
                Display = p => p.Name
            };
            var proj = projMenu.Show();
            if (proj is null) return Task.CompletedTask; // back to main menu

            EditProject(proj.Name);
        }
        return Task.CompletedTask;
    }

    private void EditProject(string project)
    {
        while (true)
        {
            var ftp = _ftp.Get(project);
            var sql = _sql.Get(project);

            var items = new[]
            {
                new KindChoice(true, ftp is null
                    ? "FTP  — (none — add)"
                    : $"FTP  — {ftp.User}@{ftp.Host}:{ftp.Port}  {ftp.RemotePath}"),
                new KindChoice(false, sql is null
                    ? "SQL  — (none — add)"
                    : $"SQL  — {sql.User}@{sql.Server}/{sql.Database}"),
            };

            var menu = new SelectableList<KindChoice>(_screen)
            {
                Title = $"'{project}' — edit which connection?",
                Hint = "↑/↓ · Enter · Esc to go back",
                Items = items,
                Display = c => c.Label
            };
            var pick = menu.Show();
            if (pick is null) return;

            if (pick.IsFtp) EditFtp(project, ftp);
            else EditSql(project, sql);
        }
    }

    private void EditFtp(string project, FtpProfile? current)
    {
        _screen.Clear();
        _screen.DrawCentredTitle(1, $"{project} — FTP connection", Theme.HeaderFg);
        Console.SetCursorPosition(0, 3);

        var host = _text.Show("FTP host", current?.Host);
        if (string.IsNullOrWhiteSpace(host)) return;
        var portStr = _text.Show("FTP port", current?.Port.ToString() ?? "21", allowEmpty: true);
        var port = int.TryParse(portStr, out var p) ? p : 21;
        var user = _text.Show("FTP user", current?.User ?? "", allowEmpty: true) ?? "";
        var pwd = _text.Show("FTP password (blank = keep current)", "", allowEmpty: true) ?? "";
        var remoteIn = _text.Show("Remote path", current?.RemotePath ?? "/", allowEmpty: true);
        var remote = string.IsNullOrWhiteSpace(remoteIn) ? "/" : remoteIn!;

        // Keep the existing (encrypted) password if the user left it blank.
        var encrypted = string.IsNullOrEmpty(pwd) && current is not null
            ? current.EncryptedPassword
            : _ftp.Protect(pwd);

        var name = string.IsNullOrWhiteSpace(user) ? host! : $"{user}@{host}";
        _ftp.Save(project, new FtpProfile(name, host!, port, user, encrypted, remote));
        _status.Success($"FTP connection for '{project}' saved.");
        _status.Pause();
    }

    private void EditSql(string project, SqlProfile? current)
    {
        _screen.Clear();
        _screen.DrawCentredTitle(1, $"{project} — SQL connection", Theme.HeaderFg);
        Console.SetCursorPosition(0, 3);

        var server = _text.Show("SQL server (host,port)", current?.Server);
        if (string.IsNullOrWhiteSpace(server)) return;
        var database = _text.Show("Database name", current?.Database);
        if (string.IsNullOrWhiteSpace(database)) return;
        var user = _text.Show("SQL user", current?.User);
        if (string.IsNullOrWhiteSpace(user)) return;
        var pwd = _text.Show("SQL password (blank = keep current)", "", allowEmpty: true) ?? "";

        var encrypted = string.IsNullOrEmpty(pwd) && current is not null
            ? current.EncryptedPassword
            : _sql.Protect(pwd);

        var name = $"{user}@{server}";
        _sql.Save(project, new SqlProfile(name, server!, database!, user!, encrypted));
        _status.Success($"SQL connection for '{project}' saved.");
        _status.Pause();
    }
}
