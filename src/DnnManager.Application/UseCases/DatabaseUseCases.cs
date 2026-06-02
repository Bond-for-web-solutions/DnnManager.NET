using DnnManager.Application.Abstractions;
using DnnManager.Application.Configuration;
using DnnManager.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DnnManager.Application.UseCases;

public sealed class ExportDatabaseUseCase
{
    private readonly AppOptions _opts;
    private readonly IProjectRepository _projects;
    private readonly IEnvFileService _envFiles;
    private readonly ISqlServerService _sql;
    private readonly ILogger<ExportDatabaseUseCase> _log;

    public ExportDatabaseUseCase(
        IOptions<AppOptions> opts,
        IProjectRepository projects,
        IEnvFileService envFiles,
        ISqlServerService sql,
        ILogger<ExportDatabaseUseCase> log)
    {
        _opts = opts.Value; _projects = projects; _envFiles = envFiles; _sql = sql; _log = log;
    }

    public async Task<Result> ExecuteAsync(string projectName, DnnEnvironment env, IProgressReporter reporter, CancellationToken ct)
    {
        try
        {
            var p = _projects.Build(projectName);
            var envPath = env == DnnEnvironment.Developer ? p.DeveloperEnvFile : p.ProductionEnvFile;
            if (!File.Exists(envPath))
                return Result.Fail($"Env file missing: {envPath}");

            var cfg = _envFiles.Read(envPath);
            var dbName = cfg.GetValueOrDefault("DB_NAME") ?? "";
            if (string.IsNullOrEmpty(dbName)) return Result.Fail("DB_NAME not set in env file.");

            if (env != DnnEnvironment.Developer)
            {
                reporter.Info("Production export is delegated to the remote SQL Server.");
                reporter.Info("Edit/extend the use case to call your remote BACKUP DATABASE flow.");
                return Result.Fail("Production export not implemented in this build.");
            }

            Directory.CreateDirectory(p.BackupDirectory);
            var name = $"developer_{DateTime.Now:yyyyMMdd_HHmmss}.bak";
            reporter.Step($"Backing up [{dbName}]");
            var backup = await _sql.BackupDatabaseLocalAsync(dbName, name, ct);
            if (!backup.Success || backup.Value is null) return Result.Fail(backup.Error ?? "Backup failed.");

            var dest = Path.Combine(p.BackupDirectory, name);
            File.Copy(backup.Value, dest, overwrite: true);
            reporter.Success($"Export complete: {dest}");
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Export failed");
            return Result.Fail(ex.Message);
        }
    }
}

public sealed class ImportDatabaseUseCase
{
    private readonly IProjectRepository _projects;
    private readonly IEnvFileService _envFiles;
    private readonly ISqlServerService _sql;
    private readonly IUserPrompt _prompt;
    private readonly AppOptions _opts;
    private readonly ILogger<ImportDatabaseUseCase> _log;

    public ImportDatabaseUseCase(
        IProjectRepository projects, IEnvFileService envFiles, ISqlServerService sql,
        IUserPrompt prompt, IOptions<AppOptions> opts, ILogger<ImportDatabaseUseCase> log)
    {
        _projects = projects; _envFiles = envFiles; _sql = sql; _prompt = prompt; _opts = opts.Value; _log = log;
    }

    public async Task<Result> ExecuteAsync(string projectName, DnnEnvironment env, string backupFilePath,
        IProgressReporter reporter, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(backupFilePath)) return Result.Fail($"Backup file not found: {backupFilePath}");
            if (env != DnnEnvironment.Developer)
                return Result.Fail("Production import not implemented in this build.");

            if (!await _prompt.ConfirmAsync($"Restore from {Path.GetFileName(backupFilePath)} (overwrites database)?", false, ct))
                return Result.Fail("Aborted by user.");

            var p = _projects.Build(projectName);
            var cfg = _envFiles.Read(p.DeveloperEnvFile);
            var db = new DatabaseConfig(
                cfg.GetValueOrDefault("DB_SERVER", "localhost"),
                cfg.GetValueOrDefault("DB_NAME", projectName + _opts.Docker.DefaultDbNameSuffix),
                cfg.GetValueOrDefault("DB_USER", projectName + _opts.Docker.DefaultDbUserSuffix),
                cfg.GetValueOrDefault("DB_PASSWORD", _opts.Docker.DefaultDbPassword),
                _opts.Docker.Collation,
                int.TryParse(cfg.GetValueOrDefault("SQLSERVER_PORT", ""), out var pp) ? pp : _opts.Docker.DefaultPort,
                p.BackupDirectory);

            reporter.Step($"Restoring [{db.DatabaseName}]");
            return await _sql.RestoreDatabaseLocalAsync(db, backupFilePath, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Import failed");
            return Result.Fail(ex.Message);
        }
    }
}
