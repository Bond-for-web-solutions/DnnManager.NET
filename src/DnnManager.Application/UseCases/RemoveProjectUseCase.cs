using DnnManager.Application.Abstractions;
using DnnManager.Application.Configuration;
using DnnManager.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DnnManager.Application.UseCases;

public sealed class RemoveProjectUseCase
{
    private readonly AppOptions _opts;
    private readonly IProjectRepository _projects;
    private readonly IIisManager _iis;
    private readonly ISqlServerService _sql;
    private readonly IEnvFileService _envFiles;
    private readonly IUserPrompt _prompt;
    private readonly ILogger<RemoveProjectUseCase> _log;

    public RemoveProjectUseCase(
        IOptions<AppOptions> opts,
        IProjectRepository projects,
        IIisManager iis,
        ISqlServerService sql,
        IEnvFileService envFiles,
        IUserPrompt prompt,
        ILogger<RemoveProjectUseCase> log)
    {
        _opts = opts.Value;
        _projects = projects;
        _iis = iis;
        _sql = sql;
        _envFiles = envFiles;
        _prompt = prompt;
        _log = log;
    }

    public async Task<Result> ExecuteAsync(string projectName, IProgressReporter reporter, CancellationToken ct)
    {
        try
        {
            var project = _projects.Build(projectName);
            var dropDb = await _prompt.ConfirmAsync("Also drop the project's database?", false, ct);
            if (!await _prompt.ConfirmAsync($"Remove project '{projectName}' permanently?", false, ct))
                return Result.Fail("Aborted by user.");

            reporter.Step("Step 1: Remove IIS site & pool");
            _iis.RemoveSite(projectName);

            if (dropDb)
            {
                reporter.Step("Step 2: Drop project database");
                var env = _envFiles.Read(project.EnvFile);
                var db = new DatabaseConfig(
                    "localhost",
                    env.GetValueOrDefault("DB_NAME", projectName + _opts.Docker.DefaultDbNameSuffix),
                    env.GetValueOrDefault("DB_USER", projectName + _opts.Docker.DefaultDbUserSuffix),
                    env.GetValueOrDefault("DB_PASSWORD", _opts.Docker.DefaultDbPassword),
                    _opts.Docker.Collation,
                    int.TryParse(env.GetValueOrDefault("SQLSERVER_PORT", ""), out var p) ? p : _opts.Docker.DefaultPort,
                    project.BackupDirectory);
                await _sql.DropDatabaseAndUserAsync(db, ct);
            }

            reporter.Step("Step 3: Delete project directory");
            if (Directory.Exists(project.ProjectDirectory))
            {
                try
                {
                    Directory.Delete(project.ProjectDirectory, recursive: true);
                    reporter.Success($"Deleted {project.ProjectDirectory}");
                }
                catch (Exception ex)
                {
                    reporter.Fail($"Could not fully delete: {ex.Message}");
                }
            }

            reporter.Step("Removal complete");
            return Result.Ok();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Remove failed");
            return Result.Fail(ex.Message);
        }
    }
}
