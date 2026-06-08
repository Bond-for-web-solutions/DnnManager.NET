using DnnManager.Application.Abstractions;
using DnnManager.Domain;
using DnnManager.Infrastructure.Processes;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace DnnManager.Infrastructure.Sql;

/// <summary>
/// Wraps the <c>SqlPackage</c> CLI to export an (Azure) SQL database to a .bacpac and import it
/// into a local SQL Server. SqlPackage is the supported way to copy an Azure SQL Database, which
/// cannot produce a native .bak.
/// </summary>
public sealed class SqlPackageService : IBacpacService
{
    private readonly ProcessRunner _proc;
    private readonly ILogger<SqlPackageService> _log;

    public SqlPackageService(ProcessRunner proc, ILogger<SqlPackageService> log)
    {
        _proc = proc; _log = log;
    }

    public string InstallHint =>
        "SqlPackage was not found. Install it with:  dotnet tool install -g microsoft.sqlpackage";

    // The .NET (Core) build of SqlPackage throws "4096 (0x1000) is an invalid culture
    // identifier" because ICU can't resolve that custom-locale LCID a SQL collation maps to.
    // Forcing the .NET globalization backend to Windows NLS (instead of ICU) resolves such
    // locales the way the OS does. (Invariant mode is explicitly rejected by SqlPackage.)
    private static readonly Dictionary<string, string?> SqlPackageEnv = new()
    {
        ["DOTNET_SYSTEM_GLOBALIZATION_USENLS"] = "true"
    };

    public bool IsAvailable() => ResolveExe() is not null;

    /// <summary>Finds the SqlPackage executable: the dotnet global tool location, then PATH.</summary>
    private static string? ResolveExe()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var toolPath = Path.Combine(home, ".dotnet", "tools", "sqlpackage.exe");
        if (File.Exists(toolPath)) return toolPath;

        // Fall back to PATH lookup via `where`.
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("where", "sqlpackage")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return null;
            var outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            var first = outp.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
            return string.IsNullOrEmpty(first) ? null : first;
        }
        catch
        {
            return null;
        }
    }

    public async Task<Result> ExportAsync(SiteSqlConnection source, string bacpacPath, IProgressReporter reporter, CancellationToken ct)
    {
        var exe = ResolveExe();
        if (exe is null) return Result.Fail(InstallHint);

        var cs = new SqlConnectionStringBuilder
        {
            DataSource = NormalizeServer(source.Server),
            InitialCatalog = source.Database,
            UserID = source.User,
            Password = source.Password,
            Encrypt = true,
            TrustServerCertificate = true,
            ConnectTimeout = 60
        }.ConnectionString;

        var args = new[]
        {
            "/Action:Export",
            $"/SourceConnectionString:{cs}",
            $"/TargetFile:{bacpacPath}",
            "/OverwriteFiles:True"
        };

        reporter.Step($"Exporting [{source.Database}] from {NormalizeServer(source.Server)} (BACPAC)");
        reporter.Info("This can take several minutes for a large database…");
        var r = await _proc.RunAsync(exe, args, ct, SqlPackageEnv);
        if (!r.Success)
        {
            _log.LogError("SqlPackage export failed: {Err}\n{Out}", r.StdErr, r.StdOut);
            TryDelete(bacpacPath); // don't leave a partial/zero-byte .bacpac behind for a later import to pick.
            return Result.Fail($"BACPAC export failed: {Tail(r.StdErr, r.StdOut)}");
        }
        if (!File.Exists(bacpacPath) || new FileInfo(bacpacPath).Length == 0)
        {
            TryDelete(bacpacPath);
            return Result.Fail("BACPAC export reported success but produced no usable file.");
        }
        reporter.Success($"Exported BACPAC ({new FileInfo(bacpacPath).Length / 1024d / 1024d:N1} MB).");
        return Result.Ok();
    }

    public async Task<Result> ImportAsync(string targetServer, string saUser, string saPassword,
        string databaseName, string bacpacPath, IProgressReporter reporter, CancellationToken ct,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        var exe = ResolveExe();
        if (exe is null) return Result.Fail(InstallHint);

        var cs = new SqlConnectionStringBuilder
        {
            DataSource = targetServer,
            InitialCatalog = databaseName,
            UserID = saUser,
            Password = saPassword,
            Encrypt = true,
            TrustServerCertificate = true,
            ConnectTimeout = 60
        }.ConnectionString;

        var args = new List<string>
        {
            "/Action:Import",
            $"/TargetConnectionString:{cs}",
            $"/SourceFile:{bacpacPath}"
        };
        if (properties is not null)
            foreach (var kv in properties)
                args.Add($"/p:{kv.Key}={kv.Value}");

        reporter.Step($"Importing BACPAC into [{databaseName}]");
        reporter.Info("This can take several minutes…");
        var r = await _proc.RunAsync(exe, args, ct, SqlPackageEnv);
        if (!r.Success)
        {
            _log.LogError("SqlPackage import failed: {Err}\n{Out}", r.StdErr, r.StdOut);
            return Result.Fail($"BACPAC import failed: {Tail(r.StdErr, r.StdOut)}");
        }
        reporter.Success("BACPAC imported.");
        return Result.Ok();
    }

    // SqlPackage/SqlClient accept "host,port"; drop the optional "tcp:" prefix.
    private static string NormalizeServer(string server)
    {
        var s = server.Trim();
        return s.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase) ? s[4..] : s;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
    }

    private static string Tail(string err, string outp)
    {
        var text = string.IsNullOrWhiteSpace(err) ? outp : err;
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        return lines.Count == 0 ? "(no output)" : string.Join(" | ", lines.TakeLast(3));
    }
}
